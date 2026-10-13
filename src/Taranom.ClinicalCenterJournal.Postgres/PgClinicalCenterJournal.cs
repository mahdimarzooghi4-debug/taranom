using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.ClinicalCenterGovernance;
using CenterReducer = global::Taranom.ClinicalCenterGovernance.ClinicalCenterJournal;

namespace Taranom.ClinicalCenterJournal.Postgres;

// SYNTHETIC-ONLY / NON-DEPLOYABLE event + relational projection journal.
// It is not a trusted center manager, licensure, medical or identity adapter.
internal sealed class PgClinicalCenterJournal(NpgsqlDataSource dataSource)
{
    private static void ValidateCenter(string centerRef)
    {
        if (!CenterReducer.IsSyntheticRef(centerRef))
            throw new InvalidOperationException("TEST_CENTER_SYNTHETIC_REF_REQUIRED");
    }

    internal static async Task InitializeIsolatedTestSchemaAsync(
        NpgsqlDataSource owner, CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS internal_test_center (
                center_ref text PRIMARY KEY,
                current_version bigint NOT NULL DEFAULT 0 CHECK (current_version >= 0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_center_department (
                center_ref text NOT NULL REFERENCES internal_test_center(center_ref),
                department_ref text NOT NULL,
                PRIMARY KEY (center_ref, department_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_center_doctor (
                center_ref text NOT NULL REFERENCES internal_test_center(center_ref),
                doctor_ref text NOT NULL,
                affiliation_code integer NOT NULL CHECK (affiliation_code IN (1, 2)),
                PRIMARY KEY (center_ref, doctor_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_center_assignment (
                center_ref text NOT NULL,
                doctor_ref text NOT NULL,
                department_ref text NOT NULL,
                PRIMARY KEY (center_ref, doctor_ref, department_ref),
                FOREIGN KEY (center_ref, doctor_ref)
                    REFERENCES internal_test_center_doctor(center_ref, doctor_ref),
                FOREIGN KEY (center_ref, department_ref)
                    REFERENCES internal_test_center_department(center_ref, department_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_center_event (
                center_ref text NOT NULL REFERENCES internal_test_center(center_ref),
                event_version bigint NOT NULL CHECK (event_version > 0),
                request_ref uuid NOT NULL,
                command_json text NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY (center_ref, event_version),
                UNIQUE (center_ref, request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_center_audit (
                center_ref text NOT NULL,
                event_version bigint NOT NULL,
                request_ref uuid NOT NULL,
                event_digest text NOT NULL,
                occurred_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (center_ref, event_version),
                FOREIGN KEY (center_ref, event_version)
                    REFERENCES internal_test_center_event(center_ref, event_version)
            );
            CREATE TABLE IF NOT EXISTS internal_test_center_denial (
                attempt_ref uuid PRIMARY KEY,
                center_ref text NOT NULL,
                request_ref uuid NOT NULL,
                outcome_code integer NOT NULL CHECK (outcome_code IN (3,4,5,6,7,8,9)),
                observed_version bigint NOT NULL CHECK (observed_version >= 0),
                occurred_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE OR REPLACE FUNCTION internal_test_center_guard()
            RETURNS trigger LANGUAGE plpgsql AS $guard$
            BEGIN
                IF TG_OP = 'INSERT' AND TG_TABLE_NAME='internal_test_center_audit' AND
                   NEW.center_ref LIKE 'test:abort-audit-%' THEN
                    RAISE EXCEPTION 'TEST_CENTER_AUDIT_UNAVAILABLE' USING ERRCODE='P0001';
                END IF;
                IF TG_OP = 'INSERT' AND TG_TABLE_NAME='internal_test_center_denial' AND
                   NEW.center_ref LIKE 'test:abort-denial-%' THEN
                    RAISE EXCEPTION 'TEST_CENTER_DENIAL_AUDIT_UNAVAILABLE' USING ERRCODE='P0001';
                END IF;
                IF TG_OP = 'INSERT' THEN RETURN NEW; END IF;
                RAISE EXCEPTION 'TEST_CENTER_APPEND_ONLY' USING ERRCODE='P0001';
            END;
            $guard$;
            DROP TRIGGER IF EXISTS internal_test_center_event_guard ON internal_test_center_event;
            CREATE TRIGGER internal_test_center_event_guard
                BEFORE UPDATE OR DELETE ON internal_test_center_event
                FOR EACH ROW EXECUTE FUNCTION internal_test_center_guard();
            DROP TRIGGER IF EXISTS internal_test_center_audit_guard ON internal_test_center_audit;
            CREATE TRIGGER internal_test_center_audit_guard
                BEFORE INSERT OR UPDATE OR DELETE ON internal_test_center_audit
                FOR EACH ROW EXECUTE FUNCTION internal_test_center_guard();
            DROP TRIGGER IF EXISTS internal_test_center_denial_guard ON internal_test_center_denial;
            CREATE TRIGGER internal_test_center_denial_guard
                BEFORE INSERT OR UPDATE OR DELETE ON internal_test_center_denial
                FOR EACH ROW EXECUTE FUNCTION internal_test_center_guard();
            """;
        await using var connection = await owner.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task<CenterChange> ApplyAsync(
        string centerRef, CenterCommand command, CancellationToken ct = default)
    {
        ValidateCenter(centerRef);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await using (var init = new NpgsqlCommand("""
            INSERT INTO internal_test_center(center_ref) VALUES (@center)
            ON CONFLICT DO NOTHING
            """, connection, tx))
        {
            init.Parameters.AddWithValue("center", centerRef);
            await init.ExecuteNonQueryAsync(ct);
        }

        var current = await RehydrateVerifiedAsync(connection, tx, centerRef, ct, lockCenterRow: true);
        var changed = CenterReducer.Apply(current, command);
        if (changed.Outcome != CenterOutcome.Applied)
        {
            // Never materialize an otherwise absent center on a denial.
            await tx.RollbackAsync(ct);
            if (changed.Outcome != CenterOutcome.Replayed)
                await RecordDeniedAsync(connection, centerRef, command.RequestId,
                    changed.Outcome, current.Version, ct);
            return changed;
        }

        var next = changed.Snapshot;
        var json = JsonSerializer.Serialize(command);
        var digest = Digest(centerRef, next.Version, json);

        // All projection, accepted event, audit and CAS changes commit atomically.
        switch (command.Kind)
        {
            case CenterCommandKind.DefineDepartment:
                await RunAsync(connection, tx, """
                    INSERT INTO internal_test_center_department(center_ref,department_ref)
                    VALUES (@center,@department)
                    """, ct, ("center", centerRef), ("department", command.DepartmentRef!));
                break;
            case CenterCommandKind.RecordDoctorAffiliation:
                await RunAsync(connection, tx, """
                    INSERT INTO internal_test_center_doctor(center_ref,doctor_ref,affiliation_code)
                    VALUES (@center,@doctor,1)
                    """, ct, ("center", centerRef), ("doctor", command.DoctorRef!));
                break;
            case CenterCommandKind.AssignDoctorToDepartment:
                await RunAsync(connection, tx, """
                    INSERT INTO internal_test_center_assignment(center_ref,doctor_ref,department_ref)
                    VALUES (@center,@doctor,@department)
                    """, ct, ("center", centerRef), ("doctor", command.DoctorRef!),
                    ("department", command.DepartmentRef!));
                break;
            case CenterCommandKind.EndDoctorAffiliation:
                await RunAsync(connection, tx, """
                    DELETE FROM internal_test_center_assignment
                    WHERE center_ref=@center AND doctor_ref=@doctor
                    """, ct, ("center", centerRef), ("doctor", command.DoctorRef!));
                await RunAsync(connection, tx, """
                    UPDATE internal_test_center_doctor SET affiliation_code=2
                    WHERE center_ref=@center AND doctor_ref=@doctor AND affiliation_code=1
                    """, ct, ("center", centerRef), ("doctor", command.DoctorRef!));
                break;
            default:
                throw new InvalidOperationException("TEST_CENTER_COMMAND_UNSUPPORTED");
        }

        await RunAsync(connection, tx, """
            INSERT INTO internal_test_center_event
                (center_ref,event_version,request_ref,command_json,event_digest)
            VALUES (@center,@version,@request,@json,@digest)
            """, ct, ("center", (object)centerRef), ("version", (object)next.Version),
            ("request", (object)command.RequestId), ("json", (object)json), ("digest", (object)digest));

        await RunAsync(connection, tx, """
            INSERT INTO internal_test_center_audit
                (center_ref,event_version,request_ref,event_digest)
            VALUES (@center,@version,@request,@digest)
            """, ct, ("center", (object)centerRef), ("version", (object)next.Version),
            ("request", (object)command.RequestId), ("digest", (object)digest));

        var written = await RunAsync(connection, tx, """
            UPDATE internal_test_center
            SET current_version=@version
            WHERE center_ref=@center AND current_version=@previous
            """, ct, ("center", (object)centerRef),
            ("version", (object)next.Version), ("previous", (object)current.Version));
        if (written != 1)
            throw new InvalidOperationException("TEST_CENTER_CONCURRENT_CAS_FAILED");

        await tx.CommitAsync(ct);
        return changed;
    }

    internal async Task<CenterSnapshot> ReadAsync(
        string centerRef, CancellationToken ct = default)
    {
        ValidateCenter(centerRef);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // All journal, audit and projection SELECTs must see ONE committed
        // PostgreSQL MVCC snapshot; unlike FOR UPDATE this never acquires a
        // writer row lock. Writers still serialize under their original lock.
        await using var tx = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, ct);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx))
            await readOnly.ExecuteNonQueryAsync(ct);
        var result = await RehydrateVerifiedAsync(connection, tx, centerRef, ct,
            missingAllowed: true, lockCenterRow: false);
        await tx.CommitAsync(ct);
        return result;
    }

    private static async Task RecordDeniedAsync(
        NpgsqlConnection connection, string centerRef, Guid requestId,
        CenterOutcome outcome, long version, CancellationToken ct)
    {
        if (outcome is not (CenterOutcome.Invalid or CenterOutcome.ConflictingReplay
            or CenterOutcome.ScopeDenied or CenterOutcome.EvidenceDenied
            or CenterOutcome.EvidenceMissing or CenterOutcome.StaleRevision
            or CenterOutcome.NotApplicable))
            throw new InvalidOperationException("TEST_CENTER_DENIAL_REASON_INVALID");

        await RunAsync(connection, null, """
            INSERT INTO internal_test_center_denial
                (attempt_ref,center_ref,request_ref,outcome_code,observed_version)
            VALUES (@attempt,@center,@request,@outcome,@version)
            """, ct, ("attempt", Guid.NewGuid()), ("center", centerRef),
            ("request", requestId), ("outcome", (int)outcome), ("version", version));
    }

    private static async Task<CenterSnapshot> RehydrateVerifiedAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, string centerRef,
        CancellationToken ct, bool missingAllowed = false, bool lockCenterRow = true)
    {
        long version;
        await using (var get = new NpgsqlCommand(lockCenterRow ? """
            SELECT current_version FROM internal_test_center
            WHERE center_ref=@center FOR UPDATE
            """ : """
            SELECT current_version FROM internal_test_center
            WHERE center_ref=@center
            """, connection, tx))
        {
            get.Parameters.AddWithValue("center", centerRef);
            var found = await get.ExecuteScalarAsync(ct);
            if (found is null)
            {
                if (missingAllowed) return CenterSnapshot.NewSynthetic(centerRef);
                throw new InvalidOperationException("TEST_CENTER_STATE_NOT_FOUND");
            }
            version = (long)found;
        }

        var state = CenterSnapshot.NewSynthetic(centerRef);
        await using (var history = new NpgsqlCommand("""
            SELECT e.event_version,e.request_ref,e.command_json,e.event_digest,
                   a.request_ref,a.event_digest
            FROM internal_test_center_event e
            LEFT JOIN internal_test_center_audit a
              ON a.center_ref=e.center_ref AND a.event_version=e.event_version
            WHERE e.center_ref=@center ORDER BY e.event_version
            """, connection, tx))
        {
            history.Parameters.AddWithValue("center", centerRef);
            await using var reader = await history.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(4) || reader.IsDBNull(5))
                    throw new InvalidOperationException("TEST_CENTER_JOURNAL_CORRUPT");
                CenterCommand command;
                try
                {
                    command = JsonSerializer.Deserialize<CenterCommand>(reader.GetString(2));
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("TEST_CENTER_JOURNAL_CORRUPT");
                }
                var changed = CenterReducer.Apply(state, command);
                if (changed.Outcome != CenterOutcome.Applied ||
                    changed.Snapshot.Version != reader.GetInt64(0) ||
                    command.RequestId != reader.GetGuid(1) ||
                    command.RequestId != reader.GetGuid(4) ||
                    reader.GetString(3) != reader.GetString(5) ||
                    reader.GetString(3) != Digest(centerRef, reader.GetInt64(0),
                        reader.GetString(2)))
                    throw new InvalidOperationException("TEST_CENTER_JOURNAL_CORRUPT");
                state = changed.Snapshot;
            }
        }

        if (state.Version != version)
            throw new InvalidOperationException("TEST_CENTER_JOURNAL_CORRUPT");

        // The materialized, relational current state must exactly equal an
        // independent replay of accepted history for this ONE center.
        var departmentRefs = await ReadRefsAsync(connection, tx, """
            SELECT department_ref FROM internal_test_center_department
            WHERE center_ref=@center
            """, centerRef, ct);
        if (!departmentRefs.SetEquals(state.Departments))
            throw new InvalidOperationException("TEST_CENTER_PROJECTION_CORRUPT");

        var doctors = new Dictionary<string, DoctorAffiliation>(StringComparer.Ordinal);
        await using (var query = new NpgsqlCommand("""
            SELECT doctor_ref,affiliation_code FROM internal_test_center_doctor
            WHERE center_ref=@center
            """, connection, tx))
        {
            query.Parameters.AddWithValue("center", centerRef);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                doctors.Add(reader.GetString(0), (DoctorAffiliation)reader.GetInt32(1));
        }
        if (doctors.Count != state.Doctors.Count ||
            doctors.Any(p => !state.Doctors.TryGetValue(p.Key, out var value) || value != p.Value))
            throw new InvalidOperationException("TEST_CENTER_PROJECTION_CORRUPT");

        var currentPairs = new HashSet<string>(StringComparer.Ordinal);
        await using (var query = new NpgsqlCommand("""
            SELECT doctor_ref,department_ref FROM internal_test_center_assignment
            WHERE center_ref=@center
            """, connection, tx))
        {
            query.Parameters.AddWithValue("center", centerRef);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                currentPairs.Add(reader.GetString(0) + "|" + reader.GetString(1));
        }
        var reconstructedPairs = state.DoctorDepartments.SelectMany(pair =>
            pair.Value.Select(department => pair.Key + "|" + department)).ToHashSet(StringComparer.Ordinal);
        if (!currentPairs.SetEquals(reconstructedPairs))
            throw new InvalidOperationException("TEST_CENTER_PROJECTION_CORRUPT");

        return state;
    }

    private static async Task<HashSet<string>> ReadRefsAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        string sql, string center, CancellationToken ct)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("center", center);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            values.Add(reader.GetString(0));
        return values;
    }

    private static async Task<int> RunAsync(
        NpgsqlConnection connection, NpgsqlTransaction? tx, string sql,
        CancellationToken ct, params (string Name, object Value)[] args)
    {
        await using var query = new NpgsqlCommand(sql, connection, tx);
        foreach (var (name, value) in args)
            query.Parameters.AddWithValue(name, value);
        return await query.ExecuteNonQueryAsync(ct);
    }

    private static string Digest(string centerRef, long version, string serializedCommand)
    {
        // No user-controlled separator is allowed in synthetic refs.
        var canonical = string.Join("|", "taranom.test-center-journal.v1", centerRef,
            version.ToString(CultureInfo.InvariantCulture), serializedCommand);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
