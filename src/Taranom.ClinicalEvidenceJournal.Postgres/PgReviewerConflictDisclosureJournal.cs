using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using IntakeDomain = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Internal SYNTHETIC-ONLY review conflict disclosure. Neither an assertion
// that a person is independent nor a complete legal conflict-of-interest rule.
internal enum ConflictNoticeOutcome
{
    PotentialRelationshipRecorded = 1,
    Replayed = 2,
    ConflictingReplay = 3,
    StaleRevision = 4,
    Invalid = 5,
    ReviewNoteMissingOrCorrupt = 6
}

internal enum ConflictScreeningStatus
{
    UnknownWithoutDisclosureEvidence = 1,
    PotentialRelationshipRequiresIndependentReview = 2
}

internal readonly record struct ConflictNoticeCommand(
    Guid RequestId, long ExpectedRevision, string CenterRef,
    string SubjectRef, EvidenceSubjectKind SubjectKind, long EvidenceRevision,
    Guid ReviewNoteRequestId, string ReporterClaimRef,
    string RelationshipEvidenceRef);

internal readonly record struct ConflictNoticeRecord(
    long Revision, Guid RequestId, string ReporterClaimRef,
    string RelationshipEvidenceRef);

internal sealed record ConflictNoticeReadModel(
    ConflictScreeningStatus ScreeningStatus,
    IReadOnlyList<ConflictNoticeRecord> Disclosures,
    long Revision);

