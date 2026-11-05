using System.Data;
using System.Text.Json;
using Npgsql;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Read-only adapter derives all references and validates every journal chain in one PostgreSQL snapshot.
internal sealed class PgMedicalSourceBlobReconciler(
    NpgsqlDataSource readerDataSource,
    MedicalSourcePackageBlobStore blobStore)
{
    internal async Task<IReadOnlyList<MedicalSourceBlobFinding>> ReconcileAsync(
        long maximumBytesPerBlob,CancellationToken ct=default)
    {
        if(maximumBytesPerBlob<=0)throw new ArgumentOutOfRangeException(nameof(maximumBytesPerBlob));
        var states=new Dictionary<string,CatalogState>(StringComparer.Ordinal);
        var referenced=new HashSet<string>(StringComparer.Ordinal);
        await using(var cn=await readerDataSource.OpenConnectionAsync(ct))
        await using(var tx=await cn.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct))
        {
            await using(var readOnly=new NpgsqlCommand("SET TRANSACTION READ ONLY",cn,tx))
                await readOnly.ExecuteNonQueryAsync(ct);
            await using(var query=new NpgsqlCommand("""
                SELECT catalog_sha256,taxonomy_sha256,current_revision,head_sha256
                FROM internal_test_medical_review_head ORDER BY catalog_sha256
                """,cn,tx))
            await using(var rows=await query.ExecuteReaderAsync(ct))
            {
                while(await rows.ReadAsync(ct))
                {
                    var catalog=rows.GetString(0);var taxonomy=rows.GetString(1);var head=rows.GetString(3);
                    if(!MedicalSourceReviewIntegrity.IsSha256(catalog)||!MedicalSourceReviewIntegrity.IsSha256(taxonomy)||!MedicalSourceReviewIntegrity.IsSha256(head))
                        throw new InvalidOperationException("MEDICAL_REVIEW_CATALOG_METADATA_TAMPERED");
                    states.Add(catalog,new(catalog,taxonomy,rows.GetInt64(2),head));
                }
            }
            await using(var query=new NpgsqlCommand("""
                SELECT catalog_sha256,source_ref,package_sha256
                FROM internal_test_medical_review_source ORDER BY catalog_sha256,source_ref
                """,cn,tx))
            await using(var rows=await query.ExecuteReaderAsync(ct))
            {
                while(await rows.ReadAsync(ct))
                {
                    var catalog=rows.GetString(0);var sourceRef=rows.GetString(1);var packageSha=rows.GetString(2);
                    if(!states.TryGetValue(catalog,out var state)||!MedicalSourceReviewIntegrity.IsSourceRef(sourceRef)||!MedicalSourceReviewIntegrity.IsSha256(packageSha))
                        throw new InvalidOperationException("MEDICAL_REVIEW_SOURCE_SCOPE_TAMPERED");
                    state.Sources.Add(sourceRef,packageSha);referenced.Add(packageSha);
                }
            }
            await using(var query=new NpgsqlCommand("""
                SELECT e.catalog_sha256,e.revision,e.request_id,e.command_json,e.previous_sha256,e.event_sha256,
                       a.request_id,a.event_sha256
                FROM internal_test_medical_review_event e
                FULL OUTER JOIN internal_test_medical_review_audit a
                  ON a.catalog_sha256=e.catalog_sha256 AND a.revision=e.revision
                ORDER BY COALESCE(e.catalog_sha256,a.catalog_sha256),COALESCE(e.revision,a.revision)
                """,cn,tx))
            await using(var rows=await query.ExecuteReaderAsync(ct))
            {
                while(await rows.ReadAsync(ct))
                {
                    if(rows.IsDBNull(0))
                        throw new InvalidOperationException("MEDICAL_REVIEW_ORPHAN_AUDIT_TAMPERED");
                    var catalog=rows.GetString(0);var revision=rows.GetInt64(1);var requestId=rows.GetGuid(2);
                    var json=rows.GetString(3);var previous=rows.GetString(4);var eventSha=rows.GetString(5);
                    if(!states.TryGetValue(catalog,out var state))
                        throw new InvalidOperationException("MEDICAL_REVIEW_EVENT_SCOPE_TAMPERED");
                    var expectedRevision=state.EventCount+1;
                    MedicalSourceReviewCommand? command;
                    try { command=JsonSerializer.Deserialize<MedicalSourceReviewCommand>(json); }
                    catch(JsonException) { throw new InvalidOperationException("MEDICAL_REVIEW_COMMAND_JSON_TAMPERED"); }
                    if(command is null||!MedicalSourceReviewIntegrity.IsValidCommand(command)||revision!=expectedRevision||previous!=state.LastEventSha256||
                       MedicalSourceReviewIntegrity.EventDigest(previous,revision,json)!=eventSha||rows.IsDBNull(6)||rows.IsDBNull(7)||
                       rows.GetGuid(6)!=requestId||command.RequestId!=requestId||rows.GetString(7)!=eventSha||
                       !state.Sources.TryGetValue(command.SourceRef,out var packageSha)||packageSha!=command.PackageSha256)
                        throw new InvalidOperationException("MEDICAL_REVIEW_CHAIN_OR_AUDIT_TAMPERED");
                    state.EventCount=revision;state.LastEventSha256=eventSha;
                    referenced.Add(command.EvidencePackageSha256);
                }
            }
            foreach(var state in states.Values)
                if(state.Sources.Count==0||state.EventCount!=state.Revision||state.LastEventSha256!=state.HeadSha256)
                    throw new InvalidOperationException("MEDICAL_REVIEW_HEAD_DIVERGENCE");
            await tx.CommitAsync(ct);
        }
        return await blobStore.ReconcileAsync(referenced,maximumBytesPerBlob,ct);
    }

    private sealed class CatalogState(string catalogSha256,string taxonomySha256,long revision,string headSha256)
    {
        internal long Revision { get; }=revision;
        internal string HeadSha256 { get; }=headSha256;
        internal long EventCount { get; set; }
        internal string LastEventSha256 { get; set; }=MedicalSourceReviewIntegrity.Digest($"genesis\n{catalogSha256}\n{taxonomySha256}");
        internal Dictionary<string,string> Sources { get; }=new(StringComparer.Ordinal);
    }

}
