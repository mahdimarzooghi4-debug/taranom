using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taranom.MemberGovernance;

// TRN-TECH-146: pure cryptographic evidence verification for a FUTURE
// self-hosted .NET AI-admin control plane. This is not OIDC login or RBAC.
// There are no configured legal actors, trusted issuers or approved grants.
public sealed record AiPinnedIdentityTrust(
    string Issuer, string Audience, string KeyId, string RsaPublicKeyPem);

public enum AiControlPlaneOperation
{
    ReadModelInventory = 1,
    RegisterModelVersion = 2,
    RegisterTrainingRequest = 3,
    ReadEvaluation = 4,
    RequestCommitteeReview = 5,
    RecordCommitteeApproval = 6,
    AuthorizeProductionActivation = 7,
    RollbackProduction = 8
}

public sealed record AiIdentityEvidence(
    string Status,
    bool CryptographicIdentityEvidenceVerified,
    bool RealOperatorAuthenticated,
    bool AdminAuthorized,
    bool CommitteeApproved,
    bool ProductionActivationAuthorized,
    bool ProductionAllowed,
    string? TokenSha256);

public sealed record AiAdminAccessDecision(
    string Status,
    AiControlPlaneOperation RequestedOperation,
    bool CryptographicIdentityEvidenceVerified,
    bool Allowed,
    bool AdminAuthenticated,
    bool HumanReviewCompleted,
    bool ProductionAllowed,
    string? TokenSha256);

// This verifier is deliberately not a production permission provider:
// - It can recognize a correctly signed/pinned JWT as identity EVIDENCE.
// - Neither a role claim nor a good signature grants administrative rights.
// - No approved OIDC issuer/audience/key, actual tenant administrator,
//   committee membership or final promoting officer is installed by default.
public static class AiAdminIdentityBoundary
{
    public const string Contract = "TRN-TECH-146";
    private const int MaxTokenChars = 8192;
    private const int MaxFieldChars = 512;
    private static readonly JsonDocumentOptions JsonRules =
        new() { MaxDepth = 8, AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow };

