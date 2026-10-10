using Taranom.ClinicalEvidenceIntake;
using Xunit;

namespace Taranom.ClinicalEvidenceIntake.Tests;

public sealed class ClinicalEvidenceIntakeTests
{
    private const string Center = "test:center-a";
    private const string Doctor = "test:doctor-a";
    private const string Representative = "test:operator-a";

    private static EvidenceIntakeSnapshot NewDoctor() =>
        EvidenceIntakeSnapshot.ForSyntheticSubject(
            Center, Doctor, EvidenceSubjectKind.Physician);

    private static EvidenceIntakeSnapshot NewManager() =>
        EvidenceIntakeSnapshot.ForSyntheticSubject(
            Center, Representative, EvidenceSubjectKind.CenterRepresentative);

    private static EvidenceSubmission SubmitDoctor(long version = 0,
        EvidenceKind kind = EvidenceKind.PhysicianProfessionalQualificationClaim) =>
        new(Guid.NewGuid(), version, Center, Doctor,
            EvidenceSubjectKind.Physician, kind,
            Representative, "test:unverified-evidence-a");

    private static EvidenceSubmission SubmitManager(long version = 0) =>
        new(Guid.NewGuid(), version, Center, Representative,
            EvidenceSubjectKind.CenterRepresentative,
            EvidenceKind.CenterManagerAuthorityClaim,
            Representative, "test:operator-delegation-claim");

    [Fact]
    public void NewSubjectRequiresSyntheticCenterAndSubject()
    {
        Assert.Throws<ArgumentException>(() =>
            EvidenceIntakeSnapshot.ForSyntheticSubject(
                "real:hospital", Doctor, EvidenceSubjectKind.Physician));
        Assert.Throws<ArgumentException>(() =>
            EvidenceIntakeSnapshot.ForSyntheticSubject(
                Center, "national-id:abc", EvidenceSubjectKind.Physician));
        Assert.Throws<ArgumentException>(() =>
            EvidenceIntakeSnapshot.ForSyntheticSubject(
                Center, Doctor, (EvidenceSubjectKind)99));
    }

    [Fact]
    public void NewDoctorIsNotCertifiedOrContracted()
    {
        var state = NewDoctor();
        Assert.Equal(0, state.Revision);
        Assert.Empty(state.Submissions);
        Assert.Empty(state.ReviewWorkItems);
        Assert.DoesNotContain(Enum.GetNames<EvidenceIntakeOutcome>(),
            name => name.Contains("Approved", StringComparison.Ordinal) ||
                    name.Contains("Verified", StringComparison.Ordinal) ||
                    name.Contains("Active", StringComparison.Ordinal));
    }

    [Fact]
    public void NewManagerHasNoDelegationOrAuthenticatedSession()
    {
        var state = NewManager();
        Assert.Equal(EvidenceSubjectKind.CenterRepresentative, state.SubjectKind);
        Assert.Empty(state.ReviewWorkItems);
    }

