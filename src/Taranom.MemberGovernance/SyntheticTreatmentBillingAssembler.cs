using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Taranom.MemberGovernance;

// TRN-BIZ-048/049/050: a member-visible treatment bill gathers separate
// legal-source claims; it is NOT the pre-treatment quotation from Sprint28.
// INTERNAL TEST-ONLY: no authenticated issuer, official document or payment.
internal enum SyntheticBillLane
{
    ProviderService = 1,
    PrimaryInsurance = 2,
    SupplementalInsurance = 3,
    TreatmentCredit = 4,
    MemberRemainder = 5
}

internal enum SyntheticBillClaimedIssuer
{
    ProviderDocumentIssuer = 1,
    PrimaryInsurer = 2,
    SupplementalInsurer = 3,
    LicensedCreditCustodian = 4
}

internal enum SyntheticBillEvidenceChange
{
    UnverifiedClaim = 1,
    Disputed = 2,
    Withdrawn = 3
}

internal enum SyntheticBillViewState
{
    Invalid = 1,
    MissingOrWithdrawn = 2,
    Disputed = 3,
    IncompatibleCurrencies = 4,
    UnverifiedClaimsOnly = 5
}

internal readonly record struct SyntheticBillEvidence(
    string? MemberRef,
    string? TreatmentRef,
    Guid PacketId,
    SyntheticBillLane Lane,
    SyntheticBillClaimedIssuer ClaimedIssuer,
    SyntheticBillEvidenceChange Change,
    Guid EvidenceId,
    Guid SupersedesEvidenceId,
    long LaneRevision,
    string? UnverifiedSourceRef,
    long SourceRevision,
    decimal? UnverifiedAmount,
    string? CurrencyCode);

internal readonly record struct SyntheticBillLaneView(
    SyntheticBillLane Lane,
    SyntheticBillEvidenceChange? LastChange,
    Guid LatestEvidenceId,
    long LaneRevision,
    decimal? UnverifiedAmount,
    string? CurrencyCode);

internal readonly record struct SyntheticBillView(
    SyntheticBillViewState State,
    string? MemberRef,
    string? TreatmentRef,
    Guid PacketId,
    long ViewRevision,
    ImmutableArray<SyntheticBillLaneView> Lines,
    string? EvidenceFingerprint);

// Pure read-side assembler. A 'claim' from a test: source never becomes a
// verified issuer decision. Dispute or withdrawal masks the old numeric claim.
// Every corrected version must explicitly link to the immediately previous
// source event in the SAME lane. No arithmetic, coverage, allocation or payee.
internal static class SyntheticTreatmentBillingAssembler
{
    internal static SyntheticBillView Build(
        string? memberRef, string? treatmentRef, Guid packetId, long viewRevision,
        IReadOnlyList<SyntheticBillEvidence>? evidence)
    {
        var invalid = new SyntheticBillView(SyntheticBillViewState.Invalid,
            null, null, Guid.Empty, 0, ImmutableArray<SyntheticBillLaneView>.Empty, null);
        if (!SyntheticCostPreviewBuilder.Synthetic(memberRef) ||
            !SyntheticCostPreviewBuilder.Synthetic(treatmentRef) ||
            packetId == Guid.Empty || viewRevision <= 0 || evidence is null)
            return invalid;

        var knownIds = new HashSet<Guid>();
        var sorted = evidence.OrderBy(e => (int)e.Lane).ThenBy(e => e.LaneRevision).ToArray();
        foreach (var e in sorted)
        {
            if (e.MemberRef != memberRef || e.TreatmentRef != treatmentRef ||
                e.PacketId != packetId || !Enum.IsDefined(e.Lane) ||
                !Enum.IsDefined(e.ClaimedIssuer) ||
                !Enum.IsDefined(e.Change) ||
                e.ClaimedIssuer != ExpectedIssuer(e.Lane) ||
                e.EvidenceId == Guid.Empty || !knownIds.Add(e.EvidenceId) ||
                e.LaneRevision < 1 ||
                !SyntheticCostPreviewBuilder.Synthetic(e.UnverifiedSourceRef) ||
                e.SourceRevision < 1)
                return invalid;

            if (e.Change == SyntheticBillEvidenceChange.UnverifiedClaim)
            {
                if (e.UnverifiedAmount is null || e.UnverifiedAmount < 0 ||
                    !Currency(e.CurrencyCode))
                    return invalid;
            }
            else if (e.UnverifiedAmount is not null || e.CurrencyCode is not null)
                return invalid;
        }

        var views = ImmutableArray.CreateBuilder<SyntheticBillLaneView>(5);
        var currencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lane in Enum.GetValues<SyntheticBillLane>())
        {
            var versions = sorted.Where(x => x.Lane == lane).ToArray();
            if (versions.Length == 0)
            {
                views.Add(new(lane, null, Guid.Empty, 0, null, null));
                continue;
            }
            Guid priorId = Guid.Empty;
            long expectedRevision = 1;
            foreach (var e in versions)
            {
                if (e.LaneRevision != expectedRevision ||
                    e.SupersedesEvidenceId != priorId)
                    return invalid;
                priorId = e.EvidenceId;
                expectedRevision++;
            }
            var latest = versions[^1];
            if (latest.Change == SyntheticBillEvidenceChange.UnverifiedClaim)
                currencies.Add(latest.CurrencyCode!);
            views.Add(new(lane, latest.Change, latest.EvidenceId, latest.LaneRevision,
                latest.Change == SyntheticBillEvidenceChange.UnverifiedClaim
                    ? latest.UnverifiedAmount : null,
                latest.Change == SyntheticBillEvidenceChange.UnverifiedClaim
                    ? latest.CurrencyCode : null));
        }

