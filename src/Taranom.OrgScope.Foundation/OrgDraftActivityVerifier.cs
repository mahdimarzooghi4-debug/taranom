using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Taranom.OrgScope.Foundation;

// TRN-TECH-136: one verifier shared by history reads and queue projections.
// Both must use the SAME PostgreSQL read-only MVCC transaction.
internal static class OrgDraftActivityVerifier
{
    internal static async Task<IReadOnlyList<DraftActivity>> VerifyHistoryAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string scope,
        string itemRef, CancellationToken ct = default)
    {
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
        return items;
    }
}
