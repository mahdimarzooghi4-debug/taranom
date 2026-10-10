using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using IntakeReducer = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// STRICTLY TEST-ONLY review intake persistence. Every identifier must be test:*.
// No authoritative physician/manager verification, IdP, PHI, human decision,
// provider activation, contract validation or Production migration exists.
internal sealed class PgClinicalEvidenceJournal(NpgsqlDataSource dataSource)
{
    private static void ValidateCase(string center, string subject, EvidenceSubjectKind kind)
    {
        if (!IntakeReducer.IsSyntheticRef(center) ||
            !IntakeReducer.IsSyntheticRef(subject) || !Enum.IsDefined(kind))
            throw new InvalidOperationException("TEST_EVIDENCE_CASE_REQUIRED");
    }

    internal static async Task InitializeIsolatedTestSchemaAsync(
        NpgsqlDataSource owner, CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS internal_test_review_case (
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL CHECK (subject_kind IN (1, 2)),
                current_revision bigint NOT NULL DEFAULT 0
                    CHECK (current_revision >= 0),
                PRIMARY KEY (center_ref, subject_ref, subject_kind)
            );
            CREATE TABLE IF NOT EXISTS internal_test_review_event (
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                event_revision bigint NOT NULL CHECK (event_revision > 0),
                request_ref uuid NOT NULL,
                command_json text NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY(center_ref,subject_ref,subject_kind,event_revision),
                UNIQUE(center_ref,subject_ref,subject_kind,request_ref),
                FOREIGN KEY(center_ref,subject_ref,subject_kind) REFERENCES
                    internal_test_review_case(center_ref,subject_ref,subject_kind)
            );
            CREATE TABLE IF NOT EXISTS internal_test_review_work_item (
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                event_revision bigint NOT NULL,
                evidence_kind integer NOT NULL CHECK(evidence_kind IN (1,2,3)),
                claimant_ref text NOT NULL,
                opaque_evidence_ref text NOT NULL,
                PRIMARY KEY(center_ref,subject_ref,subject_kind,event_revision),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,event_revision)
                  REFERENCES internal_test_review_event
                    (center_ref,subject_ref,subject_kind,event_revision)
            );
            CREATE TABLE IF NOT EXISTS internal_test_review_audit (
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                event_revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                event_digest text NOT NULL,
                observed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(center_ref,subject_ref,subject_kind,event_revision),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,event_revision)
                  REFERENCES internal_test_review_event
                    (center_ref,subject_ref,subject_kind,event_revision)
            );
            CREATE TABLE IF NOT EXISTS internal_test_review_denial (
                attempt_ref uuid PRIMARY KEY,
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                request_ref uuid NOT NULL,
                outcome_code integer NOT NULL CHECK(outcome_code IN (3,4,5,6)),
                seen_revision bigint NOT NULL CHECK(seen_revision >= 0),
                observed_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE OR REPLACE FUNCTION internal_test_review_append_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              IF TG_OP <> 'INSERT' THEN
                  RAISE EXCEPTION 'TEST_EVIDENCE_APPEND_ONLY' USING ERRCODE = 'P0001';
              END IF;
              IF TG_TABLE_NAME = 'internal_test_review_audit' AND
                 NEW.center_ref LIKE 'test:abort-audit-%' THEN
                  RAISE EXCEPTION 'TEST_EVIDENCE_AUDIT_UNAVAILABLE' USING ERRCODE = 'P0001';
              END IF;
              IF TG_TABLE_NAME = 'internal_test_review_denial' AND
                 NEW.center_ref LIKE 'test:abort-denial-%' THEN
                  RAISE EXCEPTION 'TEST_EVIDENCE_DENIAL_AUDIT_UNAVAILABLE' USING ERRCODE = 'P0001';
              END IF;
              RETURN NEW;
            END; $body$;
            DROP TRIGGER IF EXISTS internal_test_review_event_append
              ON internal_test_review_event;
            CREATE TRIGGER internal_test_review_event_append
              BEFORE UPDATE OR DELETE ON internal_test_review_event
              FOR EACH ROW EXECUTE FUNCTION internal_test_review_append_guard();
            DROP TRIGGER IF EXISTS internal_test_review_item_append
              ON internal_test_review_work_item;
            CREATE TRIGGER internal_test_review_item_append
              BEFORE UPDATE OR DELETE ON internal_test_review_work_item
              FOR EACH ROW EXECUTE FUNCTION internal_test_review_append_guard();
            DROP TRIGGER IF EXISTS internal_test_review_audit_append
              ON internal_test_review_audit;
            CREATE TRIGGER internal_test_review_audit_append
              BEFORE INSERT OR UPDATE OR DELETE ON internal_test_review_audit
              FOR EACH ROW EXECUTE FUNCTION internal_test_review_append_guard();
            DROP TRIGGER IF EXISTS internal_test_review_denial_append
              ON internal_test_review_denial;
            CREATE TRIGGER internal_test_review_denial_append
              BEFORE INSERT OR UPDATE OR DELETE ON internal_test_review_denial
              FOR EACH ROW EXECUTE FUNCTION internal_test_review_append_guard();
            """;
        await using var cn = await owner.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<EvidenceIntakeDecision> SubmitAsync(
        string center, string subject, EvidenceSubjectKind kind,
        EvidenceSubmission submission, CancellationToken ct = default)
    {
        ValidateCase(center, subject, kind);
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        await ExecuteAsync(cn, tx, """
            INSERT INTO internal_test_review_case(center_ref,subject_ref,subject_kind)
            VALUES (@center,@subject,@kind) ON CONFLICT DO NOTHING
            """, ct, ("center", center), ("subject", subject), ("kind", (object)(int)kind));

        var current = await ReplayLockedAsync(cn, tx, center, subject, kind, ct);
        var decision = IntakeReducer.Submit(current, submission);
        if (decision.Outcome != EvidenceIntakeOutcome.ReviewWorkItemCreated)
        {
            // Roll back the provisional case on invalid/denied commands.
            await tx.RollbackAsync(ct);
            if (decision.Outcome != EvidenceIntakeOutcome.Replayed)
                await RecordDenialAsync(cn, center, subject, kind, submission.RequestId,
                    decision.Outcome, current.Revision, ct);
            return decision;
        }

        var next = decision.Snapshot;
        var json = JsonSerializer.Serialize(submission);
        var digest = Digest(center, subject, kind, next.Revision, json);
        await ExecuteAsync(cn, tx, """
            INSERT INTO internal_test_review_event
              (center_ref,subject_ref,subject_kind,event_revision,
               request_ref,command_json,event_digest)
            VALUES (@center,@subject,@kind,@rev,@request,@json,@digest)
            """, ct, ("center", center), ("subject", subject), ("kind", (object)(int)kind),
            ("rev", (object)next.Revision), ("request", (object)submission.RequestId),
            ("json", json), ("digest", digest));

        // A pending review item and its event MUST commit atomically.
        await ExecuteAsync(cn, tx, """
            INSERT INTO internal_test_review_work_item
              (center_ref,subject_ref,subject_kind,event_revision,
               evidence_kind,claimant_ref,opaque_evidence_ref)
            VALUES (@center,@subject,@kind,@rev,@evidence,@claimant,@opaque)
            """, ct, ("center", center), ("subject", subject), ("kind", (object)(int)kind),
            ("rev", (object)next.Revision), ("evidence", (object)(int)submission.EvidenceKind),
            ("claimant", submission.SubmittingActorRef!), ("opaque", submission.OpaqueEvidenceRef!));

        await ExecuteAsync(cn, tx, """
            INSERT INTO internal_test_review_audit
              (center_ref,subject_ref,subject_kind,event_revision,request_ref,event_digest)
            VALUES (@center,@subject,@kind,@rev,@request,@digest)
            """, ct, ("center", center), ("subject", subject), ("kind", (object)(int)kind),
            ("rev", (object)next.Revision), ("request", (object)submission.RequestId),
            ("digest", digest));

        var updates = await ExecuteAsync(cn, tx, """
            UPDATE internal_test_review_case SET current_revision=@rev
            WHERE center_ref=@center AND subject_ref=@subject
              AND subject_kind=@kind AND current_revision=@previous
            """, ct, ("rev", (object)next.Revision), ("center", center),
            ("subject", subject), ("kind", (object)(int)kind),
            ("previous", (object)current.Revision));
        if (updates != 1) throw new InvalidOperationException("TEST_EVIDENCE_REVISION_CONFLICT");

        await tx.CommitAsync(ct);
        return decision;
    }

    internal async Task<EvidenceIntakeSnapshot> ReadAsync(
        string center, string subject, EvidenceSubjectKind kind,
        CancellationToken ct = default)
    {
        ValidateCase(center, subject, kind);
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var state = await ReplayLockedAsync(cn, tx, center, subject, kind, ct, allowMissing: true);
        await tx.CommitAsync(ct);
        return state;
    }

    private static async Task<EvidenceIntakeSnapshot> ReplayLockedAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, string center, string subject,
        EvidenceSubjectKind kind, CancellationToken ct, bool allowMissing = false)
    {
        object? value;
        await using (var cmd = new NpgsqlCommand("""
            SELECT current_revision FROM internal_test_review_case
            WHERE center_ref=@center AND subject_ref=@subject AND subject_kind=@kind FOR UPDATE
            """, cn, tx))
        {
            cmd.Parameters.AddWithValue("center", center);
            cmd.Parameters.AddWithValue("subject", subject);
            cmd.Parameters.AddWithValue("kind", (int)kind);
            value = await cmd.ExecuteScalarAsync(ct);
        }

        if (value is null && !allowMissing)
            throw new InvalidOperationException("TEST_EVIDENCE_CASE_NOT_FOUND");
        var state = EvidenceIntakeSnapshot.ForSyntheticSubject(center, subject, kind);
        if (value is null) return state;

        await using (var cmd = new NpgsqlCommand("""
            SELECT e.event_revision,e.request_ref,e.command_json,e.event_digest,
                   w.evidence_kind,w.claimant_ref,w.opaque_evidence_ref,
                   a.request_ref,a.event_digest
            FROM internal_test_review_event e
            LEFT JOIN internal_test_review_work_item w ON
                w.center_ref=e.center_ref AND w.subject_ref=e.subject_ref AND
                w.subject_kind=e.subject_kind AND w.event_revision=e.event_revision
            LEFT JOIN internal_test_review_audit a ON
                a.center_ref=e.center_ref AND a.subject_ref=e.subject_ref AND
                a.subject_kind=e.subject_kind AND a.event_revision=e.event_revision
            WHERE e.center_ref=@center AND e.subject_ref=@subject AND e.subject_kind=@kind
            ORDER BY e.event_revision
            """, cn, tx))
        {
            cmd.Parameters.AddWithValue("center", center);
            cmd.Parameters.AddWithValue("subject", subject);
            cmd.Parameters.AddWithValue("kind", (int)kind);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (Enumerable.Range(4, 5).Any(reader.IsDBNull))
                    throw new InvalidOperationException("TEST_EVIDENCE_HISTORY_CORRUPT");
                EvidenceSubmission command;
                try
                {
                    command = JsonSerializer.Deserialize<EvidenceSubmission>(reader.GetString(2));
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("TEST_EVIDENCE_HISTORY_CORRUPT");
                }
                var changed = IntakeReducer.Submit(state, command);
                if (changed.Outcome != EvidenceIntakeOutcome.ReviewWorkItemCreated ||
                    changed.Snapshot.Revision != reader.GetInt64(0) ||
                    command.RequestId != reader.GetGuid(1) ||
                    command.RequestId != reader.GetGuid(7) ||
                    command.EvidenceKind != (EvidenceKind)reader.GetInt32(4) ||
                    command.SubmittingActorRef != reader.GetString(5) ||
                    command.OpaqueEvidenceRef != reader.GetString(6) ||
                    reader.GetString(3) != reader.GetString(8) ||
                    reader.GetString(3) != Digest(center, subject, kind,
                        reader.GetInt64(0), reader.GetString(2)))
                    throw new InvalidOperationException("TEST_EVIDENCE_HISTORY_CORRUPT");
                state = changed.Snapshot;
            }
        }
        if (state.Revision != (long)value)
            throw new InvalidOperationException("TEST_EVIDENCE_HISTORY_CORRUPT");
        return state;
    }

    private static async Task RecordDenialAsync(NpgsqlConnection cn, string center,
        string subject, EvidenceSubjectKind kind, Guid requestId,
        EvidenceIntakeOutcome outcome, long revision, CancellationToken ct)
    {
        if (outcome is not (EvidenceIntakeOutcome.Invalid or
            EvidenceIntakeOutcome.ConflictingReplay or EvidenceIntakeOutcome.ScopeDenied
            or EvidenceIntakeOutcome.StaleRevision))
            throw new InvalidOperationException("TEST_EVIDENCE_DENIAL_UNSUPPORTED");
        // Denials carry no attached evidence content or identity document.
        await ExecuteAsync(cn, null, """
            INSERT INTO internal_test_review_denial
                (attempt_ref,center_ref,subject_ref,subject_kind,
                 request_ref,outcome_code,seen_revision)
            VALUES (@attempt,@center,@subject,@kind,@request,@outcome,@rev)
            """, ct, ("attempt", (object)Guid.NewGuid()), ("center", center),
            ("subject", subject), ("kind", (object)(int)kind),
            ("request", (object)requestId), ("outcome", (object)(int)outcome),
            ("rev", (object)revision));
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection cn, NpgsqlTransaction? tx,
        string sql, CancellationToken ct, params (string Name, object Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Digest(string center, string subject, EvidenceSubjectKind kind,
        long revision, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-review-intake.v1", center, subject,
            ((int)kind).ToString(CultureInfo.InvariantCulture),
            revision.ToString(CultureInfo.InvariantCulture), json))));
}
