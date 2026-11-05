using System.Data;
using Npgsql;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Read-only adapter that derives the reconciliation reference set from the synthetic review journal.
internal sealed class PgMedicalSourceBlobReconciler(
    NpgsqlDataSource readerDataSource,
    PgMedicalSourceReviewJournal journal,
    MedicalSourcePackageBlobStore blobStore)
{
    internal async Task<IReadOnlyList<MedicalSourceBlobFinding>> ReconcileAsync(
        long maximumBytesPerBlob,CancellationToken ct=default)
    {
        if(maximumBytesPerBlob<=0)throw new ArgumentOutOfRangeException(nameof(maximumBytesPerBlob));
        var scopes=new List<(string Catalog,string Taxonomy)>();
        var referenced=new HashSet<string>(StringComparer.Ordinal);
        await using(var cn=await readerDataSource.OpenConnectionAsync(ct))
        await using(var tx=await cn.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct))
        {
            await using(var query=new NpgsqlCommand("""
                SELECT catalog_sha256,taxonomy_sha256
                FROM internal_test_medical_review_head ORDER BY catalog_sha256
                """,cn,tx))
            await using(var rows=await query.ExecuteReaderAsync(ct))
            {
                while(await rows.ReadAsync(ct))scopes.Add((rows.GetString(0),rows.GetString(1)));
            }
            await using(var query=new NpgsqlCommand("""
                SELECT package_sha256 FROM internal_test_medical_review_source ORDER BY catalog_sha256,source_ref
                """,cn,tx))
            await using(var rows=await query.ExecuteReaderAsync(ct))
            {
                while(await rows.ReadAsync(ct))referenced.Add(rows.GetString(0));
            }
            await tx.CommitAsync(ct);
        }

        // ReadAsync verifies each catalog's event, audit, hash-chain, and source linkage.
        foreach(var (catalog,taxonomy) in scopes)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot=await journal.ReadAsync(catalog,taxonomy,ct);
            foreach(var reviewEvent in snapshot.Events)
                referenced.Add(reviewEvent.Command.EvidencePackageSha256);
        }
        return await blobStore.ReconcileAsync(referenced,maximumBytesPerBlob,ct);
    }
}
