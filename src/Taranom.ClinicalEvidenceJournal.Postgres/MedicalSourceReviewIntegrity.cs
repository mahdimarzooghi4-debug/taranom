using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// One integrity contract is shared by journal reads and the report-only blob inventory.
internal static class MedicalSourceReviewIntegrity
{
    private static readonly HashSet<string> Lanes=["RIGHTS","SCIENCE","TAXONOMY"];
    private static readonly HashSet<string> Recommendations=["REQUEST_EVIDENCE","RECOMMEND_ACCEPT","RECOMMEND_REJECT"];

    internal static bool IsValidSubmission(MedicalSourceReviewSubmission? x)=>x is not null&&x.RequestId!=Guid.Empty&&
        x.ExpectedRevision>=0&&IsSourceRef(x.SourceRef)&&IsSha256(x.PackageSha256)&&Lanes.Contains(x.Lane)&&
        Recommendations.Contains(x.Recommendation)&&IsClaimRef(x.ReviewerRefClaimed,"reviewer:")&&
        IsClaimRef(x.EvidenceRefClaimed,"evidence:");

    internal static bool IsValidCommand(MedicalSourceReviewCommand? x)=>x is not null&&
        IsValidSubmission(new MedicalSourceReviewSubmission(x.RequestId,x.ExpectedRevision,x.SourceRef,x.PackageSha256,
            x.Lane,x.Recommendation,x.ReviewerRefClaimed,x.EvidenceRefClaimed))&&IsSha256(x.EvidencePackageSha256);

    internal static bool IsSourceRef(string? x)=>x is not null&&
        Regex.IsMatch(x,"^test:[A-Za-z0-9_:-]{1,120}$",RegexOptions.CultureInvariant);

    internal static bool IsClaimRef(string? x,string prefix)=>x is not null&&
        x.StartsWith(prefix,StringComparison.Ordinal)&&x.Length>prefix.Length&&x.Length<=129&&
        x.AsSpan(prefix.Length).ToString().All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or ':' or '-');

    internal static bool IsSha256(string? x)=>x is not null&&x.Length==64&&
        x.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static string Digest(string value)=>Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string EventDigest(string previous,long revision,string json)=>
        Digest($"event\n{previous}\n{revision}\n{json}");
}
