using System.Data;
using Npgsql;
using NpgsqlTypes;
using IntakeDomain = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Bounded *historical* integrity observations; not proof of authority,
// legal receipt, professional qualification, or complete external audit.
internal readonly record struct SyntheticReceiptAuditSweepPage(
    int Inspected, int Inconsistent, bool HasMore, Guid? NextPacketCursor);

// Test-only historical diagnostic. Supply a SELECT-only PostgreSQL data source.
// Page snapshots are isolated; no cross-page or external issuer atomicity.
internal sealed class PgSyntheticReceiptAuditSweep(NpgsqlDataSource database)
{
    // Purely technical memory/SQL resource cap, NOT a business threshold.
    internal const int MaxPageSize = 100;

    internal async Task<SyntheticReceiptAuditSweepPage> ScanPageAsync(
        string centerRef, Guid? afterPacketId, int pageSize,
        CancellationToken ct = default)
    {
        if (!IntakeDomain.IsSyntheticRef(centerRef))
            throw new ArgumentException("Synthetic center required.", nameof(centerRef));
        if (afterPacketId == Guid.Empty)
            throw new ArgumentException("Empty cursor.", nameof(afterPacketId));
        if (pageSize is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        await using var cn = await database.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY", cn, tx))
            await mode.ExecuteNonQueryAsync(ct);

        var ids = new List<Guid>();
        await using (var query = new NpgsqlCommand("""
            SELECT packet_id FROM internal_test_packet_receipt
            WHERE center_ref=@center AND (@after IS NULL OR packet_id > @after)
            ORDER BY packet_id LIMIT @take
            """, cn, tx))
        {
            query.Parameters.AddWithValue("center", centerRef);
            query.Parameters.Add("after", NpgsqlDbType.Uuid).Value =
                afterPacketId.HasValue ? afterPacketId.Value : DBNull.Value;
            query.Parameters.AddWithValue("take", pageSize + 1);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        }

        var count = Math.Min(ids.Count, pageSize);
        var inconsistent = 0;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var stored = await PgSyntheticPacketReceiptJournal.ReadLockedAsync(
                    cn, tx, ids[i], ct);
                if (stored is null || stored.Command.CenterRef != centerRef)
                    inconsistent++;
            }
            catch (InvalidOperationException ex)
                when (ex.Message == "TEST_PACKET_RECEIPT_CORRUPT")
            {
                inconsistent++;
            }
            // Never suppress unexpected permissions, connection or SQL failures.
        }
        await tx.CommitAsync(ct);
        var more = ids.Count > pageSize;
        return new SyntheticReceiptAuditSweepPage(
            count, inconsistent, more, more ? ids[count - 1] : null);
    }
}
