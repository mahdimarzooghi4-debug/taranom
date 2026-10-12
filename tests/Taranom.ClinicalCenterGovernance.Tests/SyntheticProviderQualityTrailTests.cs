using Taranom.ClinicalCenterGovernance;
using Xunit;

namespace Taranom.ClinicalCenterGovernance.Tests;

public sealed class SyntheticProviderQualityTrailTests
{
    private static SyntheticProviderQualityNote Root(bool network = false,
        Guid? id = null, long revision = 0) =>
        new(id ?? Guid.NewGuid(), revision,
            SyntheticProviderQualityNoteAction.RecordUnverifiedQualityScope,
            "test:quality-case-a", "test:tenant-a", "test:provider-a",
            "test:service-a", network
                ? SyntheticProviderPath.QualifiedPartnerNetwork
                : SyntheticProviderPath.DirectContract,
            network ? "test:network-a" : null, Guid.Empty,
            null, null, null, null);

    private static SyntheticProviderQualityNote Note(
        SyntheticProviderQualityNote root, SyntheticProviderQualityNoteAction action,
        long revision, Guid? id = null) =>
        root with
        {
            RequestId = id ?? Guid.NewGuid(),
            ExpectedRevision = revision,
            Action = action,
            OriginalScopeRequestId = root.RequestId,
            UnverifiedEvidenceRef = "test:paper-evidence-a",
            ClaimedActorRef = "test:claimed-actor",
            UnverifiedIncidentRef = action ==
                SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference
                ? "test:opaque-incident-a" : null,
            ClaimedReceivingAuthorityRef = action ==
                SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference
                ? "test:claimed-authority" : null
        };

    private static SyntheticProviderQualityChange Open(
        SyntheticProviderQualityNote root) =>
        SyntheticProviderQualityTrail.Apply(SyntheticProviderQualitySnapshot.Empty, root);

