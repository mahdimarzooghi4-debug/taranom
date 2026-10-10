using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.ProofingJournal.Postgres;

// Strictly isolated synthetic PostgreSQL exercise; NOT a deployable identity,
// membership or KYC adapter. No real applicant identifier is permitted.
// The review commands contain only enums, revisions, booleans and a random UUID.
// Table names, roles and schema are isolated to the ephemeral CI database.
internal sealed class PgProofingCaseJournal(NpgsqlDataSource dataSource)
{
    private static void CheckSyntheticCase(string caseRef)
    {
        if (string.IsNullOrWhiteSpace(caseRef) || caseRef.Length > 100 ||
            !caseRef.StartsWith("test:", StringComparison.Ordinal) ||
            caseRef.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ':' and '-' and '_'))
            throw new InvalidOperationException("TEST_PROOFING_CASE_REF_REQUIRED");
    }

    internal static async Task InitializeIsolatedTestSchemaAsync(
        NpgsqlDataSource schemaOwner, CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS internal_test_proofing_case (
                case_ref text PRIMARY KEY,
                case_revision bigint NOT NULL DEFAULT 0 CHECK (case_revision >= 0),
                identity_revision bigint NOT NULL DEFAULT 0 CHECK (identity_revision >= 0),
                review_code integer NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS internal_test_proofing_event (
                case_ref text NOT NULL REFERENCES internal_test_proofing_case(case_ref),
                case_revision bigint NOT NULL CHECK (case_revision > 0),
                identity_revision bigint NOT NULL CHECK (identity_revision >= 0),
                request_ref uuid NOT NULL,
                command_json text NOT NULL,
                review_code integer NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY(case_ref,case_revision),
                UNIQUE(case_ref,request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_proofing_audit (
                case_ref text NOT NULL,
                case_revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                event_digest text NOT NULL,
                occurred_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(case_ref,case_revision),
                FOREIGN KEY(case_ref,case_revision)
                    REFERENCES internal_test_proofing_event(case_ref,case_revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_proofing_immutable()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'TEST_PROOFING_APPEND_ONLY'
                    USING ERRCODE = 'P0001';
            END;
            $$;
            CREATE OR REPLACE FUNCTION internal_test_proofing_audit_guard()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW.case_ref LIKE 'test:abort-%' THEN
                        RAISE EXCEPTION 'TEST_PROOFING_AUDIT_UNAVAILABLE'
                            USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END IF;
                RAISE EXCEPTION 'TEST_PROOFING_APPEND_ONLY'
                    USING ERRCODE = 'P0001';
            END;
            $$;
            DROP TRIGGER IF EXISTS internal_test_proofing_event_immutable
                ON internal_test_proofing_event;
            CREATE TRIGGER internal_test_proofing_event_immutable
                BEFORE UPDATE OR DELETE ON internal_test_proofing_event
                FOR EACH ROW EXECUTE FUNCTION internal_test_proofing_immutable();
            DROP TRIGGER IF EXISTS internal_test_proofing_audit_immutable
                ON internal_test_proofing_audit;
            CREATE TRIGGER internal_test_proofing_audit_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON internal_test_proofing_audit
                FOR EACH ROW EXECUTE FUNCTION internal_test_proofing_audit_guard();
            """;
        await using var connection = await schemaOwner.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    // INSERT + row lock serializes all writers to a synthetic case across
    // replicas. The audit, event and state must commit in the SAME transaction.
    // No state/event is committed when idempotency or audit insertion fails.
    internal async Task<ProofingCaseChange> ApplyAsync(
        string caseRef, ProofingCaseCommand input, CancellationToken ct = default)
    {
        CheckSyntheticCase(caseRef);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var init = new NpgsqlCommand("""
            INSERT INTO internal_test_proofing_case (case_ref) VALUES (@case)
            ON CONFLICT DO NOTHING
            """, connection, tx))
        {
            init.Parameters.AddWithValue("case", caseRef);
            await init.ExecuteNonQueryAsync(ct);
        }

        var current = await RehydrateLockedAsync(connection, tx, caseRef, ct);
        var change = ProofingCaseJournal.Apply(current, input);
        if (change.Outcome != ProofingJournalOutcome.Applied)
            return change; // disposing an uncommitted tx rolls back any initial row

        var snapshot = change.Snapshot;
        var json = JsonSerializer.Serialize(input);
        var digest = Digest(caseRef, snapshot.CaseRevision,
            snapshot.IdentityRevision, json, snapshot.Review);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO internal_test_proofing_event
              (case_ref,case_revision,identity_revision,request_ref,
               command_json,review_code,event_digest)
            VALUES (@case,@revision,@identity,@request,@json,@review,@digest)
            """, connection, tx))
        {
            insert.Parameters.AddWithValue("case", caseRef);
            insert.Parameters.AddWithValue("revision", snapshot.CaseRevision);
            insert.Parameters.AddWithValue("identity", snapshot.IdentityRevision);
            insert.Parameters.AddWithValue("request", input.RequestId);
            insert.Parameters.AddWithValue("json", json);
            insert.Parameters.AddWithValue("review", (int)snapshot.Review);
            insert.Parameters.AddWithValue("digest", digest);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_proofing_audit
                (case_ref,case_revision,request_ref,event_digest)
            VALUES (@case,@revision,@request,@digest)
            """, connection, tx))
        {
            audit.Parameters.AddWithValue("case", caseRef);
            audit.Parameters.AddWithValue("revision", snapshot.CaseRevision);
            audit.Parameters.AddWithValue("request", input.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_proofing_case
            SET case_revision=@revision,identity_revision=@identity,review_code=@review
            WHERE case_ref=@case AND case_revision=@expected
            """, connection, tx))
        {
            update.Parameters.AddWithValue("case", caseRef);
            update.Parameters.AddWithValue("expected", current.CaseRevision);
            update.Parameters.AddWithValue("revision", snapshot.CaseRevision);
            update.Parameters.AddWithValue("identity", snapshot.IdentityRevision);
            update.Parameters.AddWithValue("review", (int)snapshot.Review);
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("TEST_PROOFING_CAS_FAILED_CLOSED");
        }

        await tx.CommitAsync(ct);
        return change;
    }

    internal async Task<ProofingCaseSnapshot> ReadAsync(
        string caseRef, CancellationToken ct = default)
    {
        CheckSyntheticCase(caseRef);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var current = await RehydrateLockedAsync(connection, tx, caseRef, ct, allowMissing: true);
        await tx.CommitAsync(ct);
        return current;
    }

    // Replay every committed event and verify its paired audit, digest and
    // projected current state. This detects silent history/state divergence.
    private static async Task<ProofingCaseSnapshot> RehydrateLockedAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, string caseRef,
        CancellationToken ct, bool allowMissing = false)
    {
        long expectedRevision, expectedIdentity;
        int expectedReview;
        await using (var state = new NpgsqlCommand("""
            SELECT case_revision,identity_revision,review_code
            FROM internal_test_proofing_case WHERE case_ref=@case FOR UPDATE
            """, connection, tx))
        {
            state.Parameters.AddWithValue("case", caseRef);
            await using var reader = await state.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                if (allowMissing) return ProofingCaseSnapshot.Empty;
                throw new InvalidOperationException("TEST_PROOFING_STATE_MISSING");
            }
            expectedRevision = reader.GetInt64(0);
            expectedIdentity = reader.GetInt64(1);
            expectedReview = reader.GetInt32(2);
        }

        var result = ProofingCaseSnapshot.Empty;
        await using (var history = new NpgsqlCommand("""
            SELECT e.case_revision,e.identity_revision,e.request_ref,e.command_json,
                   e.review_code,e.event_digest,a.request_ref,a.event_digest
            FROM internal_test_proofing_event e
            LEFT JOIN internal_test_proofing_audit a
                 ON a.case_ref=e.case_ref AND a.case_revision=e.case_revision
            WHERE e.case_ref=@case ORDER BY e.case_revision
            """, connection, tx))
        {
            history.Parameters.AddWithValue("case", caseRef);
            await using var reader = await history.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(6) || reader.IsDBNull(7))
                    throw new InvalidOperationException("TEST_PROOFING_JOURNAL_CORRUPT");

                ProofingCaseCommand command;
                try
                {
                    command = JsonSerializer.Deserialize<ProofingCaseCommand>(reader.GetString(3));
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("TEST_PROOFING_JOURNAL_CORRUPT");
                }
                var applied = ProofingCaseJournal.Apply(result, command);
                var next = applied.Snapshot;
                if (applied.Outcome != ProofingJournalOutcome.Applied ||
                    next.CaseRevision != reader.GetInt64(0) ||
                    next.IdentityRevision != reader.GetInt64(1) ||
                    next.Review != (ProofingReviewState)reader.GetInt32(4) ||
                    command.RequestId != reader.GetGuid(2) ||
                    command.RequestId != reader.GetGuid(6) ||
                    reader.GetString(5) != reader.GetString(7) ||
                    reader.GetString(5) != Digest(caseRef, next.CaseRevision,
                        next.IdentityRevision, reader.GetString(3), next.Review))
                    throw new InvalidOperationException("TEST_PROOFING_JOURNAL_CORRUPT");
                result = next;
            }
        }
        if (result.CaseRevision != expectedRevision ||
            result.IdentityRevision != expectedIdentity ||
            (int)result.Review != expectedReview)
            throw new InvalidOperationException("TEST_PROOFING_JOURNAL_CORRUPT");
        return result;
    }

    private static string Digest(string caseRef, long revision, long identity,
        string json, ProofingReviewState review)
    {
        var canonical = string.Join("|", "taranom.test-proofing.v1", caseRef,
            revision.ToString(CultureInfo.InvariantCulture),
            identity.ToString(CultureInfo.InvariantCulture),
            ((int)review).ToString(CultureInfo.InvariantCulture), json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
