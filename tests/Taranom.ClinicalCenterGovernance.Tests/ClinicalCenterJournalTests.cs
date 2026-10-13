using Taranom.ClinicalCenterGovernance;
using Xunit;

namespace Taranom.ClinicalCenterGovernance.Tests;

public sealed class ClinicalCenterJournalTests
{
    private const string Center = "test:centerA";
    private const string OtherCenter = "test:centerB";
    private const string WardOne = "test:ward_1";
    private const string WardTwo = "test:ward-2";
    private const string Doctor = "test:doctor_1";

    private static CenterSnapshot Empty() => CenterSnapshot.NewSynthetic(Center);

    private static CenterCommand Command(CenterCommandKind kind, long version,
        string? department = null, string? doctor = null) =>
        new(Guid.NewGuid(), version, kind, Center, Center, department, doctor,
            CenterEvidence.Confirmed, CenterEvidence.Confirmed);

    private static CenterChange Do(CenterSnapshot current, CenterCommand c) =>
        ClinicalCenterJournal.Apply(current, c);

    private static CenterSnapshot WithOneDepartment() =>
        Do(Empty(), Command(CenterCommandKind.DefineDepartment, 0, WardOne)).Snapshot;

    private static CenterSnapshot WithDoctor() =>
        Do(WithOneDepartment(), Command(
            CenterCommandKind.RecordDoctorAffiliation, 1, doctor: Doctor)).Snapshot;

    private static CenterSnapshot WithAssignment() =>
        Do(WithDoctor(), Command(
            CenterCommandKind.AssignDoctorToDepartment, 2, WardOne, Doctor)).Snapshot;

    [Fact]
    public void NewCenterHasNoInheritedNetworkOrActiveDoctor()
    {
        var state = Empty();
        Assert.Equal(0L, state.Version);
        Assert.Empty(state.Departments);
        Assert.Empty(state.Doctors);
        Assert.Empty(state.DoctorDepartments);
        Assert.Empty(state.Events);
    }

    [Theory]
    [InlineData("real-hospital-identity")]
    [InlineData("test:")]
    [InlineData("test:parent/child")]
    [InlineData("test: space")]
    [InlineData("")]
    public void CannotCreateCenterWithNonSyntheticOrHierarchicalRef(string key)
    {
        Assert.Throws<ArgumentException>(() => CenterSnapshot.NewSynthetic(key));
    }

