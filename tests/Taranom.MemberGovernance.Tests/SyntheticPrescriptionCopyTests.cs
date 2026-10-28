using System.Collections.Immutable;
using System.Security.Cryptography;
using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticPrescriptionCopyTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private static byte[] Document(string value) =>
        System.Text.Encoding.UTF8.GetBytes(value);

    private static SyntheticPrescriptionCopyCommand Command(
        Guid? requestId = null,
        long revision = 0,
        string member = "test:member-1",
        string prescription = "test:prescription-1",
        string issuer = "test:issuer-1",
        string sourceRevision = "test:source-revision-1",
        byte[]? document = null,
        DateTimeOffset? issuedAt = null)
    {
        var bytes = document ?? Document("synthetic-only-prescription-copy");
        var immutable = ImmutableArray.CreateRange(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new(requestId ?? Guid.NewGuid(), revision, member, prescription,
            issuer, sourceRevision, issuedAt ?? IssuedAt, immutable, hash);
    }

    [Fact]
    public void Records_opaque_source_bytes_and_keeps_each_revision_immutable()
    {
        var first = Command();
        var firstChange = SyntheticPrescriptionCopyDomain.Apply(
            SyntheticPrescriptionCopySnapshot.Empty, first);
        Assert.Equal(SyntheticPrescriptionCopyOutcome.RecordedCopy, firstChange.Outcome);
        Assert.Equal(1, firstChange.Snapshot.Revision);
        Assert.Equal(first.DocumentBytes, firstChange.Snapshot.History[0].DocumentBytes);

        var second = Command(revision: 1, sourceRevision: "test:source-revision-2",
            document: Document("corrected synthetic prescription copy"));
        var secondChange = SyntheticPrescriptionCopyDomain.Apply(firstChange.Snapshot, second);

        Assert.Equal(SyntheticPrescriptionCopyOutcome.RecordedCopy, secondChange.Outcome);
        Assert.Equal(2, secondChange.Snapshot.History.Length);
        Assert.Equal(first.DocumentSha256, secondChange.Snapshot.History[0].DocumentSha256);
        Assert.Equal(first.DocumentBytes, secondChange.Snapshot.History[0].DocumentBytes);
        Assert.Equal(second.DocumentBytes, secondChange.Snapshot.History[1].DocumentBytes);
    }

    [Fact]
    public void Exact_retry_with_a_new_byte_array_is_idempotent()
    {
        var original = Command();
        var recorded = SyntheticPrescriptionCopyDomain.Apply(
            SyntheticPrescriptionCopySnapshot.Empty, original);
        var retry = original with
        {
            DocumentBytes = ImmutableArray.CreateRange(original.DocumentBytes)
        };

        var replayed = SyntheticPrescriptionCopyDomain.Apply(recorded.Snapshot, retry);

        Assert.Equal(SyntheticPrescriptionCopyOutcome.Replayed, replayed.Outcome);
        Assert.Equal(1, replayed.Snapshot.Revision);
    }

    [Fact]
    public void Reused_request_id_with_changed_bytes_is_conflicting()
    {
        var original = Command();
        var recorded = SyntheticPrescriptionCopyDomain.Apply(
            SyntheticPrescriptionCopySnapshot.Empty, original);
        var changed = Command(requestId: original.RequestId, document: Document("different copy"));

        var result = SyntheticPrescriptionCopyDomain.Apply(recorded.Snapshot, changed);

        Assert.Equal(SyntheticPrescriptionCopyOutcome.ConflictingReplay, result.Outcome);
        Assert.Equal(1, result.Snapshot.Revision);
    }

    [Fact]
    public void Rejects_live_references_bad_hash_empty_or_oversized_content_and_non_utc_time()
    {
        var valid = Command();
        var oversized = Enumerable.Repeat((byte)65, 4 * 1024 * 1024 + 1).ToArray();
        var invalid = new[]
        {
            valid with { MemberRef = "member-live-1" },
            valid with { IssuerRef = "test:bad issuer" },
            valid with { DocumentSha256 = new string('0', 64) },
            valid with { DocumentBytes = ImmutableArray<byte>.Empty },
            Command(document: oversized),
            valid with { IssuedAt = IssuedAt.ToOffset(TimeSpan.FromHours(3)) }
        };

        foreach (var command in invalid)
        {
            var result = SyntheticPrescriptionCopyDomain.Apply(
                SyntheticPrescriptionCopySnapshot.Empty, command);
            Assert.Equal(SyntheticPrescriptionCopyOutcome.Invalid, result.Outcome);
            Assert.Equal(0, result.Snapshot.Revision);
        }
    }

    [Fact]
    public void Rejects_stale_revision_and_cross_member_or_record_append()
    {
        var first = Command();
        var recorded = SyntheticPrescriptionCopyDomain.Apply(
            SyntheticPrescriptionCopySnapshot.Empty, first);
        Assert.Equal(SyntheticPrescriptionCopyOutcome.StaleRevision,
            SyntheticPrescriptionCopyDomain.Apply(recorded.Snapshot,
                Command(revision: 0)).Outcome);
        Assert.Equal(SyntheticPrescriptionCopyOutcome.ScopeConflict,
            SyntheticPrescriptionCopyDomain.Apply(recorded.Snapshot,
                Command(revision: 1, member: "test:member-2")).Outcome);
        Assert.Equal(SyntheticPrescriptionCopyOutcome.ScopeConflict,
            SyntheticPrescriptionCopyDomain.Apply(recorded.Snapshot,
                Command(revision: 1, prescription: "test:prescription-2")).Outcome);
    }
    [Fact]
    public void Rejects_history_growth_beyond_bounded_storage_budget()
    {
        var snapshot = SyntheticPrescriptionCopySnapshot.Empty;
        for (var revision = 0; revision < 16; revision++)
        {
            var command = Command(revision: revision,
                sourceRevision: $"test:source-revision-{revision + 1}",
                document: Document($"synthetic-copy-{revision}"));
            var change = SyntheticPrescriptionCopyDomain.Apply(snapshot, command);
            Assert.Equal(SyntheticPrescriptionCopyOutcome.RecordedCopy, change.Outcome);
            snapshot = change.Snapshot;
        }

        var overflow = Command(revision: 16,
            sourceRevision: "test:source-revision-17",
            document: Document("synthetic-copy-overflow"));
        var rejected = SyntheticPrescriptionCopyDomain.Apply(snapshot, overflow);
        Assert.Equal(SyntheticPrescriptionCopyOutcome.Invalid, rejected.Outcome);
        Assert.Equal(16, rejected.Snapshot.Revision);
        Assert.Equal(16, rejected.Snapshot.History.Length);
   
}

public sealed class SyntheticPanelAccessPolicyTests
{
    [Fact]
    public void Exact_scoped_grants_isolate_center_fund_and_support_finance_surfaces()
    {
        var principal = SyntheticPanelPrincipal.ForTest("test:actor-admin", new[]
        {
            new SyntheticPanelGrant("test:fund-taranom",
                SyntheticPanelCapability.TaranomFundRead),
            new SyntheticPanelGrant("test:center-1",
                SyntheticPanelCapability.CenterRevenueRead),
            new SyntheticPanelGrant("test:case-7",
                SyntheticPanelCapability.SupportedCaseFinanceRead)
        });

        Assert.True(SyntheticPanelAccessPolicy.Evaluate(principal, "test:fund-taranom",
            SyntheticPanelCapability.TaranomFundRead).Allowed);
        Assert.True(SyntheticPanelAccessPolicy.Evaluate(principal, "test:center-1",
            SyntheticPanelCapability.CenterRevenueRead).Allowed);
        Assert.True(SyntheticPanelAccessPolicy.Evaluate(principal, "test:case-7",
            SyntheticPanelCapability.SupportedCaseFinanceRead).Allowed);
        Assert.False(SyntheticPanelAccessPolicy.Evaluate(principal, "test:center-2",
            SyntheticPanelCapability.CenterRevenueRead).Allowed);
        Assert.False(SyntheticPanelAccessPolicy.Evaluate(principal, "test:case-7",
            SyntheticPanelCapability.MemberOwnPrescriptionRead).Allowed);
    }

    [Fact]
    public void Denies_untrusted_live_and_invalid_capability_requests()
    {
        var principal = SyntheticPanelPrincipal.ForTest("actor-live", new[]
        {
            new SyntheticPanelGrant("test:fund-taranom",
                SyntheticPanelCapability.TaranomFundRead)
        });
        var trusted = SyntheticPanelPrincipal.ForTest("test:actor",
            new[] { new SyntheticPanelGrant("test:fund-taranom",
                SyntheticPanelCapability.TaranomFundRead) });

        Assert.False(SyntheticPanelAccessPolicy.Evaluate(principal, "test:fund-taranom",
            SyntheticPanelCapability.TaranomFundRead).Allowed);
        Assert.False(SyntheticPanelAccessPolicy.Evaluate(trusted, "fund-live",
            SyntheticPanelCapability.TaranomFundRead).Allowed);
        Assert.False(SyntheticPanelAccessPolicy.Evaluate(trusted, "test:fund-taranom",
            (SyntheticPanelCapability)999).Allowed);
        Assert.False(SyntheticPanelAccessPolicy.Evaluate(null, "test:fund-taranom",
            SyntheticPanelCapability.TaranomFundRead).Allowed);
    }


}
