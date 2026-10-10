using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using IntakeDomain = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Strictly test-only: a "reviewer" here is a caller-asserted synthetic marker,
// NOT an authenticated professional, human approval, or medical decision.
internal enum HumanReviewNoteOutcome
{
    PendingSourceVerificationRecorded = 1,
    Replayed = 2,
    ConflictingReplay = 3,
    Invalid = 4,
    ScopeDenied = 5,
    StaleRevision = 6
}

internal readonly record struct HumanReviewNoteCommand(
    Guid RequestId, long ExpectedNoteRevision,
    string CenterRef, string SubjectRef, EvidenceSubjectKind SubjectKind,
    long EvidenceRevision, Guid FrozenSnapshotId, int FrozenItemIndex,
    EvidenceSubjectKind? SnapshotSubjectFilter,
    string ReviewerClaimRef, string RationaleEvidenceRef);

internal readonly record struct HumanReviewNoteRecord(
    long NoteRevision, Guid RequestId, long EvidenceRevision,
    Guid FrozenSnapshotId, string ReviewerClaimRef, string RationaleEvidenceRef);

internal readonly record struct HumanReviewNoteResult(
    HumanReviewNoteOutcome Outcome, long CurrentRevision);

// No final PASS/FAIL/APPROVE/REJECT status can be created by this data store.
internal sealed class PgHumanReviewNoteJournal(
    NpgsqlDataSource dataSource, PgFrozenUnverifiedReviewQueue frozenQueue)
{
    internal static async Task InitializeTestSchemaAsync(
        NpgsqlDataSource owner, CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_human_note_head (
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                evidence_revision bigint NOT NULL,
                current_note_revision bigint NOT NULL DEFAULT 0 CHECK(current_note_revision>=0),
                PRIMARY KEY(center_ref,subject_ref,subject_kind,evidence_revision),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,evidence_revision)
                REFERENCES internal_test_review_event
                  (center_ref,subject_ref,subject_kind,event_revision)
            );
            CREATE TABLE IF NOT EXISTS internal_test_human_note (
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                evidence_revision bigint NOT NULL,
                note_revision bigint NOT NULL CHECK(note_revision>0),
                request_ref uuid NOT NULL,
                command_json text NOT NULL,
                note_digest text NOT NULL,
                PRIMARY KEY(center_ref,subject_ref,subject_kind,evidence_revision,note_revision),
                UNIQUE(center_ref,subject_ref,subject_kind,evidence_revision,request_ref),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,evidence_revision)
                REFERENCES internal_test_human_note_head
                  (center_ref,subject_ref,subject_kind,evidence_revision)
            );
            CREATE TABLE IF NOT EXISTS internal_test_human_note_audit (
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                evidence_revision bigint NOT NULL,
                note_revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                note_digest text NOT NULL,
                recorded_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(center_ref,subject_ref,subject_kind,evidence_revision,note_revision),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,evidence_revision,note_revision)
                REFERENCES internal_test_human_note
                  (center_ref,subject_ref,subject_kind,evidence_revision,note_revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_human_note_guard()
            RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP = 'INSERT' AND TG_TABLE_NAME='internal_test_human_note_audit' AND
                    NEW.center_ref LIKE 'test:abort-review-audit-%' THEN
                    RAISE EXCEPTION 'TEST_HUMAN_REVIEW_AUDIT_UNAVAILABLE' USING ERRCODE='P0001';
                END IF;
                IF TG_OP='INSERT' THEN RETURN NEW; END IF;
                RAISE EXCEPTION 'TEST_HUMAN_REVIEW_APPEND_ONLY' USING ERRCODE='P0001';
            END; $fn$;
            DROP TRIGGER IF EXISTS human_note_guard_tr ON internal_test_human_note;
            CREATE TRIGGER human_note_guard_tr BEFORE UPDATE OR DELETE
            ON internal_test_human_note FOR EACH ROW
            EXECUTE FUNCTION internal_test_human_note_guard();
            DROP TRIGGER IF EXISTS human_note_audit_guard_tr ON internal_test_human_note_audit;
            CREATE TRIGGER human_note_audit_guard_tr BEFORE INSERT OR UPDATE OR DELETE
            ON internal_test_human_note_audit FOR EACH ROW
            EXECUTE FUNCTION internal_test_human_note_guard();
            """;
        await using var cn = await owner.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(ddl, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<HumanReviewNoteResult> RecordAsync(
        HumanReviewNoteCommand input, CancellationToken ct = default)
    {
        if (!Valid(input))
            return new(HumanReviewNoteOutcome.Invalid, 0);

        // An immutable frozen snapshot pins precisely which accepted claim
        // was being examined, not the reviewer's authority to examine it.
        UnverifiedReviewQueueItem? pinned = null;
        try
        {
            FrozenReviewCursor? cursor = input.FrozenItemIndex == 0
                ? null
                : new(input.FrozenSnapshotId, input.CenterRef,
                    input.SnapshotSubjectFilter, input.FrozenItemIndex);
            var page = await frozenQueue.PageAsync(input.CenterRef,
                input.SnapshotSubjectFilter, input.FrozenSnapshotId, 1, cursor, ct);
            if (page.Items.Count == 1) pinned = page.Items[0];
        }
        catch (ArgumentException)
        {
            return new(HumanReviewNoteOutcome.ScopeDenied, 0);
        }
        catch (InvalidOperationException)
        {
            // Never fall back to an unsnapshotted live record.
            return new(HumanReviewNoteOutcome.ScopeDenied, 0);
        }
        if (pinned is not { } item ||
            item.CenterRef != input.CenterRef ||
            item.SubjectRef != input.SubjectRef ||
            item.SubjectKind != input.SubjectKind ||
            item.Revision != input.EvidenceRevision)
            return new(HumanReviewNoteOutcome.ScopeDenied, 0);

        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        await using (var create = new NpgsqlCommand("""
            INSERT INTO internal_test_human_note_head
              (center_ref,subject_ref,subject_kind,evidence_revision)
            VALUES (@center,@subject,@kind,@evidence)
            ON CONFLICT DO NOTHING
            """, cn, tx))
        {
            AddScope(create, input);
            await create.ExecuteNonQueryAsync(ct);
        }

        long revision;
        await using (var lockRow = new NpgsqlCommand("""
            SELECT current_note_revision FROM internal_test_human_note_head
            WHERE center_ref=@center AND subject_ref=@subject
              AND subject_kind=@kind AND evidence_revision=@evidence
            FOR UPDATE
            """, cn, tx))
        {
            AddScope(lockRow, input);
            var value = await lockRow.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("TEST_HUMAN_REVIEW_HEAD_MISSING");
            revision = (long)value;
        }
        var history = await ReadLockedAsync(cn, tx, input, revision, ct);
        var replay = history.FirstOrDefault(x => x.Command.RequestId == input.RequestId);
        if (replay.Command.RequestId == input.RequestId)
        {
            await tx.RollbackAsync(ct);
            return new(replay.Command == input
                ? HumanReviewNoteOutcome.Replayed
                : HumanReviewNoteOutcome.ConflictingReplay, revision);
        }
        if (input.ExpectedNoteRevision != revision)
        {
            await tx.RollbackAsync(ct);
            return new(HumanReviewNoteOutcome.StaleRevision, revision);
        }
        if (revision == long.MaxValue)
            throw new InvalidOperationException("TEST_HUMAN_REVIEW_REVISION_EXHAUSTED");

        var next = revision + 1;
        var json = JsonSerializer.Serialize(input);
        var digest = Digest(input, next, json);
        await using (var accepted = new NpgsqlCommand("""
            INSERT INTO internal_test_human_note
                (center_ref,subject_ref,subject_kind,evidence_revision,
                 note_revision,request_ref,command_json,note_digest)
            VALUES (@center,@subject,@kind,@evidence,@rev,@request,@json,@digest)
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
            INSERT INTO internal_test_human_note_audit
                (center_ref,subject_ref,subject_kind,evidence_revision,
                 note_revision,request_ref,note_digest)
            VALUES (@center,@subject,@kind,@evidence,@rev,@request,@digest)
            """, cn, tx))
        {
            AddScope(audit, input);
            audit.Parameters.AddWithValue("rev", next);
            audit.Parameters.AddWithValue("request", input.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using (var cas = new NpgsqlCommand("""
            UPDATE internal_test_human_note_head SET current_note_revision=@next
            WHERE center_ref=@center AND subject_ref=@subject AND subject_kind=@kind
              AND evidence_revision=@evidence AND current_note_revision=@previous
            """, cn, tx))
        {
            AddScope(cas, input);
            cas.Parameters.AddWithValue("next", next);
            cas.Parameters.AddWithValue("previous", revision);
            if (await cas.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("TEST_HUMAN_REVIEW_CAS_CONFLICT");
        }
        await tx.CommitAsync(ct);
        return new(HumanReviewNoteOutcome.PendingSourceVerificationRecorded, next);
    }

    internal async Task<IReadOnlyList<HumanReviewNoteRecord>> ReadAsync(
        string center, string subject, EvidenceSubjectKind kind, long evidenceRevision,
        CancellationToken ct = default)
    {
        if (!IntakeDomain.IsSyntheticRef(center) ||
            !IntakeDomain.IsSyntheticRef(subject) || !Enum.IsDefined(kind) ||
            evidenceRevision < 1)
            throw new ArgumentException("Invalid synthetic reviewed claim scope.");

        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        long? revision;
        await using (var head = new NpgsqlCommand("""
            SELECT current_note_revision FROM internal_test_human_note_head
            WHERE center_ref=@center AND subject_ref=@subject AND
                subject_kind=@kind AND evidence_revision=@evidence FOR UPDATE
            """, cn, tx))
        {
            AddScope(head, center, subject, kind, evidenceRevision);
            var result = await head.ExecuteScalarAsync(ct);
            revision = result is null ? null : (long)result;
        }
        if (revision is null)
        {
            await tx.CommitAsync(ct);
            return Array.Empty<HumanReviewNoteRecord>();
        }
        var command = new HumanReviewNoteCommand(Guid.Empty, 0, center, subject, kind,
            evidenceRevision, Guid.Empty, 0, null, "", "");
        var list = await ReadLockedAsync(cn, tx, command, revision.Value, ct);
        await tx.CommitAsync(ct);
        return Array.AsReadOnly(list.Select(x =>
            new HumanReviewNoteRecord(x.Version, x.Command.RequestId, evidenceRevision,
                x.Command.FrozenSnapshotId, x.Command.ReviewerClaimRef,
                x.Command.RationaleEvidenceRef)).ToArray());
    }

    private static async Task<List<(long Version,HumanReviewNoteCommand Command)>> ReadLockedAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx,
        HumanReviewNoteCommand scope, long expected, CancellationToken ct)
    {
        var found = new List<(long Version, HumanReviewNoteCommand Command)>();
        await using var command = new NpgsqlCommand("""
            SELECT n.note_revision,n.request_ref,n.command_json,n.note_digest,
                   a.request_ref,a.note_digest
            FROM internal_test_human_note n LEFT JOIN internal_test_human_note_audit a
              ON a.center_ref=n.center_ref AND a.subject_ref=n.subject_ref
              AND a.subject_kind=n.subject_kind
              AND a.evidence_revision=n.evidence_revision AND a.note_revision=n.note_revision
            WHERE n.center_ref=@center AND n.subject_ref=@subject
              AND n.subject_kind=@kind AND n.evidence_revision=@evidence
            ORDER BY n.note_revision
            """, cn, tx);
        AddScope(command, scope);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(4) || reader.IsDBNull(5))
                throw new InvalidOperationException("TEST_HUMAN_REVIEW_HISTORY_CORRUPT");
            HumanReviewNoteCommand item;
            try { item = JsonSerializer.Deserialize<HumanReviewNoteCommand>(reader.GetString(2)); }
            catch (JsonException)
            { throw new InvalidOperationException("TEST_HUMAN_REVIEW_HISTORY_CORRUPT"); }
            var version = reader.GetInt64(0);
            if (!Valid(item) ||
                version != found.Count + 1 ||
                item.ExpectedNoteRevision != version - 1 ||
                item.CenterRef != scope.CenterRef ||
                item.SubjectRef != scope.SubjectRef ||
                item.SubjectKind != scope.SubjectKind ||
                item.EvidenceRevision != scope.EvidenceRevision ||
                item.RequestId != reader.GetGuid(1) ||
                item.RequestId != reader.GetGuid(4) ||
                reader.GetString(3) != reader.GetString(5) ||
                reader.GetString(3) != Digest(item, version, reader.GetString(2)))
                throw new InvalidOperationException("TEST_HUMAN_REVIEW_HISTORY_CORRUPT");
            found.Add((version, item));
        }
        if (found.Count != expected)
            throw new InvalidOperationException("TEST_HUMAN_REVIEW_HISTORY_CORRUPT");
        return found;
    }

    private static bool Valid(HumanReviewNoteCommand x) =>
        x.RequestId != Guid.Empty && x.ExpectedNoteRevision >= 0 &&
        IntakeDomain.IsSyntheticRef(x.CenterRef) &&
        IntakeDomain.IsSyntheticRef(x.SubjectRef) &&
        IntakeDomain.IsSyntheticRef(x.ReviewerClaimRef) &&
        IntakeDomain.IsSyntheticRef(x.RationaleEvidenceRef) &&
        Enum.IsDefined(x.SubjectKind) &&
        (!x.SnapshotSubjectFilter.HasValue ||
            (Enum.IsDefined(x.SnapshotSubjectFilter.Value) &&
             x.SnapshotSubjectFilter.Value == x.SubjectKind)) &&
        x.EvidenceRevision >= 1 && x.FrozenSnapshotId != Guid.Empty &&
        x.FrozenItemIndex >= 0;

    private static void AddScope(NpgsqlCommand cmd, HumanReviewNoteCommand scope) =>
        AddScope(cmd, scope.CenterRef, scope.SubjectRef,
            scope.SubjectKind, scope.EvidenceRevision);

    private static void AddScope(NpgsqlCommand cmd, string center, string subject,
        EvidenceSubjectKind kind, long evidenceRevision)
    {
        cmd.Parameters.AddWithValue("center", center);
        cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.AddWithValue("kind", (int)kind);
        cmd.Parameters.AddWithValue("evidence", evidenceRevision);
    }

    private static string Digest(HumanReviewNoteCommand input, long version, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-human-note.v1", input.CenterRef, input.SubjectRef,
            ((int)input.SubjectKind).ToString(CultureInfo.InvariantCulture),
            input.EvidenceRevision.ToString(CultureInfo.InvariantCulture),
            version.ToString(CultureInfo.InvariantCulture), json))));
}
