using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticClinicalAccessTransparencyTrailTests
{
    private static SyntheticRecordDisclosureInput Scope => new(
        SyntheticRecordRequester.Clinician,
        SyntheticRecordPurpose.ClinicianTreatmentNeed,
        "test:tenant-A", "test:tenant-A",
        "test:member-A", "test:member-A", "test:member-A",
        "test:clinician-A", "test:clinician-A", "test:clinician-A",
        "test:care-service-A", "test:care-service-A",
        "test:category-A", "test:category-A",
        "test:clinical-need-A", "test:record-source-A",
        1, 1, 1,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed);

    private static SelfReadBoundaryInput Viewer => new(
        SelfReadActorKind.Member,
        "test:member-A", "test:tenant-A",
        "test:member-A", "test:tenant-A",
        null, null,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        new ReadPrerequisiteSnapshot(
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed));

    private static SyntheticAccessNoteCommand Open(
        SyntheticRecordDisclosureInput? scope = null,
        Guid? id = null, long revision = 0) =>
        new(id ?? Guid.NewGuid(), revision,
            SyntheticAccessNoteAction.OpenUnverifiedClinicianRequest,
            scope ?? Scope, Guid.Empty, null, null);

    private static SyntheticAccessNoteCommand Concern(
        SyntheticAccessNoteCommand root, long revision = 1,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), revision,
            SyntheticAccessNoteAction.RecordUnverifiedMemberConcern,
            root.ClaimedDisclosureScope,
            root.RequestId, root.ClaimedDisclosureScope.MemberRef,
            "test:member-paper-concern");

    private static SyntheticAccessNoteCommand Oversight(
        SyntheticAccessNoteCommand root, long revision = 1,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), revision,
            SyntheticAccessNoteAction.RecordUnverifiedOversightNote,
            root.ClaimedDisclosureScope, root.RequestId,
            "test:unverified-independent-reviewer", "test:oversight-paper");

    private static SyntheticAccessNoteChange Apply(
        SyntheticAccessTransparencySnapshot snapshot, SyntheticAccessNoteCommand c) =>
        SyntheticClinicalAccessTransparencyTrail.Apply(snapshot, c);

    [Fact]
    public void OpenRequestIsOnlyUnverifiedPaperEvenWhenAllChecksClaimConfirmed()
    {
        var root = Open();
        var opened = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        Assert.Equal(SyntheticAccessNoteOutcome.RecordedUnverified, opened.Outcome);
        var read = SyntheticClinicalAccessTransparencyTrail.ProjectForClaimedMember(
            opened.Snapshot, Viewer);
        Assert.Equal(SyntheticAccessTransparencyState.UnverifiedPaperOnly, read.State);
        Assert.Equal(SyntheticRecordDisclosureResult.PreconditionsPresentOnly,
            read.UntrustedPrerequisiteAssessment);
        Assert.Equal(1, read.UnverifiedRequestCount);
        Assert.Equal(0, read.UnverifiedMemberConcernCount);
        Assert.Equal(0, read.UnverifiedOversightNoteCount);
        Assert.False(read.RecordReadOccurred);
        Assert.False(read.DisclosureGranted);
        Assert.False(read.LegalExceptionEstablished);
    }

    [Fact]
    public void MemberConcernAndOversightAreIndependentUnverifiedPaperNotes()
    {
        var root = Open();
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var concern = Concern(root);
        var two = Apply(one.Snapshot, concern);
        var oversight = Oversight(root, 2);
        var three = Apply(two.Snapshot, oversight);
        Assert.Equal(SyntheticAccessNoteOutcome.RecordedUnverified, three.Outcome);
        var read = SyntheticClinicalAccessTransparencyTrail.ProjectForClaimedMember(
            three.Snapshot, Viewer);
        Assert.Equal(3, read.Revision);
        Assert.Equal(1, read.UnverifiedMemberConcernCount);
        Assert.Equal(1, read.UnverifiedOversightNoteCount);
        Assert.False(read.RecordReadOccurred);
        Assert.False(read.DisclosureGranted);
    }

    [Fact]
    public void OversightMayPrecedeConcernWithoutConveyingLegalAuthority()
    {
        var root = Open();
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var two = Apply(one.Snapshot, Oversight(root));
        var three = Apply(two.Snapshot, Concern(root, 2));
        Assert.Equal(SyntheticAccessNoteOutcome.RecordedUnverified, three.Outcome);
        Assert.Equal(3, three.Snapshot.Revision);
    }

    [Fact]
    public void ReplayingAnyHistoricalExactRequestIsIdempotentAfterLaterNotes()
    {
        var root = Open();
        var concern = Concern(root);
        var oversight = Oversight(root, 2);
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var two = Apply(one.Snapshot, concern);
        var three = Apply(two.Snapshot, oversight);
        foreach (var command in new[] { root, concern, oversight })
        {
            var replay = Apply(three.Snapshot, command);
            Assert.Equal(SyntheticAccessNoteOutcome.Replayed, replay.Outcome);
            Assert.Same(three.Snapshot, replay.Snapshot);
        }
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("paper")]
    [InlineData("member")]
    [InlineData("tenant")]
    [InlineData("clinician")]
    [InlineData("service")]
    [InlineData("category")]
    [InlineData("member-choice")]
    [InlineData("member-revision")]
    [InlineData("action")]
    [InlineData("origin")]
    public void DuplicateUuidWithChangedPayloadCannotRewritePaperHistory(string mode)
    {
        var root = Open();
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var saved = Concern(root);
        var two = Apply(one.Snapshot, saved);
        var changed = mode switch
        {
            "actor" => saved with { ClaimedActorRef = "test:member-B" },
            "paper" => saved with { UnverifiedEvidenceRef = "test:altered-paper" },
            "member" => saved with { ClaimedDisclosureScope = Scope with {
                MemberRef = "test:member-B", RecordOwnerMemberRef = "test:member-B",
                MemberSharingSubjectRef = "test:member-B" } },
            "tenant" => saved with { ClaimedDisclosureScope = Scope with {
                TenantRef = "test:tenant-B", RecordOwnerTenantRef = "test:tenant-B" } },
            "clinician" => saved with { ClaimedDisclosureScope = Scope with {
                ClaimedRequesterRef = "test:clinician-B",
                ClaimedClinicianRef = "test:clinician-B",
                ClaimedScopedClinicianRef = "test:clinician-B" } },
            "service" => saved with { ClaimedDisclosureScope = Scope with {
                ClinicalServiceRef = "test:service-B", ClaimedScopedServiceRef = "test:service-B" } },
            "category" => saved with { ClaimedDisclosureScope = Scope with {
                RequestedDataCategoryRef = "test:category-B",
                ClaimedScopedDataCategoryRef = "test:category-B" } },
            "member-choice" => saved with { ClaimedDisclosureScope = Scope with {
                CurrentMemberSharingChoice = EvidenceCheck.Missing } },
            "member-revision" => saved with { ClaimedDisclosureScope = Scope with {
                MemberControlRevision = 2 } },
            "action" => saved with { Action =
                SyntheticAccessNoteAction.RecordUnverifiedOversightNote },
            "origin" => saved with { OriginalRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        Assert.NotEqual(SyntheticAccessNoteOutcome.RecordedUnverified,
            Apply(two.Snapshot, changed).Outcome);
        Assert.Equal(2, two.Snapshot.Revision);
    }

    [Theory]
    [InlineData("changed-record-source")]
    [InlineData("changed-member-choice-revision")]
    [InlineData("changed-clinical-need-revision")]
    [InlineData("different-clinician")]
    [InlineData("different-service")]
    [InlineData("different-category")]
    [InlineData("different-member")]
    [InlineData("different-tenant")]
    [InlineData("different-legal-check")]
    public void FreshNoteCannotRebindOriginalClaimedScope(string mode)
    {
        var root = Open();
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var c = Concern(root);
        var scope = mode switch
        {
            "changed-record-source" => Scope with {
                UnverifiedRecordSourceRef = "test:changed-record" },
            "changed-member-choice-revision" => Scope with { MemberControlRevision = 2 },
            "changed-clinical-need-revision" => Scope with { ClinicalNeedRevision = 2 },
            "different-clinician" => Scope with {
                ClaimedRequesterRef = "test:clinician-B", ClaimedClinicianRef = "test:clinician-B",
                ClaimedScopedClinicianRef = "test:clinician-B" },
            "different-service" => Scope with {
                ClinicalServiceRef = "test:service-B", ClaimedScopedServiceRef = "test:service-B" },
            "different-category" => Scope with {
                RequestedDataCategoryRef = "test:category-B",
                ClaimedScopedDataCategoryRef = "test:category-B" },
            "different-member" => Scope with {
                MemberRef = "test:member-B", RecordOwnerMemberRef = "test:member-B",
                MemberSharingSubjectRef = "test:member-B" },
            "different-tenant" => Scope with {
                TenantRef = "test:tenant-B", RecordOwnerTenantRef = "test:tenant-B" },
            "different-legal-check" => Scope with {
                CurrentLawfulDisclosureBasis = EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        c = c with { ClaimedDisclosureScope = scope,
            ClaimedActorRef = scope.MemberRef };
        Assert.Equal(SyntheticAccessNoteOutcome.ScopeChanged,
            Apply(one.Snapshot, c).Outcome);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void SponsorEmployerCoordinatorNetworkAndMemberCannotOpenClinicianTrail(int kind)
    {
        var scope = Scope with { RequesterKind = (SyntheticRecordRequester)kind };
        var result = Apply(SyntheticAccessTransparencySnapshot.Empty, Open(scope));
        Assert.Equal(SyntheticAccessNoteOutcome.Invalid, result.Outcome);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MemberSelfResearchAndEmergencyPurposesCannotOpenTreatmentTrail(int purpose)
    {
        Assert.Equal(SyntheticAccessNoteOutcome.Invalid,
            Apply(SyntheticAccessTransparencySnapshot.Empty,
                Open(Scope with { Purpose = (SyntheticRecordPurpose)purpose })).Outcome);
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing)]
    [InlineData(EvidenceCheck.Rejected)]
    public void MissingOrExplicitlyDeniedMemberSharingNeverImpliesRecordDisclosure(
        EvidenceCheck value)
    {
        var root = Open(Scope with { CurrentMemberSharingChoice = value });
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        Assert.Equal(SyntheticAccessNoteOutcome.RecordedUnverified, one.Outcome);
        var read = SyntheticClinicalAccessTransparencyTrail.ProjectForClaimedMember(
            one.Snapshot, Viewer);
        Assert.Equal(value == EvidenceCheck.Rejected
            ? SyntheticRecordDisclosureResult.MemberDeclined
            : SyntheticRecordDisclosureResult.EvidenceMissing,
            read.UntrustedPrerequisiteAssessment);
        Assert.False(read.DisclosureGranted);
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("negative-revision")]
    [InlineData("missing-scope")]
    [InlineData("real-member")]
    [InlineData("bad-category")]
    [InlineData("open-with-actor")]
    [InlineData("open-with-evidence")]
    [InlineData("open-with-origin")]
    public void InvalidRootNeverCreatesAcceptedRequest(string mutation)
    {
        var c = Open();
        c = mutation switch
        {
            "missing-id" => c with { RequestId = Guid.Empty },
            "negative-revision" => c with { ExpectedRevision = -1 },
            "missing-scope" => c with { ClaimedDisclosureScope = default },
            "real-member" => c with { ClaimedDisclosureScope = Scope with {
                MemberRef = "real:person" } },
            "bad-category" => c with { ClaimedDisclosureScope = Scope with {
                RequestedDataCategoryRef = "test:invalid path" } },
            "open-with-actor" => c with { ClaimedActorRef = "test:actor" },
            "open-with-evidence" => c with { UnverifiedEvidenceRef = "test:paper" },
            "open-with-origin" => c with { OriginalRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticAccessNoteOutcome.Invalid,
            Apply(SyntheticAccessTransparencySnapshot.Empty, c).Outcome);
    }

    [Theory]
    [InlineData("missing-original")]
    [InlineData("unrelated-original")]
    [InlineData("missing-actor")]
    [InlineData("missing-paper")]
    [InlineData("real-paper")]
    [InlineData("claimed-other-member")]
    public void InvalidOrMissingMemberConcernFailsClosed(string change)
    {
        var root = Open();
        var opened = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var c = Concern(root);
        c = change switch
        {
            "missing-original" => c with { OriginalRequestId = Guid.Empty },
            "unrelated-original" => c with { OriginalRequestId = Guid.NewGuid() },
            "missing-actor" => c with { ClaimedActorRef = null },
            "missing-paper" => c with { UnverifiedEvidenceRef = null },
            "real-paper" => c with { UnverifiedEvidenceRef = "real:patient-file" },
            "claimed-other-member" => c with { ClaimedActorRef = "test:member-B" },
            _ => throw new ArgumentException()
        };
        Assert.NotEqual(SyntheticAccessNoteOutcome.RecordedUnverified,
            Apply(opened.Snapshot, c).Outcome);
    }

    [Theory]
    [InlineData("clinician-self-review")]
    [InlineData("member-as-overseer")]
    [InlineData("missing-actor")]
    public void UnverifiedOversightCannotImpersonateClinicianOrMember(string mode)
    {
        var root = Open();
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var c = Oversight(root);
        c = mode switch
        {
            "clinician-self-review" => c with {
                ClaimedActorRef = Scope.ClaimedClinicianRef },
            "member-as-overseer" => c with { ClaimedActorRef = Scope.MemberRef },
            "missing-actor" => c with { ClaimedActorRef = null },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticAccessNoteOutcome.Invalid,
            Apply(one.Snapshot, c).Outcome);
    }

    [Fact]
    public void MissingRootAndSecondRootDoNotBecomeAccessOrOversight()
    {
        var root = Open();
        Assert.Equal(SyntheticAccessNoteOutcome.OriginalMissing,
            Apply(SyntheticAccessTransparencySnapshot.Empty, Concern(root, 0)).Outcome);
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        Assert.Equal(SyntheticAccessNoteOutcome.ScopeChanged,
            Apply(one.Snapshot, Open(revision: 1)).Outcome);
    }

    [Fact]
    public void DuplicateConcernOversightAndStaleNoteCannotMutateOriginal()
    {
        var root = Open();
        var one = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var two = Apply(one.Snapshot, Concern(root));
        Assert.Equal(SyntheticAccessNoteOutcome.DuplicateStage,
            Apply(two.Snapshot, Concern(root, 2)).Outcome);
        Assert.Equal(SyntheticAccessNoteOutcome.StaleRevision,
            Apply(two.Snapshot, Oversight(root, 1)).Outcome);
        var three = Apply(two.Snapshot, Oversight(root, 2));
        Assert.Equal(SyntheticAccessNoteOutcome.DuplicateStage,
            Apply(three.Snapshot, Oversight(root, 3)).Outcome);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(0)]
    public void NonMemberViewerCannotLearnAnyUnverifiedCaseExistence(int viewer)
    {
        var root = Open();
        var opened = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var state = SyntheticClinicalAccessTransparencyTrail.ProjectForClaimedMember(
            opened.Snapshot, Viewer with { ActorKind = (SelfReadActorKind)viewer });
        Assert.Equal(SyntheticAccessTransparencyState.ViewerDenied, state.State);
        Assert.Equal(0, state.Revision);
        Assert.Null(state.UntrustedPrerequisiteAssessment);
    }

    [Theory]
    [InlineData("other-member")]
    [InlineData("other-tenant")]
    [InlineData("fake-server-member")]
    [InlineData("fake-server-tenant")]
    public void CrossTenantOrMemberViewerLearnsNoClinicalScope(string mode)
    {
        var root = Open();
        var opened = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var viewer = mode switch
        {
            "other-member" => Viewer with { ResourceOwnerMemberRef = "test:member-B" },
            "other-tenant" => Viewer with { ResourceOwnerTenantRef = "test:tenant-B" },
            "fake-server-member" => Viewer with { ServerResolvedMemberRef = "test:member-B" },
            "fake-server-tenant" => Viewer with { ServerResolvedTenantRef = "test:tenant-B" },
            _ => throw new ArgumentException()
        };
        var read = SyntheticClinicalAccessTransparencyTrail.ProjectForClaimedMember(
            opened.Snapshot, viewer);
        Assert.Equal(SyntheticAccessTransparencyState.ViewerScopeMismatch, read.State);
        Assert.Equal(0, read.UnverifiedRequestCount);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("unsigned-member")]
    [InlineData("no-lawful-basis")]
    [InlineData("invalid-browser-hint")]
    [InlineData("missing-audit")]
    public void MemberViewerStillNeedsIndependentSelfReadPrerequisites(string mode)
    {
        var root = Open();
        var opened = Apply(SyntheticAccessTransparencySnapshot.Empty, root);
        var viewer = mode switch
        {
            "revoked" => Viewer with { CurrentSessionNotRevoked = EvidenceCheck.Rejected },
            "unsigned-member" => Viewer with { CurrentIdentityMemberLink = EvidenceCheck.Missing },
            "no-lawful-basis" => Viewer with { OtherReadPrerequisites =
                Viewer.OtherReadPrerequisites with {
                    OperationSpecificLawfulBasis = EvidenceCheck.Missing } },
            "invalid-browser-hint" => Viewer with {
                BrowserMemberHint = "test:other-member" },
            "missing-audit" => Viewer with { OtherReadPrerequisites =
                Viewer.OtherReadPrerequisites with {
                    AuditableDecisionPath = EvidenceCheck.Missing } },
            _ => throw new ArgumentException()
        };
        var read = SyntheticClinicalAccessTransparencyTrail.ProjectForClaimedMember(
            opened.Snapshot, viewer);
        Assert.Equal(SyntheticAccessTransparencyState.ViewerDenied, read.State);
        Assert.Null(read.UntrustedPrerequisiteAssessment);
    }

    [Fact]
    public void NoSnapshotAndNoRealPatientDataOrAccessPermission()
    {
        Assert.Equal(SyntheticAccessNoteOutcome.Invalid,
            SyntheticClinicalAccessTransparencyTrail.Apply(null, Open()).Outcome);
        var empty = SyntheticClinicalAccessTransparencyTrail.ProjectForClaimedMember(
            SyntheticAccessTransparencySnapshot.Empty, Viewer);
        Assert.Equal(SyntheticAccessTransparencyState.Unavailable, empty.State);
        Assert.False(empty.RecordReadOccurred);
        Assert.False(empty.DisclosureGranted);
        foreach (var p in typeof(SyntheticAccessTransparencyRead).GetProperties())
            Assert.DoesNotContain(new[] { "Diagnosis", "MedicalText", "RecordPayload",
                "NationalId", "Signature", "PatientName", "ReadToken" }, s =>
                p.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
    }
}
