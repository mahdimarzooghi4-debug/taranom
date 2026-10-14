using System.Data;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Taranom.OrgScope.Foundation;

internal enum DraftActivityStatus
{
    Recorded, Replayed, ConflictingReplay, StaleRevision, NotFound, InvalidRequest
}

internal sealed record DraftActivityResult(DraftActivityStatus Status, int CurrentNo);
internal sealed record DraftActivity(int ActivityNo, string RequestRef,
    string NoteRef, int ExpectedPreviousNo, string PreviousSha256,
    string ActivitySha256);

// Only opaque test: references, never raw free text, members or claims.
// Scope is always resolved from the PostgreSQL connection principal.
internal sealed class PgScopedDraftActivity(NpgsqlDataSource database)
{
    internal async Task<DraftActivityResult> AppendAsync(string? itemRef,
        string? requestRef, string? noteRef, int expectedPreviousNo,
        CancellationToken ct = default)
    {
        PgScopedDraftWorkspace.RequireEphemeral();
        if (!PgScopedDraftWorkspace.IsTestRef(itemRef) ||
            !PgScopedDraftWorkspace.IsTestRef(requestRef) ||
            !PgScopedDraftWorkspace.IsTestRef(noteRef) ||
            expectedPreviousNo < 0 || expectedPreviousNo == int.MaxValue)
            return new(DraftActivityStatus.InvalidRequest, 0);

        await using var conn = await database.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var scope = await PgScopedDraftWorkspace.ScopeAsync(conn, tx);
        if (await OrgDraftAuditVerifier.VerifyAsync(conn,tx,scope,itemRef!,ct)
            is null)
            return new(DraftActivityStatus.NotFound, 0);
        await using var cmd = new NpgsqlCommand("""
            SELECT result_code,current_no
            FROM public.internal_test_org_activity_append(
                 @item,@request,@note,@expected)
            """, conn, tx);
        cmd.Parameters.AddWithValue("item",itemRef!);
        cmd.Parameters.AddWithValue("request",requestRef!);
        cmd.Parameters.AddWithValue("note",noteRef!);
        cmd.Parameters.AddWithValue("expected",expectedPreviousNo);
        await using var row = await cmd.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct) ||
            !Enum.TryParse<DraftActivityStatus>(row.GetString(0),out var status))
            throw new InvalidOperationException("ORG_ACTIVITY_INVALID_DB_RESULT");
        var currentNo = row.GetInt32(1);
        if (await row.ReadAsync(ct))
            throw new InvalidOperationException("ORG_ACTIVITY_MULTIPLE_DB_RESULTS");
        await row.DisposeAsync();
        await tx.CommitAsync(ct);
        return new(status,currentNo);
    }

    internal async Task<IReadOnlyList<DraftActivity>> ReadHistoryAsync(
        string? itemRef, CancellationToken ct = default)
    {
        PgScopedDraftWorkspace.RequireEphemeral();
        if (!PgScopedDraftWorkspace.IsTestRef(itemRef))
            throw new ArgumentException("ORG_ACTIVITY_INVALID_ITEM_REF",
                nameof(itemRef));
        await using var conn = await database.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,ct);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY",
            conn,tx))
            await mode.ExecuteNonQueryAsync(ct);
        var scope = await PgScopedDraftWorkspace.ScopeAsync(conn,tx);
        var audit = await OrgDraftAuditVerifier.VerifyAsync(conn,tx,
            scope,itemRef!,ct);
        if (audit is null)
        {
            await tx.CommitAsync(ct);
            return [];
        }
        var items = new List<DraftActivity>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT activity_no,request_ref,note_ref,expected_previous_no,
                   previous_sha256,activity_sha256
            FROM public.internal_test_org_draft_activity
            WHERE scope_ref=@scope AND item_ref=@item
            ORDER BY activity_no
            """,conn,tx))
        {
            cmd.Parameters.AddWithValue("scope",scope);
            cmd.Parameters.AddWithValue("item",itemRef!);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                items.Add(new DraftActivity(reader.GetInt32(0),
                    reader.GetString(1),reader.GetString(2),
                    reader.GetInt32(3),reader.GetString(4),
                    reader.GetString(5)));
        }
        string previous = "GENESIS";
        int ordinal = 0;
        foreach (var n in items)
        {
            ordinal++;
            var preimage = "TRN-TECH-134|" + scope + "|" + itemRef + "|" +
                ordinal + "|" + n.RequestRef + "|" + n.NoteRef + "|" +
                previous;
            var digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(preimage)));
            if (n.ActivityNo != ordinal || n.ExpectedPreviousNo != ordinal-1 ||
                !PgScopedDraftWorkspace.IsTestRef(n.RequestRef) ||
                !PgScopedDraftWorkspace.IsTestRef(n.NoteRef) ||
                n.PreviousSha256 != previous ||
                !string.Equals(digest,n.ActivitySha256,StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "ORG_ACTIVITY_HISTORY_INTEGRITY_MISMATCH");
            previous = digest;
        }
        await tx.CommitAsync(ct);
        return items;
    }
}