    [Fact]
    public void ProfessionalClaimCreatesReviewWorkItemNotMedicalLicense()
    {
        var cmd = SubmitDoctor();
        var accepted = ClinicalEvidenceIntake.Submit(NewDoctor(), cmd);
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated, accepted.Outcome);
        Assert.Equal(1L, accepted.Snapshot.Revision);
        Assert.Single(accepted.Snapshot.Submissions);
        var item = Assert.Single(accepted.Snapshot.ReviewWorkItems);
        Assert.Equal(EvidenceKind.PhysicianProfessionalQualificationClaim, item.EvidenceKind);
        Assert.Equal(cmd.OpaqueEvidenceRef, item.OpaqueEvidenceRef);
        Assert.Equal(cmd.SubmittingActorRef, item.SubmittingActorRef);
    }

    [Fact]
    public void ProviderContractClaimIsNotEquivalentToProfessionalLicense()
    {
        var first = ClinicalEvidenceIntake.Submit(NewDoctor(), SubmitDoctor()).Snapshot;
        var second = ClinicalEvidenceIntake.Submit(first, SubmitDoctor(1,
            EvidenceKind.PhysicianTaranomServiceContractClaim));
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated, second.Outcome);
        Assert.Equal(2, second.Snapshot.ReviewWorkItems.Length);
        Assert.NotEqual(second.Snapshot.ReviewWorkItems[0].EvidenceKind,
            second.Snapshot.ReviewWorkItems[1].EvidenceKind);
        Assert.Equal(2, second.Snapshot.Submissions.Length);
    }

    [Fact]
    public void CenterManagerClaimDoesNotGrantIdentityOrPermission()
    {
        var accepted = ClinicalEvidenceIntake.Submit(NewManager(), SubmitManager());
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated, accepted.Outcome);
        Assert.Single(accepted.Snapshot.ReviewWorkItems);
        Assert.Equal(EvidenceKind.CenterManagerAuthorityClaim,
            accepted.Snapshot.ReviewWorkItems[0].EvidenceKind);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 3)]
    public void EvidenceSubjectAndClaimTypeMustMatch(int subjectKind, int kind)
    {
        var state = subjectKind == 1 ? NewManager() : NewDoctor();
        var cmd = subjectKind == 1 ? SubmitManager() : SubmitDoctor();
        cmd = cmd with { EvidenceKind = (EvidenceKind)kind };
        Assert.Equal(EvidenceIntakeOutcome.Invalid,
            ClinicalEvidenceIntake.Submit(state, cmd).Outcome);
    }

    [Fact]
    public void WrongCenterCannotSubmitEvidenceInAnotherCentersCase()
    {
        var state = NewDoctor();
        var changed = ClinicalEvidenceIntake.Submit(state, SubmitDoctor() with {
            CenterRef = "test:center-b"
        });
        Assert.Equal(EvidenceIntakeOutcome.ScopeDenied, changed.Outcome);
        Assert.Same(state, changed.Snapshot);
    }

    [Fact]
    public void WrongSubjectCannotImpersonateDoctor()
    {
        var state = NewDoctor();
        var changed = ClinicalEvidenceIntake.Submit(state, SubmitDoctor() with {
            SubjectRef = "test:doctor-b"
        });
        Assert.Equal(EvidenceIntakeOutcome.ScopeDenied, changed.Outcome);
        Assert.Empty(changed.Snapshot.ReviewWorkItems);
    }

    [Fact]
    public void SubjectKindMismatchCannotSubmitClaim()
    {
        var result = ClinicalEvidenceIntake.Submit(NewDoctor(),
            SubmitDoctor() with { SubjectKind = EvidenceSubjectKind.CenterRepresentative });
        Assert.Equal(EvidenceIntakeOutcome.Invalid, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("test:")]
    [InlineData("test:slash/child")]
    [InlineData("test: space")]
    [InlineData("real:document")]
    [InlineData("test:pipe|injection")]
    public void InvalidEvidenceRefIsRejected(string evidence)
    {
        var result = ClinicalEvidenceIntake.Submit(NewDoctor(),
            SubmitDoctor() with { OpaqueEvidenceRef = evidence });
        Assert.Equal(EvidenceIntakeOutcome.Invalid, result.Outcome);
        Assert.Empty(result.Snapshot.ReviewWorkItems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("test:")]
    [InlineData("real:operator-id")]
    [InlineData("test:../outside")]
    public void UntrustedOrMalformedSubmittingActorRefRejected(string actor)
    {
        var result = ClinicalEvidenceIntake.Submit(NewDoctor(),
            SubmitDoctor() with { SubmittingActorRef = actor });
        Assert.Equal(EvidenceIntakeOutcome.Invalid, result.Outcome);
        Assert.Empty(result.Snapshot.Submissions);
    }

    [Fact]
    public void MissingFieldsDoNotCreateWorkItem()
    {
        var cmd = SubmitDoctor() with { OpaqueEvidenceRef = null };
        Assert.Equal(EvidenceIntakeOutcome.Invalid,
            ClinicalEvidenceIntake.Submit(NewDoctor(), cmd).Outcome);
        Assert.Equal(EvidenceIntakeOutcome.Invalid,
            ClinicalEvidenceIntake.Submit(NewDoctor(),
                cmd with { OpaqueEvidenceRef = "test:proof", SubmittingActorRef = null }).Outcome);
    }

    [Fact]
    public void InvalidRequestIdCannotCreateSubmission()
    {
        var cmd = SubmitDoctor() with { RequestId = Guid.Empty };
        Assert.Equal(EvidenceIntakeOutcome.Invalid,
            ClinicalEvidenceIntake.Submit(NewDoctor(), cmd).Outcome);
    }

    [Fact]
    public void NegativeExpectedRevisionFails()
    {
        var cmd = SubmitDoctor() with { ExpectedRevision = -1 };
        Assert.Equal(EvidenceIntakeOutcome.Invalid,
            ClinicalEvidenceIntake.Submit(NewDoctor(), cmd).Outcome);
    }

    [Fact]
    public void StaleExpectedRevisionCannotRewriteEvidence()
    {
        var state = ClinicalEvidenceIntake.Submit(NewDoctor(), SubmitDoctor()).Snapshot;
        var rejected = ClinicalEvidenceIntake.Submit(state, SubmitDoctor(0,
            EvidenceKind.PhysicianTaranomServiceContractClaim));
        Assert.Equal(EvidenceIntakeOutcome.StaleRevision, rejected.Outcome);
        Assert.Single(rejected.Snapshot.Submissions);
    }

    [Fact]
    public void ExactHistoricalReplayDoesNotCreateSecondWorkItem()
    {
        var first = SubmitDoctor();
        var accepted = ClinicalEvidenceIntake.Submit(NewDoctor(), first);
        var second = SubmitDoctor(1, EvidenceKind.PhysicianTaranomServiceContractClaim);
        var latter = ClinicalEvidenceIntake.Submit(accepted.Snapshot, second);
        var replay = ClinicalEvidenceIntake.Submit(latter.Snapshot, first);
        Assert.Equal(EvidenceIntakeOutcome.Replayed, replay.Outcome);
        Assert.Same(latter.Snapshot, replay.Snapshot);
        Assert.Equal(2, replay.Snapshot.ReviewWorkItems.Length);
    }

    [Fact]
    public void SameRequestIdWithChangedEvidenceConflicts()
    {
        var original = SubmitDoctor();
        var state = ClinicalEvidenceIntake.Submit(NewDoctor(), original).Snapshot;
        var other = original with { OpaqueEvidenceRef = "test:modified-evidence" };
        var result = ClinicalEvidenceIntake.Submit(state, other);
        Assert.Equal(EvidenceIntakeOutcome.ConflictingReplay, result.Outcome);
        Assert.Same(state, result.Snapshot);
    }

    [Fact]
    public void SameRequestIdWithChangedActorConflicts()
    {
        var original = SubmitDoctor();
        var state = ClinicalEvidenceIntake.Submit(NewDoctor(), original).Snapshot;
        var altered = original with { SubmittingActorRef = "test:other-actor" };
        Assert.Equal(EvidenceIntakeOutcome.ConflictingReplay,
            ClinicalEvidenceIntake.Submit(state, altered).Outcome);
    }

    [Fact]
    public void EveryAcceptedClaimRetainsEarlierHistory()
    {
        var before = NewDoctor();
        var first = ClinicalEvidenceIntake.Submit(before, SubmitDoctor());
        var second = ClinicalEvidenceIntake.Submit(first.Snapshot,
            SubmitDoctor(1, EvidenceKind.PhysicianTaranomServiceContractClaim));
        Assert.Empty(before.Submissions);
        Assert.Single(first.Snapshot.Submissions);
        Assert.Equal(2, second.Snapshot.Submissions.Length);
        Assert.Equal(new long[] {1, 2},
            second.Snapshot.ReviewWorkItems.Select(w => w.Revision).ToArray());
    }

    [Fact]
    public void NullSnapshotNeverMeansAnApprovedNewCase()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ClinicalEvidenceIntake.Submit(null, SubmitDoctor()));
    }

    [Fact]
    public void SubjectCannotBeAnotherCentersRepresentativeByChangingClaim()
    {
        var original = NewManager();
        var cmd = SubmitManager() with { SubjectRef = "test:manager-b" };
        Assert.Equal(EvidenceIntakeOutcome.ScopeDenied,
            ClinicalEvidenceIntake.Submit(original, cmd).Outcome);
    }

    [Fact]
    public void NoResultCanGrantDoctorOrCenterPermissions()
    {
        var names = Enum.GetNames<EvidenceIntakeOutcome>();
        Assert.DoesNotContain(names, name =>
            name.Contains("Grant", StringComparison.Ordinal) ||
            name.Contains("Qualified", StringComparison.Ordinal) ||
            name.Contains("Activated", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ReviewWorkItem).GetProperties(),
            p => p.Name is "Decision" or "Approved" or "Qualified" or "Verified");
    }
}