    public static AiIdentityEvidence VerifyEvidence(
        string? compactJwt, AiPinnedIdentityTrust? trusted,
        DateTimeOffset nowUtc)
    {
        var denied = Denied();
        if (trusted is null || !ValidString(trusted.Issuer) ||
            !ValidString(trusted.Audience) || !ValidString(trusted.KeyId) ||
            string.IsNullOrWhiteSpace(trusted.RsaPublicKeyPem) ||
            trusted.RsaPublicKeyPem.Length > 8192 || nowUtc == default ||
            string.IsNullOrEmpty(compactJwt) || compactJwt.Length > MaxTokenChars)
            return denied;

        var pieces = compactJwt.Split('.');
        if (pieces.Length != 3 ||
            !TryBase64Url(pieces[0], out var protectedBytes) ||
            !TryBase64Url(pieces[1], out var claimsBytes) ||
            !TryBase64Url(pieces[2], out var signature) ||
            signature.Length == 0)
            return denied;
        try
        {
            using var jose = JsonDocument.Parse(protectedBytes, JsonRules);
            if (!DistinctObject(jose.RootElement) ||
                jose.RootElement.EnumerateObject().Count() != 3 ||
                !EqualString(jose.RootElement, "alg", "RS256") ||
                !EqualString(jose.RootElement, "typ", "JWT") ||
                !EqualString(jose.RootElement, "kid", trusted.KeyId))
                return denied;

            using var claims = JsonDocument.Parse(claimsBytes, JsonRules);
            var root = claims.RootElement;
            if (!DistinctObject(root) ||
                !EqualString(root, "iss", trusted.Issuer) ||
                !EqualString(root, "aud", trusted.Audience) ||
                !TryNonblankString(root, "sub", out _) ||
                !TryNonblankString(root, "jti", out _) ||
                !TryNumber(root, "iat", out var issued) ||
                !TryNumber(root, "nbf", out var notBefore) ||
                !TryNumber(root, "exp", out var expires))
                return denied;

            var now = nowUtc.ToUnixTimeSeconds();
            // Time bounds are structural: no invented policy for token TTL
            // or release timing. A valid expiry must be in the future.
            if (issued > now || notBefore > now || expires <= now ||
                issued > expires || notBefore > expires)
                return denied;

            using var rsa = RSA.Create();
            rsa.ImportFromPem(trusted.RsaPublicKeyPem);
            var signed = Encoding.ASCII.GetBytes(pieces[0] + "." + pieces[1]);
            if (!rsa.VerifyData(signed, signature,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return denied;

            return new AiIdentityEvidence(
                "PINNED_SIGNATURE_EVIDENCE_ONLY",
                CryptographicIdentityEvidenceVerified: true,
                RealOperatorAuthenticated: false,
                AdminAuthorized: false,
                CommitteeApproved: false,
                ProductionActivationAuthorized: false,
                ProductionAllowed: false,
                TokenSha256: Convert.ToHexString(SHA256.HashData(
                    Encoding.ASCII.GetBytes(compactJwt))));
        }
        catch (Exception e) when (
            e is JsonException or CryptographicException or
            ArgumentException or FormatException or InvalidOperationException)
        {
            return denied;
        }
    }

    public static AiAdminAccessDecision Decide(
        string? compactJwt, AiPinnedIdentityTrust? trusted,
        DateTimeOffset nowUtc, AiControlPlaneOperation operation)
    {
        // Even a signed, issuer/audience-pinned identity cannot authorize
        // a business command without the separate, approved role/committee
        // contract and human promotion authority. They are not defined yet.
        var evidence = VerifyEvidence(compactJwt, trusted, nowUtc);
        return new AiAdminAccessDecision(
            evidence.CryptographicIdentityEvidenceVerified
                ? "VERIFIED_IDENTITY_EVIDENCE_BUT_ACCESS_DENIED"
                : "IDENTITY_NOT_VERIFIED_ACCESS_DENIED",
            operation,
            evidence.CryptographicIdentityEvidenceVerified,
            Allowed: false,
            AdminAuthenticated: false,
            HumanReviewCompleted: false,
            ProductionAllowed: false,
            evidence.TokenSha256);
    }

    private static AiIdentityEvidence Denied() => new(
        "IDENTITY_UNVERIFIED", false, false, false, false, false, false, null);

    private static bool ValidString(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxFieldChars;

    private static bool DistinctObject(JsonElement x)
    {
        if (x.ValueKind != JsonValueKind.Object) return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        return x.EnumerateObject().All(p => keys.Add(p.Name));
    }

    private static bool EqualString(JsonElement x, string key, string expected) =>
        x.TryGetProperty(key, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    private static bool TryNonblankString(JsonElement x, string key, out string? value)
    {
        value = null;
        if (!x.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            return false;
        value = v.GetString();
        return ValidString(value);
    }

    private static bool TryNumber(JsonElement x, string key, out long value)
    {
        value = 0;
        return x.TryGetProperty(key, out var v) &&
            v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out value);
    }

    private static bool TryBase64Url(string input, out byte[] bytes)
    {
        bytes = [];
        if (input.Length == 0 || input.Length > MaxTokenChars ||
            input.Length % 4 == 1 || input.Any(c =>
                !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            return false;
        try
        {
            var padded = input.Replace('-', '+').Replace('_', '/') +
                new string('=', (4 - input.Length % 4) % 4);
            bytes = Convert.FromBase64String(padded);
            // Canonical encoding: do not allow alternative representations.
            return Convert.ToBase64String(bytes).TrimEnd('=')
                .Replace('+','-').Replace('/','_') == input;
        }
        catch (FormatException) { return false; }
    }
}