    [Fact]
    public void ASourceScopeHasNoQualityRatingNorVerifiedLicensing()
    {
        var root = Root();
        var result = Open(root);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.RecordedUnverified, result.Outcome);
        var read = SyntheticProviderQualityProjection.Project(result.Snapshot)!.Value;
        Assert.Equal(1, read.Revision);
        Assert.Equal("test:provider-a", read.ProviderRef);
        Assert.Equal(0, read.ClaimedTaranomStandardNoteCount);
        Assert.Equal(0, read.ClaimedNetworkControlNoteCount);
        Assert.Equal(0, read.ClaimedIncidentHandoffReferenceCount);
    }

    [Fact]
    public void NetworkCaseCanCollectThreeDifferentUnverifiedNoteTypes()
    {
        var root = Root(true);
        var first = Open(root);
        var second = SyntheticProviderQualityTrail.Apply(first.Snapshot,
            Note(root, SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote, 1));
        var third = SyntheticProviderQualityTrail.Apply(second.Snapshot,
            Note(root, SyntheticProviderQualityNoteAction.RecordUnverifiedNetworkProviderControlNote, 2));
        var fourth = SyntheticProviderQualityTrail.Apply(third.Snapshot,
            Note(root, SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference, 3));
        Assert.Equal(SyntheticProviderQualityNoteOutcome.RecordedUnverified, fourth.Outcome);
        var read = SyntheticProviderQualityProjection.Project(fourth.Snapshot)!.Value;
        Assert.Equal(4, read.Revision);
        Assert.Equal(1, read.ClaimedTaranomStandardNoteCount);
        Assert.Equal(1, read.ClaimedNetworkControlNoteCount);
        Assert.Equal(1, read.ClaimedIncidentHandoffReferenceCount);
    }

    [Fact]
    public void DirectProviderCanHaveStandardAndIncidentNotesButNotNetworkDelegation()
    {
        var root = Root();
        var first = Open(root);
        var standard = SyntheticProviderQualityTrail.Apply(first.Snapshot,
            Note(root, SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote, 1));
        var incident = SyntheticProviderQualityTrail.Apply(standard.Snapshot,
            Note(root, SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference, 2));
        Assert.Equal(SyntheticProviderQualityNoteOutcome.RecordedUnverified, incident.Outcome);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.NetworkScopeDenied,
            SyntheticProviderQualityTrail.Apply(incident.Snapshot,
                Note(root, SyntheticProviderQualityNoteAction.RecordUnverifiedNetworkProviderControlNote, 3)).Outcome);
        Assert.Equal(3, incident.Snapshot.Revision);
    }

    [Fact]
    public void ExactHistoricalReplayIsIdempotentAfterFurtherNotes()
    {
        var root = Root(true);
        var first = Open(root);
        var standard = Note(root,
            SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote, 1);
        var second = SyntheticProviderQualityTrail.Apply(first.Snapshot, standard);
        var network = Note(root,
            SyntheticProviderQualityNoteAction.RecordUnverifiedNetworkProviderControlNote, 2);
        var third = SyntheticProviderQualityTrail.Apply(second.Snapshot, network);
        foreach (var old in new[] { root, standard, network })
        {
            var replay = SyntheticProviderQualityTrail.Apply(third.Snapshot, old);
            Assert.Equal(SyntheticProviderQualityNoteOutcome.Replayed, replay.Outcome);
            Assert.Same(third.Snapshot, replay.Snapshot);
        }
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("actor")]
    [InlineData("incident")]
    [InlineData("authority")]
    [InlineData("kind")]
    public void ReusedRequestIdWithChangedPayloadCannotRewriteHistory(string mode)
    {
        var root = Root(true);
        var first = Open(root);
        var note = Note(root,
            SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference, 1);
        var second = SyntheticProviderQualityTrail.Apply(first.Snapshot, note);
        var changed = mode switch
        {
            "evidence" => note with { UnverifiedEvidenceRef = "test:other" },
            "actor" => note with { ClaimedActorRef = "test:another" },
            "incident" => note with { UnverifiedIncidentRef = "test:different" },
            "authority" => note with { ClaimedReceivingAuthorityRef = "test:another" },
            "kind" => note with {
                Action = SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote,
                UnverifiedIncidentRef = null, ClaimedReceivingAuthorityRef = null
            },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderQualityNoteOutcome.ConflictingReplay,
            SyntheticProviderQualityTrail.Apply(second.Snapshot, changed).Outcome);
        Assert.Equal(2, second.Snapshot.Revision);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("tenant")]
    [InlineData("provider")]
    [InlineData("service")]
    [InlineData("path")]
    [InlineData("network")]
    [InlineData("origin")]
    public void NoCrossCaseOrNetworkOrClinicianSubstitution(string mutation)
    {
        var root = Root(true);
        var opened = Open(root);
        var note = Note(root,
            SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote, 1);
        note = mutation switch
        {
            "case" => note with { CaseRef = "test:case-b" },
            "tenant" => note with { TenantRef = "test:tenant-b" },
            "provider" => note with { ProviderRef = "test:provider-b" },
            "service" => note with { ServiceRef = "test:service-b" },
            "path" => note with {
                Path = SyntheticProviderPath.DirectContract, PartnerNetworkRef = null
            },
            "network" => note with { PartnerNetworkRef = "test:network-b" },
            "origin" => note with { OriginalScopeRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderQualityNoteOutcome.ScopeMismatch,
            SyntheticProviderQualityTrail.Apply(opened.Snapshot, note).Outcome);
    }

    [Theory]
    [InlineData("real-case")]
    [InlineData("real-provider")]
    [InlineData("real-service")]
    [InlineData("missing-tenant")]
    [InlineData("empty-request")]
    [InlineData("unknown-kind")]
    [InlineData("unknown-path")]
    [InlineData("negative-revision")]
    [InlineData("extra-evidence-at-root")]
    [InlineData("unexpected-origin")]
    public void MalformedScopeMustNotCreateCase(string mutation)
    {
        var x = Root();
        x = mutation switch
        {
            "real-case" => x with { CaseRef = "patient:123" },
            "real-provider" => x with { ProviderRef = "real:doctor" },
            "real-service" => x with { ServiceRef = "actual-care" },
            "missing-tenant" => x with { TenantRef = null },
            "empty-request" => x with { RequestId = Guid.Empty },
            "unknown-kind" => x with { Action = (SyntheticProviderQualityNoteAction)99 },
            "unknown-path" => x with { Path = (SyntheticProviderPath)99 },
            "negative-revision" => x with { ExpectedRevision = -1 },
            "extra-evidence-at-root" => x with { UnverifiedEvidenceRef = "test:opaque" },
            "unexpected-origin" => x with { OriginalScopeRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderQualityNoteOutcome.Invalid,
            Open(x).Outcome);
    }

    [Theory]
    [InlineData("missing-network")]
    [InlineData("real-network")]
    [InlineData("direct-network")]
    public void NetworkAndDirectClaimedSourcesNeverCross(string mutation)
    {
        var x = mutation == "direct-network"
            ? Root() with { PartnerNetworkRef = "test:network-a" }
            : Root(true) with { PartnerNetworkRef =
                mutation == "missing-network" ? null : "real:network" };
        Assert.Equal(SyntheticProviderQualityNoteOutcome.NetworkScopeDenied,
            Open(x).Outcome);
    }

    [Theory]
    [InlineData("missing-evidence")]
    [InlineData("missing-actor")]
    [InlineData("real-actor")]
    [InlineData("incident-no-reference")]
    [InlineData("incident-no-authority")]
    [InlineData("nonincident-extra-reference")]
    [InlineData("nonincident-extra-authority")]
    [InlineData("missing-origin")]
    public void InvalidHandoffOrOversightEvidenceCannotAppend(string mutation)
    {
        var root = Root(true);
        var opened = Open(root);
        var x = Note(root,
            SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference, 1);
        x = mutation switch
        {
            "missing-evidence" => x with { UnverifiedEvidenceRef = null },
            "missing-actor" => x with { ClaimedActorRef = null },
            "real-actor" => x with { ClaimedActorRef = "real:person" },
            "incident-no-reference" => x with { UnverifiedIncidentRef = null },
            "incident-no-authority" => x with { ClaimedReceivingAuthorityRef = null },
            "nonincident-extra-reference" => x with {
                Action = SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote,
                ClaimedReceivingAuthorityRef = null
            },
            "nonincident-extra-authority" => x with {
                Action = SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote,
                UnverifiedIncidentRef = null
            },
            "missing-origin" => x with { OriginalScopeRequestId = Guid.Empty },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderQualityNoteOutcome.Invalid,
            SyntheticProviderQualityTrail.Apply(opened.Snapshot, x).Outcome);
    }

    [Fact]
    public void MissingOriginalAndStaleCASDoNotFabricateHandoff()
    {
        var root = Root(true);
        var later = Note(root,
            SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference, 0);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.OriginalScopeMissing,
            SyntheticProviderQualityTrail.Apply(SyntheticProviderQualitySnapshot.Empty, later).Outcome);
        var opened = Open(root);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.StaleRevision,
            SyntheticProviderQualityTrail.Apply(opened.Snapshot, later).Outcome);
    }

    [Fact]
    public void CannotSecondOpenOrChangeServiceAfterRootWasRecorded()
    {
        var root = Root(true);
        var opened = Open(root);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.ScopeMismatch,
            SyntheticProviderQualityTrail.Apply(opened.Snapshot,
                Root(true, revision: 1)).Outcome);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.ScopeMismatch,
            SyntheticProviderQualityTrail.Apply(opened.Snapshot,
                Root(true, revision: 1) with { ServiceRef = "test:other" }).Outcome);
    }

    [Fact]
    public void NullSnapshotAndEmptyProjectionNeverImplyQualification()
    {
        var root = Root();
        Assert.Equal(SyntheticProviderQualityNoteOutcome.Invalid,
            SyntheticProviderQualityTrail.Apply(null, root).Outcome);
        Assert.Null(SyntheticProviderQualityProjection.Project(null));
        Assert.Null(SyntheticProviderQualityProjection.Project(
            SyntheticProviderQualitySnapshot.Empty));
    }

    [Fact]
    public void ReadContractCannotContainAProviderRatingOrLegalOutcome()
    {
        var fields = typeof(SyntheticProviderQualityRead).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(fields, f => new[] { "Score", "Rated",
            "Approved", "Qualified", "Suspended", "Negligence",
            "Penalty", "TreatmentAuthorized", "ReferralSent" }
            .Any(b => f.Contains(b, StringComparison.OrdinalIgnoreCase)));
    }
}
