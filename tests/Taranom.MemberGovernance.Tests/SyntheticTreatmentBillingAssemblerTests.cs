using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticTreatmentBillingAssemblerTests
{
    private const string Member = "test:member-A";
    private const string Treatment = "test:treatment-1";
    private static readonly Guid Packet = Guid.Parse("00000000-0000-4000-8000-000000000041");

    private static SyntheticBillClaimedIssuer Issuer(SyntheticBillLane lane) => lane switch
    {
        SyntheticBillLane.ProviderService or SyntheticBillLane.MemberRemainder
            => SyntheticBillClaimedIssuer.ProviderDocumentIssuer,
        SyntheticBillLane.PrimaryInsurance => SyntheticBillClaimedIssuer.PrimaryInsurer,
        SyntheticBillLane.SupplementalInsurance => SyntheticBillClaimedIssuer.SupplementalInsurer,
        _ => SyntheticBillClaimedIssuer.LicensedCreditCustodian
    };

    private static SyntheticBillEvidence Claim(SyntheticBillLane lane,
        decimal? amount = 10m, string currency = "IRR") =>
        new(Member, Treatment, Packet, lane, Issuer(lane),
            SyntheticBillEvidenceChange.UnverifiedClaim, Guid.NewGuid(),
            Guid.Empty, 1, "test:source-document", 1, amount, currency);

    private static SyntheticBillEvidence Later(SyntheticBillEvidence before,
        SyntheticBillEvidenceChange change, decimal? amount = null,
        string? currency = null) =>
        before with {
            Change = change,
            EvidenceId = Guid.NewGuid(),
            SupersedesEvidenceId = before.EvidenceId,
            LaneRevision = before.LaneRevision + 1,
            SourceRevision = before.SourceRevision + 1,
            UnverifiedAmount = amount,
            CurrencyCode = currency
        };

    private static SyntheticBillEvidence[] AllClaims() =>
        Enum.GetValues<SyntheticBillLane>().Select(x => Claim(x)).ToArray();

    private static SyntheticBillView Build(IReadOnlyList<SyntheticBillEvidence>? lines,
        string? member = Member, string? treatment = Treatment,
        Guid? packet = null, long revision = 1) =>
        SyntheticTreatmentBillingAssembler.Build(member, treatment,
            packet ?? Packet, revision, lines);

    [Fact]
    public void EmptyEvidenceDisplaysFiveTrulyUnknownLegalSourceLanes()
    {
        var view = Build(Array.Empty<SyntheticBillEvidence>());
        Assert.Equal(SyntheticBillViewState.MissingOrWithdrawn, view.State);
        Assert.Equal(5, view.Lines.Length);
        Assert.All(view.Lines, line => {
            Assert.Null(line.LastChange);
            Assert.Null(line.UnverifiedAmount);
            Assert.Equal(Guid.Empty, line.LatestEvidenceId);
            Assert.Equal(0, line.LaneRevision);
        });
        Assert.Equal(64, view.EvidenceFingerprint!.Length);
    }

    [Fact]
    public void AllClaimedAmountsAreStillUnverifiedNeverFinal()
    {
        var view = Build(AllClaims());
        Assert.Equal(SyntheticBillViewState.UnverifiedClaimsOnly, view.State);
        Assert.All(view.Lines, x => Assert.Equal(SyntheticBillEvidenceChange.UnverifiedClaim, x.LastChange));
        Assert.DoesNotContain(Enum.GetNames<SyntheticBillViewState>(),
            text => text.Contains("Final") || text.Contains("Settled") ||
                    text.Contains("Approved") || text.Contains("Paid"));
    }

    [Fact]
    public void LiteralZeroIsNotTheSameAsAbsentClaim()
    {
        var zero = Claim(SyntheticBillLane.PrimaryInsurance, 0m);
        Assert.Equal(0m, Build(new[] { zero }).Lines[1].UnverifiedAmount);
        Assert.Null(Build(Array.Empty<SyntheticBillEvidence>()).Lines[1].UnverifiedAmount);
        Assert.NotEqual(Build(new[] { zero }).EvidenceFingerprint,
            Build(Array.Empty<SyntheticBillEvidence>()).EvidenceFingerprint);
    }

    [Fact]
    public void InsurerDisputeHidesPreviouslyClaimedAmountAndPreservesOtherLane()
    {
        var primary = Claim(SyntheticBillLane.PrimaryInsurance, 20m);
        var service = Claim(SyntheticBillLane.ProviderService, 80m);
        var dispute = Later(primary, SyntheticBillEvidenceChange.Disputed);
        var view = Build(new[] { service, dispute, primary });
        Assert.Equal(SyntheticBillViewState.Disputed, view.State);
        Assert.Null(view.Lines[1].UnverifiedAmount);
        Assert.Null(view.Lines[1].CurrencyCode);
        Assert.Equal(dispute.EvidenceId, view.Lines[1].LatestEvidenceId);
        Assert.Equal(2, view.Lines[1].LaneRevision);
        Assert.Equal(80m, view.Lines[0].UnverifiedAmount);
    }

    [Fact]
    public void WithdrawalNeverCarriesForwardPreviousValue()
    {
        var first = Claim(SyntheticBillLane.TreatmentCredit, 23m);
        var withdrawn = Later(first, SyntheticBillEvidenceChange.Withdrawn);
        var view = Build(new[] { withdrawn, first });
        Assert.Equal(SyntheticBillViewState.MissingOrWithdrawn, view.State);
        Assert.Null(view.Lines[3].UnverifiedAmount);
        Assert.Equal(SyntheticBillEvidenceChange.Withdrawn, view.Lines[3].LastChange);
    }

    [Fact]
    public void CorrectionAfterDisputeIsStillUnverifiedAndLinked()
    {
        var first = Claim(SyntheticBillLane.PrimaryInsurance, 10m);
        var disputed = Later(first, SyntheticBillEvidenceChange.Disputed);
        var corrected = Later(disputed, SyntheticBillEvidenceChange.UnverifiedClaim, 15m, "IRR");
        var result = Build(new[] { corrected, first, disputed });
        Assert.Equal(3, result.Lines[1].LaneRevision);
        Assert.Equal(15m, result.Lines[1].UnverifiedAmount);
        Assert.Equal(SyntheticBillViewState.MissingOrWithdrawn, result.State);
    }

    [Fact]
    public void DistinctCurrentCurrenciesCannotBePromotedToUnifiedMoney()
    {
        var line = Claim(SyntheticBillLane.ProviderService, 10m, "IRR");
        var insurance = Claim(SyntheticBillLane.PrimaryInsurance, 10m, "USD");
        Assert.Equal(SyntheticBillViewState.IncompatibleCurrencies,
            Build(new[] { line, insurance }).State);
    }

    [Fact]
    public void DisputeOverridesCurrencyMismatchButDoesNotEraseLineage()
    {
        var first = Claim(SyntheticBillLane.ProviderService, 10m, "IRR");
        var other = Claim(SyntheticBillLane.PrimaryInsurance, 10m, "USD");
        var dispute = Later(other, SyntheticBillEvidenceChange.Disputed);
        var view = Build(new[] { first, other, dispute });
        Assert.Equal(SyntheticBillViewState.Disputed, view.State);
        Assert.NotNull(view.EvidenceFingerprint);
    }

    [Fact]
    public void PermutedEvidenceGivesSameFingerprintButChangedVersionDoesNot()
    {
        var a = AllClaims();
        Assert.Equal(Build(a).EvidenceFingerprint, Build(a.Reverse().ToArray()).EvidenceFingerprint);
        Assert.NotEqual(Build(a).EvidenceFingerprint, Build(a, revision: 2).EvidenceFingerprint);
        var mutated = a.ToArray();
        mutated[0] = mutated[0] with { UnverifiedAmount = 11m };
        Assert.NotEqual(Build(a).EvidenceFingerprint, Build(mutated).EvidenceFingerprint);
    }

    [Theory]
    [InlineData("wrong-member")]
    [InlineData("wrong-treatment")]
    [InlineData("wrong-packet")]
    [InlineData("wrong-issuer")]
    [InlineData("invalid-lane")]
    [InlineData("invalid-change")]
    [InlineData("invalid-issuer")]
    [InlineData("blank-source")]
    [InlineData("real-source")]
    [InlineData("source-revision-zero")]
    [InlineData("empty-event")]
    [InlineData("amount-negative")]
    [InlineData("amount-absent")]
    [InlineData("bad-currency")]
    [InlineData("no-currency")]
    [InlineData("wrong-supersedes")]
    [InlineData("wrong-start-revision")]
    public void InvalidMetadataFailsClosedWithNoLeakedClaims(string mutation)
    {
        var line = Claim(SyntheticBillLane.PrimaryInsurance);
        line = mutation switch
        {
            "wrong-member" => line with { MemberRef = "test:other-member" },
            "wrong-treatment" => line with { TreatmentRef = "test:other-treatment" },
            "wrong-packet" => line with { PacketId = Guid.NewGuid() },
            "wrong-issuer" => line with { ClaimedIssuer = SyntheticBillClaimedIssuer.ProviderDocumentIssuer },
            "invalid-lane" => line with { Lane = (SyntheticBillLane)99 },
            "invalid-change" => line with { Change = (SyntheticBillEvidenceChange)99 },
            "invalid-issuer" => line with { ClaimedIssuer = (SyntheticBillClaimedIssuer)99 },
            "blank-source" => line with { UnverifiedSourceRef = "" },
            "real-source" => line with { UnverifiedSourceRef = "insurer:live" },
            "source-revision-zero" => line with { SourceRevision = 0 },
            "empty-event" => line with { EvidenceId = Guid.Empty },
            "amount-negative" => line with { UnverifiedAmount = -1m },
            "amount-absent" => line with { UnverifiedAmount = null },
            "bad-currency" => line with { CurrencyCode = "irr" },
            "no-currency" => line with { CurrencyCode = null },
            "wrong-supersedes" => line with { SupersedesEvidenceId = Guid.NewGuid() },
            "wrong-start-revision" => line with { LaneRevision = 2 },
            _ => throw new ArgumentException()
        };
        var result = Build(new[] { line });
        Assert.Equal(SyntheticBillViewState.Invalid, result.State);
        Assert.Empty(result.Lines);
        Assert.Null(result.MemberRef);
        Assert.Null(result.EvidenceFingerprint);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void DisputeAndWithdrawalCannotCarryNumericClaims(int changeCode)
    {
        var first = Claim(SyntheticBillLane.SupplementalInsurance);
        var invalid = Later(first, (SyntheticBillEvidenceChange)changeCode, 5m, "IRR");
        Assert.Equal(SyntheticBillViewState.Invalid, Build(new[] { first, invalid }).State);
    }

    [Theory]
    [InlineData("skipped-revision")]
    [InlineData("wrong-parent")]
    [InlineData("duplicate-revision")]
    [InlineData("duplicate-evidence-id")]
    [InlineData("reuse-across-lanes")]
    public void HistoricalSourceChainMustBeExactAndNonreplayableAcrossLanes(string mutation)
    {
        var first = Claim(SyntheticBillLane.ProviderService);
        var second = Later(first, SyntheticBillEvidenceChange.UnverifiedClaim, 12m, "IRR");
        var lines = new List<SyntheticBillEvidence> { first, second };
        switch (mutation)
        {
            case "skipped-revision": lines[1] = second with { LaneRevision = 3 }; break;
            case "wrong-parent": lines[1] = second with { SupersedesEvidenceId = Guid.NewGuid() }; break;
            case "duplicate-revision": lines[1] = second with { LaneRevision = 1 }; break;
            case "duplicate-evidence-id": lines[1] = second with { EvidenceId = first.EvidenceId }; break;
            case "reuse-across-lanes":
                lines.Add(Claim(SyntheticBillLane.MemberRemainder) with { EvidenceId = first.EvidenceId });
                break;
        }
        Assert.Equal(SyntheticBillViewState.Invalid, Build(lines).State);
    }

    [Theory]
    [InlineData("invalid-member")]
    [InlineData("invalid-treatment")]
    [InlineData("empty-packet")]
    [InlineData("zero-view-revision")]
    [InlineData("null-evidence")]
    public void PacketAndReadScopeNeverAcceptOperationalOrMissingIdentity(string change)
    {
        var result = change switch
        {
            "invalid-member" => Build(AllClaims(), member: "real:patient"),
            "invalid-treatment" => Build(AllClaims(), treatment: "real:clinical-visit"),
            "empty-packet" => Build(AllClaims(), packet: Guid.Empty),
            "zero-view-revision" => Build(AllClaims(), revision: 0),
            "null-evidence" => Build(null),
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticBillViewState.Invalid, result.State);
    }

    [Fact]
    public void ViewCannotBecomeEntitlementInvoiceSettlementOrPaymentCommand()
    {
        var properties = typeof(SyntheticBillView).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(properties, name => new[] {
            "Payable", "Approved", "Final", "Settled", "InsuranceEntitlement",
            "WalletBalance", "Paid", "Reserved", "MemberDebt"
        }.Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }
}