        var resultLines = views.ToImmutable();
        var state = resultLines.Any(x => x.LastChange == SyntheticBillEvidenceChange.Disputed)
            ? SyntheticBillViewState.Disputed
            : currencies.Count > 1 ? SyntheticBillViewState.IncompatibleCurrencies
            : resultLines.Any(x => x.LastChange is null or SyntheticBillEvidenceChange.Withdrawn)
                ? SyntheticBillViewState.MissingOrWithdrawn
                : SyntheticBillViewState.UnverifiedClaimsOnly;

        // Fingerprint is only an order-invariant test provenance checksum,
        // never a signature or verified source-document attestation.
        var buffer = new StringBuilder("taranom.synthetic-billing-lineage.v1");
        void Put(string value) => buffer.Append('|')
            .Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        Put(memberRef!);
        Put(treatmentRef!);
        Put(packetId.ToString("N"));
        Put(viewRevision.ToString(CultureInfo.InvariantCulture));
        foreach (var e in sorted)
        {
            Put(((int)e.Lane).ToString(CultureInfo.InvariantCulture));
            Put(((int)e.ClaimedIssuer).ToString(CultureInfo.InvariantCulture));
            Put(((int)e.Change).ToString(CultureInfo.InvariantCulture));
            Put(e.EvidenceId.ToString("N"));
            Put(e.SupersedesEvidenceId.ToString("N"));
            Put(e.LaneRevision.ToString(CultureInfo.InvariantCulture));
            Put(e.UnverifiedSourceRef!);
            Put(e.SourceRevision.ToString(CultureInfo.InvariantCulture));
            Put(e.UnverifiedAmount?.ToString("G29", CultureInfo.InvariantCulture) ?? "~");
            Put(e.CurrencyCode ?? "~");
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString())));
        return new(state, memberRef, treatmentRef, packetId,
            viewRevision, resultLines, hash);
    }

    private static SyntheticBillClaimedIssuer ExpectedIssuer(SyntheticBillLane lane) => lane switch
    {
        SyntheticBillLane.ProviderService or SyntheticBillLane.MemberRemainder
            => SyntheticBillClaimedIssuer.ProviderDocumentIssuer,
        SyntheticBillLane.PrimaryInsurance => SyntheticBillClaimedIssuer.PrimaryInsurer,
        SyntheticBillLane.SupplementalInsurance => SyntheticBillClaimedIssuer.SupplementalInsurer,
        SyntheticBillLane.TreatmentCredit => SyntheticBillClaimedIssuer.LicensedCreditCustodian,
        _ => (SyntheticBillClaimedIssuer)0
    };

    private static bool Currency(string? s) =>
        s is { Length: 3 } && s.All(c => c is >= 'A' and <= 'Z');
}
