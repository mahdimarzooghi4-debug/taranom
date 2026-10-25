using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Read-only adapter derives all references and validates every journal chain in one PostgreSQL snapshot.
internal sealed class PgMedicalSourceBlobReconciler(
    NpgsqlDataSource readerDataSource,
    MedicalSourcePackageBlobStore blobStore)
{
    private static readonly HashSet<string> Lanes=["RIGHTS","SCIENCE","TAXONOMY"];
    private static readonly HashSet<string> Recommendations=["REQUEST_EVIDENCE","RECOMMEND_ACCEPT","RECOMMEND_REJECT"];

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
                    if(!ValidSha(catalog)||!ValidSha(taxonomy)||!ValidSha(head))
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
                    if(!states.TryGetValue(catalog,out var state)||!ValidSourceRef(sourceRef)||!ValidSha(packageSha))
                        throw new InvalidOperationException("MEDICAL_REVIEW_SOURCE_SCOPE_TAMPERED");
                    state.Sources.Add(sourceRef,packageSha);referenced.Add(packageSha);
                }
            }
            await using(var query=new NpgsqlCommand("""
                SELECT e.catalog_sha256,e.revision,e.request_id,e.command_json,e.previous_sha256,e.event_sha256,
                       a.request_id,a.event_sha256
                FROM internal_test_medical_review_event e
                LEFT JOIN internal_test_medical_review_audit a
                  ON a.catalog_sha256=e.catalog_sha256 AND a.revision=e.revision
                ORDER BY e.catalog_sha256,e.revision
                """,cn,tx))
            await using(var rows=await query.ExecuteReaderAsync(ct))
            {
                while(await rows.ReadAsync(ct))
                {
                    var catalog=rows.GetString(0);var revision=rows.GetInt64(1);var requestId=rows.GetGuid(2);
                    var json=rows.GetString(3);var previous=rows.GetString(4);var eventSha=rows.GetString(5);
                    if(!states.TryGetValue(catalog,out var state))
                        throw new InvalidOperationException("MEDICAL_REVIEW_EVENT_SCOPE_TAMPERED");
                    var expectedRevision=state.EventCount+1;
                    MedicalSourceReviewCommand? command;
                    try { command=JsonSerializer.Deserialize<MedicalSourceReviewCommand>(json); }
                    catch(JsonException) { throw new InvalidOperationException("MEDICAL_REVIEW_COMMAND_JSON_TAMPERED"); }
                    if(command is null||!ValidCommand(command)||revision!=expectedRevision||previous!=state.LastEventSha256||
                       EventDigest(previous,revision,json)!=eventSha||rows.IsDBNull(6)||rows.IsDBNull(7)||
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
        internal string LastEventSha256 { get; set; }=Digest($"genesis\n{catalogSha256}\n{taxonomySha256}");
        internal Dictionary<string,string> Sources { get; }=new(StringComparer.Ordinal);
    }

    private static bool ValidCommand(MedicalSourceReviewCommand x)=>x.RequestId!=Guid.Empty&&x.ExpectedRevision>=0&&
        ValidSourceRef(x.SourceRef)&&ValidSha(x.PackageSha256)&&Lanes.Contains(x.Lane)&&Recommendations.Contains(x.Recommendation)&&
        ValidClaim(x.ReviewerRefClaimed,"reviewer:")&&ValidClaim(x.EvidenceRefClaimed,"evidence:")&&ValidSha(x.EvidencePackageSha256);
    private static bool ValidSourceRef(string x)=>x is not null&&Regex.IsMatch(x,"^test:[A-Za-z0-9_:-]{1,120}$",RegexOptions.CultureInvariant);
    private static bool ValidClaim(string x,string prefix)=>x is not null&&x.StartsWith(prefix,StringComparison.Ordinal)&&
        x.Length>prefix.Length&&x.Length<=129&&x.AsSpan(prefix.Length).ToString().All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or ':' or '-');
    private static bool ValidSha(string x)=>x is not null&&x.Length==64&&x.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Digest(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string EventDigest(string previous,long revision,string json)=>Digest($"event\n{previous}\n{revision}\n{json}");
}