// This test-only journal records ONLY positive potential-relationship notices.
// Absence of a report is UNKNOWN, NEVER "no conflict" or clearance.
internal sealed class PgReviewerConflictDisclosureJournal(
    NpgsqlDataSource dataSource, PgHumanReviewNoteJournal noteJournal)
{
    internal static async Task InitializeTestSchemaAsync(
        NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_conflict_notice_head(
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                evidence_revision bigint NOT NULL,
                review_note_request_ref uuid NOT NULL,
                current_revision bigint NOT NULL DEFAULT 0 CHECK (current_revision>=0),
                PRIMARY KEY(center_ref,subject_ref,subject_kind,
                            evidence_revision,review_note_request_ref),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,evidence_revision,
                            review_note_request_ref)
                  REFERENCES internal_test_human_note(center_ref,subject_ref,
                      subject_kind,evidence_revision,request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_conflict_notice_event(
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                evidence_revision bigint NOT NULL,
                review_note_request_ref uuid NOT NULL,
                notice_revision bigint NOT NULL CHECK (notice_revision>0),
                request_ref uuid NOT NULL,
                command_json text NOT NULL,
                notice_digest text NOT NULL,
                PRIMARY KEY(center_ref,subject_ref,subject_kind,evidence_revision,
                            review_note_request_ref,notice_revision),
                UNIQUE(center_ref,subject_ref,subject_kind,evidence_revision,
                       review_note_request_ref,request_ref),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,evidence_revision,
                            review_note_request_ref)
                  REFERENCES internal_test_conflict_notice_head(center_ref,subject_ref,
                      subject_kind,evidence_revision,review_note_request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_conflict_notice_audit(
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                evidence_revision bigint NOT NULL,
                review_note_request_ref uuid NOT NULL,
                notice_revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                notice_digest text NOT NULL,
                observed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(center_ref,subject_ref,subject_kind,evidence_revision,
                            review_note_request_ref,notice_revision),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,evidence_revision,
                            review_note_request_ref,notice_revision)
                  REFERENCES internal_test_conflict_notice_event(center_ref,subject_ref,
                      subject_kind,evidence_revision,review_note_request_ref,notice_revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_conflict_notice_append_guard()
            RETURNS trigger LANGUAGE plpgsql AS $guard$
            BEGIN
                IF TG_OP='INSERT' AND TG_TABLE_NAME='internal_test_conflict_notice_audit' AND
                    NEW.center_ref LIKE 'test:abort-coi-audit-%' THEN
                    RAISE EXCEPTION 'TEST_CONFLICT_NOTICE_AUDIT_UNAVAILABLE'
                      USING ERRCODE='P0001';
                END IF;
                IF TG_OP='INSERT' THEN RETURN NEW; END IF;
                RAISE EXCEPTION 'TEST_CONFLICT_NOTICE_APPEND_ONLY' USING ERRCODE='P0001';
            END $guard$;
            DROP TRIGGER IF EXISTS internal_test_conflict_notice_event_guard
                ON internal_test_conflict_notice_event;
            CREATE TRIGGER internal_test_conflict_notice_event_guard
                BEFORE UPDATE OR DELETE ON internal_test_conflict_notice_event
                FOR EACH ROW EXECUTE FUNCTION internal_test_conflict_notice_append_guard();
            DROP TRIGGER IF EXISTS internal_test_conflict_notice_audit_guard
                ON internal_test_conflict_notice_audit;
            CREATE TRIGGER internal_test_conflict_notice_audit_guard
                BEFORE INSERT OR UPDATE OR DELETE ON internal_test_conflict_notice_audit
                FOR EACH ROW EXECUTE FUNCTION internal_test_conflict_notice_append_guard();
            """;
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(ddl, cn);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task<ConflictNoticeOutcome> RecordAsync(
        ConflictNoticeCommand input, CancellationToken ct = default)
    {
        if (!Valid(input)) return ConflictNoticeOutcome.Invalid;

        // This is a verified *synthetic DB lineage*, not verified actor rights.
        if (!await HasImmutableNoteAsync(input, ct))
            return ConflictNoticeOutcome.ReviewNoteMissingOrCorrupt;

        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using (var create = new NpgsqlCommand("""
            INSERT INTO internal_test_conflict_notice_head
              (center_ref,subject_ref,subject_kind,evidence_revision,review_note_request_ref)
            VALUES (@center,@subject,@kind,@evidence,@note)
            ON CONFLICT DO NOTHING
            """, cn, tx))
        {
            AddScope(create, input);
            await create.ExecuteNonQueryAsync(ct);
        }

        long current;
        await using (var locked = new NpgsqlCommand("""
            SELECT current_revision FROM internal_test_conflict_notice_head
            WHERE center_ref=@center AND subject_ref=@subject AND
                  subject_kind=@kind AND evidence_revision=@evidence AND
                  review_note_request_ref=@note
            FOR UPDATE
            """, cn, tx))
        {
            AddScope(locked, input);
            current = (long)(await locked.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("TEST_CONFLICT_NOTICE_HEAD_MISSING"));
        }

        var history = await ReplayLockedAsync(cn, tx, input, current, ct);
        var existing = history.FirstOrDefault(x => x.Value.RequestId == input.RequestId);
        if (existing.Value.RequestId == input.RequestId)
        {
            await tx.RollbackAsync(ct);
            return existing.Value == input
                ? ConflictNoticeOutcome.Replayed : ConflictNoticeOutcome.ConflictingReplay;
        }
        if (input.ExpectedRevision != current)
        {
            await tx.RollbackAsync(ct);
            return ConflictNoticeOutcome.StaleRevision;
        }
        if (current == long.MaxValue)
            throw new InvalidOperationException("TEST_CONFLICT_NOTICE_REVISION_EXHAUSTED");

        var next = current + 1;
        var json = JsonSerializer.Serialize(input);
        var digest = Digest(input, next, json);
        await using (var accepted = new NpgsqlCommand("""
            INSERT INTO internal_test_conflict_notice_event
              (center_ref,subject_ref,subject_kind,evidence_revision,
               review_note_request_ref,notice_revision,request_ref,command_json,notice_digest)
            VALUES (@center,@subject,@kind,@evidence,@note,@rev,@request,@json,@digest)
            """, cn, tx))
        {
            AddScope(accepted, input);
            accepted.Parameters.AddWithValue("rev", next);
            accepted.Parameters.AddWithValue("request", input.RequestId);
            accepted.Parameters.AddWithValue("json", json);
            accepted.Parameters.AddWithValue("digest", digest);
            await accepted.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_conflict_notice_audit
              (center_ref,subject_ref,subject_kind,evidence_revision,
               review_note_request_ref,notice_revision,request_ref,notice_digest)
            VALUES (@center,@subject,@kind,@evidence,@note,@rev,@request,@digest)
            """, cn, tx))
        {
            AddScope(audit, input);
            audit.Parameters.AddWithValue("rev", next);
            audit.Parameters.AddWithValue("request", input.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using (var cas = new NpgsqlCommand("""
            UPDATE internal_test_conflict_notice_head SET current_revision=@next
            WHERE center_ref=@center AND subject_ref=@subject AND
                  subject_kind=@kind AND evidence_revision=@evidence AND
                  review_note_request_ref=@note AND current_revision=@previous
            """, cn, tx))
        {
            AddScope(cas, input);
            cas.Parameters.AddWithValue("next", next);
            cas.Parameters.AddWithValue("previous", current);
            if (await cas.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("TEST_CONFLICT_NOTICE_CAS_FAILED");
        }
        await tx.CommitAsync(ct);
        return ConflictNoticeOutcome.PotentialRelationshipRecorded;
    }

    internal async Task<ConflictNoticeReadModel> ReadAsync(
        string center, string subject, EvidenceSubjectKind kind,
        long evidenceRevision, Guid noteRequestId, CancellationToken ct = default)
    {
        if (!IntakeDomain.IsSyntheticRef(center) ||
            !IntakeDomain.IsSyntheticRef(subject) || !Enum.IsDefined(kind) ||
            evidenceRevision < 1 || noteRequestId == Guid.Empty)
            throw new ArgumentException("Invalid synthetic relationship scope.");

        var scope = new ConflictNoticeCommand(Guid.Empty, 0, center, subject,
            kind, evidenceRevision, noteRequestId, "", "");
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        object? revision;
        await using (var locked = new NpgsqlCommand("""
            SELECT current_revision FROM internal_test_conflict_notice_head
            WHERE center_ref=@center AND subject_ref=@subject AND
                  subject_kind=@kind AND evidence_revision=@evidence AND
                  review_note_request_ref=@note
            FOR UPDATE
            """, cn, tx))
        {
            AddScope(locked, scope);
            revision = await locked.ExecuteScalarAsync(ct);
        }
        if (revision is null)
        {
            // No declaration is NOT independence, only absent disclosure data.
            await tx.CommitAsync(ct);
            return new(ConflictScreeningStatus.UnknownWithoutDisclosureEvidence,
                Array.Empty<ConflictNoticeRecord>(), 0);
        }
        var current = (long)revision;
        var replayed = await ReplayLockedAsync(cn, tx, scope, current, ct);
        await tx.CommitAsync(ct);
        return new(ConflictScreeningStatus.PotentialRelationshipRequiresIndependentReview,
            Array.AsReadOnly(replayed.Select(x =>
                new ConflictNoticeRecord(x.Revision, x.Value.RequestId,
                    x.Value.ReporterClaimRef, x.Value.RelationshipEvidenceRef)).ToArray()),
            current);
    }

    private async Task<bool> HasImmutableNoteAsync(
        ConflictNoticeCommand command, CancellationToken ct)
    {
        try
        {
            var notes = await noteJournal.ReadAsync(command.CenterRef,
                command.SubjectRef, command.SubjectKind,
                command.EvidenceRevision, ct);
            return notes.Any(x => x.RequestId == command.ReviewNoteRequestId);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<List<(long Revision, ConflictNoticeCommand Value)>>
        ReplayLockedAsync(NpgsqlConnection cn, NpgsqlTransaction tx,
            ConflictNoticeCommand scope, long expected, CancellationToken ct)
    {
        var history = new List<(long Revision, ConflictNoticeCommand Value)>();
        await using var cmd = new NpgsqlCommand("""
            SELECT e.notice_revision,e.request_ref,e.command_json,e.notice_digest,
                   a.request_ref,a.notice_digest
            FROM internal_test_conflict_notice_event e
            LEFT JOIN internal_test_conflict_notice_audit a ON
                a.center_ref=e.center_ref AND a.subject_ref=e.subject_ref
                AND a.subject_kind=e.subject_kind AND a.evidence_revision=e.evidence_revision
                AND a.review_note_request_ref=e.review_note_request_ref
                AND a.notice_revision=e.notice_revision
            WHERE e.center_ref=@center AND e.subject_ref=@subject AND
                  e.subject_kind=@kind AND e.evidence_revision=@evidence AND
                  e.review_note_request_ref=@note
            ORDER BY e.notice_revision
            """, cn, tx);
        AddScope(cmd, scope);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(4) || reader.IsDBNull(5))
                throw new InvalidOperationException("TEST_CONFLICT_NOTICE_HISTORY_CORRUPT");
            ConflictNoticeCommand record;
            try { record = JsonSerializer.Deserialize<ConflictNoticeCommand>(
                reader.GetString(2)); }
            catch (JsonException)
            { throw new InvalidOperationException("TEST_CONFLICT_NOTICE_HISTORY_CORRUPT"); }
            var version = reader.GetInt64(0);
            if (!Valid(record) || version != history.Count + 1 ||
                record.ExpectedRevision != version - 1 ||
                record.CenterRef != scope.CenterRef ||
                record.SubjectRef != scope.SubjectRef ||
                record.SubjectKind != scope.SubjectKind ||
                record.EvidenceRevision != scope.EvidenceRevision ||
                record.ReviewNoteRequestId != scope.ReviewNoteRequestId ||
                record.RequestId != reader.GetGuid(1) ||
                record.RequestId != reader.GetGuid(4) ||
                reader.GetString(3) != reader.GetString(5) ||
                reader.GetString(3) != Digest(record, version, reader.GetString(2)))
                throw new InvalidOperationException("TEST_CONFLICT_NOTICE_HISTORY_CORRUPT");
            history.Add((version, record));
        }
        if (history.Count != expected)
            throw new InvalidOperationException("TEST_CONFLICT_NOTICE_HISTORY_CORRUPT");
        return history;
    }

    private static bool Valid(ConflictNoticeCommand x) =>
        x.RequestId != Guid.Empty && x.ExpectedRevision >= 0 &&
        IntakeDomain.IsSyntheticRef(x.CenterRef) &&
        IntakeDomain.IsSyntheticRef(x.SubjectRef) &&
        IntakeDomain.IsSyntheticRef(x.ReporterClaimRef) &&
        IntakeDomain.IsSyntheticRef(x.RelationshipEvidenceRef) &&
        Enum.IsDefined(x.SubjectKind) && x.EvidenceRevision >= 1 &&
        x.ReviewNoteRequestId != Guid.Empty;

    private static void AddScope(NpgsqlCommand cmd, ConflictNoticeCommand scope)
    {
        cmd.Parameters.AddWithValue("center", scope.CenterRef);
        cmd.Parameters.AddWithValue("subject", scope.SubjectRef);
        cmd.Parameters.AddWithValue("kind", (int)scope.SubjectKind);
        cmd.Parameters.AddWithValue("evidence", scope.EvidenceRevision);
        cmd.Parameters.AddWithValue("note", scope.ReviewNoteRequestId);
    }

    private static string Digest(ConflictNoticeCommand input, long revision, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-review-conflict-disclosure.v1", input.CenterRef,
            input.SubjectRef, ((int)input.SubjectKind).ToString(CultureInfo.InvariantCulture),
            input.EvidenceRevision.ToString(CultureInfo.InvariantCulture),
            input.ReviewNoteRequestId.ToString("N"),
            revision.ToString(CultureInfo.InvariantCulture), json))));
}