    [Fact]
    public void CannotPassMissingAggregate()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ClinicalCenterJournal.Apply(null, Command(CenterCommandKind.DefineDepartment, 0, WardOne)));
    }

    [Fact]
    public void CenterCanDefineTwoDepartmentsUnderTheSameRecord()
    {
        var first = Do(Empty(), Command(CenterCommandKind.DefineDepartment, 0, WardOne));
        var second = Do(first.Snapshot, Command(CenterCommandKind.DefineDepartment, 1, WardTwo));
        Assert.Equal(CenterOutcome.Applied, first.Outcome);
        Assert.Equal(CenterOutcome.Applied, second.Outcome);
        Assert.Equal(Center, second.Snapshot.CenterRef);
        Assert.Equal(2L, second.Snapshot.Version);
        Assert.Equal(2, second.Snapshot.Departments.Count);
        Assert.Equal(0L, Empty().Version);
    }

    [Fact]
    public void RegisteredDoctorIsAlwaysUnverifiedAndHasNoDefaultWard()
    {
        var state = WithDoctor();
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, state.Doctors[Doctor]);
        Assert.Empty(state.DoctorDepartments);
    }

    [Fact]
    public void AffiliationMayBeAssignedToTwoDepartmentsInsideCenter()
    {
        var one = WithAssignment();
        var two = Do(one, Command(CenterCommandKind.DefineDepartment, 3, WardTwo)).Snapshot;
        var linked = Do(two, Command(
            CenterCommandKind.AssignDoctorToDepartment, 4, WardTwo, Doctor));
        Assert.Equal(CenterOutcome.Applied, linked.Outcome);
        Assert.Equal(2, linked.Snapshot.DoctorDepartments[Doctor].Count);
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification,
            linked.Snapshot.Doctors[Doctor]);
    }

    [Fact]
    public void DoctorMustBeDefinedBeforeDepartmentAssignment()
    {
        var state = WithOneDepartment();
        var attempt = Do(state, Command(
            CenterCommandKind.AssignDoctorToDepartment, 1, WardOne, Doctor));
        Assert.Equal(CenterOutcome.NotApplicable, attempt.Outcome);
        Assert.Same(state, attempt.Snapshot);
    }

    [Fact]
    public void DepartmentMustExistBeforeAssignment()
    {
        var state = WithDoctor();
        var attempt = Do(state, Command(
            CenterCommandKind.AssignDoctorToDepartment, 2, WardTwo, Doctor));
        Assert.Equal(CenterOutcome.NotApplicable, attempt.Outcome);
        Assert.Empty(attempt.Snapshot.DoctorDepartments);
    }

    [Fact]
    public void EndAffiliationClearsCurrentAssignmentsButNotHistory()
    {
        var prior = WithAssignment();
        var ended = Do(prior, Command(
            CenterCommandKind.EndDoctorAffiliation, 3, doctor: Doctor));
        Assert.Equal(CenterOutcome.Applied, ended.Outcome);
        Assert.Equal(DoctorAffiliation.Ended, ended.Snapshot.Doctors[Doctor]);
        Assert.False(ended.Snapshot.DoctorDepartments.ContainsKey(Doctor));
        Assert.Equal(4, ended.Snapshot.Events.Length);
        Assert.Equal(3, prior.Events.Length);
        Assert.Single(prior.DoctorDepartments[Doctor]);
    }

    [Fact]
    public void EndedDoctorCannotBeSilentlyRestoredOrAssigned()
    {
        var ended = Do(WithAssignment(), Command(
            CenterCommandKind.EndDoctorAffiliation, 3, doctor: Doctor)).Snapshot;
        Assert.Equal(CenterOutcome.NotApplicable, Do(ended, Command(
            CenterCommandKind.RecordDoctorAffiliation, 4, doctor: Doctor)).Outcome);
        Assert.Equal(CenterOutcome.NotApplicable, Do(ended, Command(
            CenterCommandKind.AssignDoctorToDepartment, 4, WardOne, Doctor)).Outcome);
        Assert.Equal(4L, ended.Version);
    }

    [Fact]
    public void DuplicateDepartmentAndDoctorDoNotAdvanceVersion()
    {
        var state = WithDoctor();
        Assert.Equal(CenterOutcome.NotApplicable,
            Do(state, Command(CenterCommandKind.DefineDepartment, 2, WardOne)).Outcome);
        Assert.Equal(CenterOutcome.NotApplicable,
            Do(state, Command(CenterCommandKind.RecordDoctorAffiliation, 2,
                doctor: Doctor)).Outcome);
        Assert.Equal(2L, state.Version);
    }

    [Fact]
    public void DuplicateDepartmentAssignmentDoesNotAppend()
    {
        var state = WithAssignment();
        Assert.Equal(CenterOutcome.NotApplicable,
            Do(state, Command(CenterCommandKind.AssignDoctorToDepartment, 3,
                WardOne, Doctor)).Outcome);
        Assert.Equal(3, state.Events.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void CaseOrRoleClaimsCannotCrossOtherCenterBoundary(int scenario)
    {
        var input = Command(CenterCommandKind.DefineDepartment, 0, WardOne);
        input = scenario switch
        {
            0 => input with { TargetCenterRef = OtherCenter },
            2 => input with { ActingCenterRef = OtherCenter },
            _ => input with { TargetCenterRef = OtherCenter, ActingCenterRef = OtherCenter }
        };
        var decision = Do(Empty(), input);
        Assert.Equal(CenterOutcome.ScopeDenied, decision.Outcome);
        Assert.Empty(decision.Snapshot.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void AuthorityMissingRevokedOrUnrecognizedCannotDefineDoctor(int code)
    {
        var input = Command(CenterCommandKind.RecordDoctorAffiliation, 0, doctor: Doctor)
            with { CurrentCenterManagerAuthority = (CenterEvidence)code };
        var output = Do(Empty(), input);
        Assert.NotEqual(CenterOutcome.Applied, output.Outcome);
        Assert.Empty(output.Snapshot.Doctors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void AuditUnavailableOrUnknownBlocksChanges(int code)
    {
        var input = Command(CenterCommandKind.DefineDepartment, 0, WardOne)
            with { AuditPathAvailable = (CenterEvidence)code };
        var output = Do(Empty(), input);
        Assert.NotEqual(CenterOutcome.Applied, output.Outcome);
        Assert.Empty(output.Snapshot.Events);
    }

    [Fact]
    public void DeniedAuthorityTakesPrecedenceOverMissingAudit()
    {
        var input = Command(CenterCommandKind.DefineDepartment, 0, WardOne)
            with { CurrentCenterManagerAuthority = CenterEvidence.Rejected,
                   AuditPathAvailable = CenterEvidence.Missing };
        Assert.Equal(CenterOutcome.EvidenceDenied, Do(Empty(), input).Outcome);
    }

    [Fact]
    public void ExactIdempotentReplayAfterNewEventsDoesNotRewind()
    {
        var firstCommand = Command(CenterCommandKind.DefineDepartment, 0, WardOne);
        var first = Do(Empty(), firstCommand);
        var second = Do(first.Snapshot, Command(CenterCommandKind.DefineDepartment, 1, WardTwo));
        var replay = Do(second.Snapshot, firstCommand);
        Assert.Equal(CenterOutcome.Replayed, replay.Outcome);
        Assert.Same(second.Snapshot, replay.Snapshot);
        Assert.Equal(2, replay.Snapshot.Departments.Count);
    }

    [Fact]
    public void SameRequestIdWithChangedPayloadIsConflictingReplay()
    {
        var firstCommand = Command(CenterCommandKind.DefineDepartment, 0, WardOne);
        var first = Do(Empty(), firstCommand);
        var conflict = Do(first.Snapshot, firstCommand with { DepartmentRef = WardTwo });
        Assert.Equal(CenterOutcome.ConflictingReplay, conflict.Outcome);
        Assert.Same(first.Snapshot, conflict.Snapshot);
    }

    [Fact]
    public void DistinctConcurrentExpectedVersionCannotBothApply()
    {
        var first = Do(Empty(), Command(CenterCommandKind.DefineDepartment, 0, WardOne));
        var stale = Do(first.Snapshot, Command(CenterCommandKind.DefineDepartment, 0, WardTwo));
        Assert.Equal(CenterOutcome.StaleRevision, stale.Outcome);
        Assert.Single(stale.Snapshot.Departments);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-20)]
    public void NegativeExpectedRevisionRejected(long version)
    {
        var result = Do(Empty(), Command(CenterCommandKind.DefineDepartment, version, WardOne));
        Assert.Equal(CenterOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void EmptyRequestIdOrUnknownKindFailsClosed()
    {
        var first = Command(CenterCommandKind.DefineDepartment, 0, WardOne);
        Assert.Equal(CenterOutcome.Invalid, Do(Empty(),
            first with { RequestId = Guid.Empty }).Outcome);
        Assert.Equal(CenterOutcome.Invalid, Do(Empty(),
            first with { Kind = (CenterCommandKind)100 }).Outcome);
    }

    [Theory]
    [InlineData("doctor-name")]
    [InlineData("test:doctor/ward")]
    [InlineData("test:doctor with space")]
    [InlineData("test:")]
    public void UntrustedOrRealDoctorIdentifiersRejected(string id)
    {
        var result = Do(Empty(), Command(
            CenterCommandKind.RecordDoctorAffiliation, 0, doctor: id));
        Assert.Equal(CenterOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void CommandTypesRejectUnexpectedPayloadCombination()
    {
        var cases = new[]
        {
            Command(CenterCommandKind.DefineDepartment, 0, WardOne, Doctor),
            Command(CenterCommandKind.RecordDoctorAffiliation, 0, WardOne, Doctor),
            Command(CenterCommandKind.AssignDoctorToDepartment, 0, WardOne),
            Command(CenterCommandKind.AssignDoctorToDepartment, 0, doctor: Doctor),
            Command(CenterCommandKind.EndDoctorAffiliation, 0, WardOne, Doctor),
            Command(CenterCommandKind.DefineDepartment, 0),
            Command(CenterCommandKind.RecordDoctorAffiliation, 0),
            Command(CenterCommandKind.EndDoctorAffiliation, 0)
        };
        foreach (var c in cases)
            Assert.Equal(CenterOutcome.Invalid, Do(Empty(), c).Outcome);
    }


    [Fact]
    public void SelectiveUnassignmentKeepsOtherWardAndOriginalDoctorStatus()
    {
        var first = WithAssignment();
        var two = Do(first, Command(CenterCommandKind.DefineDepartment, 3, WardTwo)).Snapshot;
        var both = Do(two, Command(CenterCommandKind.AssignDoctorToDepartment, 4, WardTwo, Doctor)).Snapshot;
        var original = Command(CenterCommandKind.UnassignDoctorFromDepartment, 5, WardOne, Doctor);
        var result = Do(both, original);
        Assert.Equal(CenterOutcome.Applied, result.Outcome);
        Assert.Equal(6L, result.Snapshot.Version);
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, result.Snapshot.Doctors[Doctor]);
        Assert.Equal(new[] { WardTwo }, result.Snapshot.DoctorDepartments[Doctor]);
        Assert.Contains(WardOne, both.DoctorDepartments[Doctor]);
        Assert.Equal(CenterOutcome.Replayed, Do(result.Snapshot, original).Outcome);
        Assert.Equal(CenterOutcome.ConflictingReplay, Do(result.Snapshot,
            original with { DepartmentRef = WardTwo }).Outcome);
        Assert.Equal(CenterOutcome.StaleRevision, Do(result.Snapshot,
            Command(CenterCommandKind.UnassignDoctorFromDepartment, 5, WardTwo, Doctor)).Outcome);
        Assert.Equal(CenterOutcome.NotApplicable, Do(result.Snapshot,
            Command(CenterCommandKind.UnassignDoctorFromDepartment, 6, WardOne, Doctor)).Outcome);
    }

    [Fact]
    public void LastUnassignmentLeavesDoctorPendingAndInvalidRequestsFailClosed()
    {
        var state = WithAssignment();
        var result = Do(state, Command(CenterCommandKind.UnassignDoctorFromDepartment,
            3, WardOne, Doctor));
        Assert.Equal(CenterOutcome.Applied, result.Outcome);
        Assert.False(result.Snapshot.DoctorDepartments.ContainsKey(Doctor));
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, result.Snapshot.Doctors[Doctor]);
        Assert.Equal(CenterOutcome.NotApplicable, Do(result.Snapshot,
            Command(CenterCommandKind.UnassignDoctorFromDepartment, 4, WardOne, Doctor)).Outcome);
        Assert.Equal(CenterOutcome.ScopeDenied, Do(result.Snapshot,
            Command(CenterCommandKind.UnassignDoctorFromDepartment, 4, WardOne, Doctor)
                with { ActingCenterRef = OtherCenter }).Outcome);
        foreach (var invalid in new[]
        {
            Command(CenterCommandKind.UnassignDoctorFromDepartment, 4, WardOne),
            Command(CenterCommandKind.UnassignDoctorFromDepartment, 4, doctor: Doctor),
            Command(CenterCommandKind.UnassignDoctorFromDepartment, 4, "real:ward", Doctor)
        })
            Assert.Equal(CenterOutcome.Invalid, Do(result.Snapshot, invalid).Outcome);
        var ended = Do(result.Snapshot, Command(CenterCommandKind.EndDoctorAffiliation, 4, doctor: Doctor)).Snapshot;
        Assert.Equal(CenterOutcome.NotApplicable, Do(ended,
            Command(CenterCommandKind.UnassignDoctorFromDepartment, 5, WardOne, Doctor)).Outcome);
    }

    [Fact]
    public void NoOperationsEverCreateActiveProfessionalGrant()
    {
        var state = WithAssignment();
        Assert.All(state.Doctors.Values,
            status => Assert.True(status is DoctorAffiliation.PendingIndependentVerification));
        Assert.DoesNotContain(state.Events,
            entry => entry.Outcome != CenterOutcome.Applied);
    }
}
