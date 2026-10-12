using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Taranom.MemberGovernance;

// Internal test-only, NONBINDING presentation input. Claimed values are NEVER
// proof of tariff, insurance eligibility, spendable credit or member liability.
internal enum SyntheticCostComponent
{
    ProviderService = 1,
    PrimaryInsurance = 2,
    SupplementalInsurance = 3,
    TreatmentCredit = 4,
    MemberRemainder = 5
}

internal enum SyntheticCostEvidenceState
{
    Missing = 1,
    ProvisionalClaim = 2,
    ConflictingClaims = 3
}

internal enum SyntheticCostPreviewState
{
    Invalid = 1,
    MissingOrIncomplete = 2,
    Conflicted = 3,
    AllComponentsProvisionalOnly = 4
}

internal readonly record struct SyntheticCostLine(
    SyntheticCostComponent Component,
    SyntheticCostEvidenceState State,
    decimal? ClaimedAmount,
    string? CurrencyCode,
    string? UnverifiedSourceRef,
    Guid EvidenceRef,
    long SourceRevision);

internal readonly record struct SyntheticCostPreview(
    SyntheticCostPreviewState State,
    string? MemberRef,
    Guid PreviewId,
    long PreviewRevision,
    ImmutableArray<SyntheticCostLine> Lines,
    string? Fingerprint);

// No aggregation, arithmetic, fallback, zero-for-missing, or legal validation.
// The five shares are independently entered and must not be trusted as adding
// up to service cost or as an approved allocation. Fingerprints bind only these
// caller-provided synthetic fields, not authenticated source documents.
internal static class SyntheticCostPreviewBuilder
{
    internal static SyntheticCostPreview Build(
        string? memberRef, Guid previewId, long revision,
        IReadOnlyList<SyntheticCostLine>? supplied)
    {
        var invalid = new SyntheticCostPreview(SyntheticCostPreviewState.Invalid,
            null, Guid.Empty, 0, ImmutableArray<SyntheticCostLine>.Empty, null);
        if (!Synthetic(memberRef) || previewId == Guid.Empty || revision <= 0 ||
            supplied is null || supplied.Count != 5)
            return invalid;

        var seen = new HashSet<SyntheticCostComponent>();
        string? currency = null;
        var result = ImmutableArray.CreateBuilder<SyntheticCostLine>(5);
        foreach (var line in supplied)
        {
            if (!Enum.IsDefined(line.Component) || !seen.Add(line.Component) ||
                !Enum.IsDefined(line.State))
                return invalid;
            if (line.State == SyntheticCostEvidenceState.Missing)
            {
                if (line.ClaimedAmount is not null || line.CurrencyCode is not null ||
                    line.UnverifiedSourceRef is not null || line.EvidenceRef != Guid.Empty ||
                    line.SourceRevision != 0)
                    return invalid;
            }
            else
            {
                if (!Synthetic(line.UnverifiedSourceRef) || line.EvidenceRef == Guid.Empty ||
                    line.SourceRevision <= 0)
                    return invalid;

                if (line.State == SyntheticCostEvidenceState.ConflictingClaims)
                {
                    // A disputed figure cannot be presented as a numeric quote.
                    if (line.ClaimedAmount is not null || line.CurrencyCode is not null)
                        return invalid;
                }
                else
                {
                    if (line.ClaimedAmount is null || line.ClaimedAmount < 0 ||
                        !Currency(line.CurrencyCode))
                        return invalid;
                    if (currency is not null && currency != line.CurrencyCode)
                        return invalid;
                    currency = line.CurrencyCode;
                }
            }
            result.Add(line);
        }
        if (seen.Count != 5) return invalid;

        var lines = result.ToImmutable().OrderBy(line => (int)line.Component).ToImmutableArray();
        var state = lines.Any(line => line.State == SyntheticCostEvidenceState.ConflictingClaims)
            ? SyntheticCostPreviewState.Conflicted
            : lines.Any(line => line.State == SyntheticCostEvidenceState.Missing)
                ? SyntheticCostPreviewState.MissingOrIncomplete
                : SyntheticCostPreviewState.AllComponentsProvisionalOnly;
        // Length-delimited text makes ambiguous delimiter injection impossible.
        var fingerprintData = new StringBuilder("taranom.synthetic-cost-preview.v1");
        void Append(string value) => fingerprintData.Append('|')
            .Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        Append(memberRef!);
        Append(previewId.ToString("N"));
        Append(revision.ToString(CultureInfo.InvariantCulture));
        foreach (var line in lines)
        {
            Append(((int)line.Component).ToString(CultureInfo.InvariantCulture));
            Append(((int)line.State).ToString(CultureInfo.InvariantCulture));
            Append(line.ClaimedAmount?.ToString("G29", CultureInfo.InvariantCulture) ?? "~");
            Append(line.CurrencyCode ?? "~");
            Append(line.UnverifiedSourceRef ?? "~");
            Append(line.EvidenceRef.ToString("N"));
            Append(line.SourceRevision.ToString(CultureInfo.InvariantCulture));
        }
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintData.ToString())));
        return new(state, memberRef, previewId, revision, lines, fingerprint);
    }

    internal static bool Synthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');

    private static bool Currency(string? value) =>
        value is { Length: 3 } && value.All(ch => ch is >= 'A' and <= 'Z');
}
