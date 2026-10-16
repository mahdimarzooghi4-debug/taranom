using System.Data;
using Npgsql;

namespace Taranom.OrgScope.Foundation;

// TRN-TECH-136: operational TEST-only coherent draft workqueue. This does NOT
// assign work, identify a staff member or decide any business state.
internal sealed record OrgDraftQueueRow(string ItemRef, string SourceRef,
    int ActivityCount, string? LastRequestRef, string? LastNoteRef);
internal sealed record OrgDraftQueuePage(IReadOnlyList<OrgDraftQueueRow> Items,
    string? NextAfterItemRef);

internal sealed class PgScopedDraftWorkQueue(NpgsqlDataSource database)
{
    internal async Task<OrgDraftQueuePage> ReadAsync(
        string? afterItemRef, int limit, CancellationToken ct = default)
    {
        PgScopedDraftWorkspace.RequireEphemeral();
        if (limit is < 1 or > 10 ||
            (afterItemRef is not null &&
             !PgScopedDraftWorkspace.IsTestRef(afterItemRef)))
            throw new ArgumentException("ORG_QUEUE_INVALID_CURSOR_OR_LIMIT");
        await using var conn = await database.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,ct);
        await using(var readonlyCmd = new NpgsqlCommand(
            "SET TRANSACTION READ ONLY", conn,tx))
            await readonlyCmd.ExecuteNonQueryAsync(ct);
        var scope = await PgScopedDraftWorkspace.ScopeAsync(conn,tx);
        var rows = new List<OrgDraftQueueRow>();
        var candidates = new List<(string Item, string Source)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT item_ref,source_ref
            FROM public.internal_test_org_draft_item
            WHERE scope_ref=@scope AND
                  (@after='' OR item_ref COLLATE "C" > @after COLLATE "C")
            ORDER BY item_ref COLLATE "C"
            LIMIT @amount
            """,conn,tx))
        {
            cmd.Parameters.AddWithValue("scope",scope);
            cmd.Parameters.AddWithValue("after",afterItemRef ?? "");
            cmd.Parameters.AddWithValue("amount",limit+1);
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
                candidates.Add((reader.GetString(0),reader.GetString(1)));
        }
        foreach(var candidate in candidates)
        {
            if (!PgScopedDraftWorkspace.IsTestRef(candidate.Item) ||
                !PgScopedDraftWorkspace.IsTestRef(candidate.Source))
                throw new InvalidOperationException("ORG_QUEUE_UNVERIFIABLE_REF");
            // The CREATE audit AND complete activity lineage (including the
            // lookahead row) must both validate before returning ANY rows.
            var origin=await OrgDraftAuditVerifier.VerifyAsync(
                conn,tx,scope,candidate.Item,ct);
            if(origin is null || origin.SourceRef!=candidate.Source)
                throw new InvalidOperationException("ORG_QUEUE_ORIGIN_UNVERIFIABLE");
            var history=await OrgDraftActivityVerifier.VerifyHistoryAsync(
                conn,tx,scope,candidate.Item,ct);
            var last=history.LastOrDefault();
            rows.Add(new OrgDraftQueueRow(candidate.Item,candidate.Source,
                history.Count,last?.RequestRef,last?.NoteRef));
        }
        await tx.CommitAsync(ct);
        bool more=rows.Count>limit;
        return new OrgDraftQueuePage(rows.Take(limit).ToArray(),
            more?rows[limit-1].ItemRef:null);
    }
}
