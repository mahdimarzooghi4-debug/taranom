using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticAppointmentRequestTests
{
    private static SyntheticAppointmentRequestCommand Request(
        Guid? id = null, long revision = 0) =>
        new(id ?? Guid.NewGuid(), revision,
            "test:member-one", "test:center-one", "test:department-one",
            "test:doctor-selected-by-member", "test:slot-one");

    [Fact]
    public void RequestPreservesMemberSpecifiedDoctorButDoesNotConfirmBooking()
    {
        var command = Request();
        var change = SyntheticAppointmentRequestDomain.Apply(
            SyntheticAppointmentRequestSnapshot.Empty, command);
        Assert.Equal(SyntheticAppointmentRequestOutcome.RecordedRequest, change.Outcome);
        Assert.Equal(1, change.Snapshot.Revision);
        Assert.Equal(command, change.Snapshot.History[0].Command);

        var read = SyntheticAppointmentRequestProjection.Project(change.Snapshot);
        Assert.Equal(1, read.RequestCount);
        Assert.Equal("test:doctor-selected-by-member",
            read.Requests[0].Command.RequestedDoctorRef);
        Assert.False(read.MemberAuthenticated);
        Assert.False(read.CenterAuthenticated);
        Assert.False(read.RequestedDoctorLicensed);
        Assert.False(read.SlotAvailabilityVerified);
        Assert.False(read.BookingConfirmed);
        Assert.False(read.ClinicalActionAllowed);
        Assert.DoesNotContain("Confirmed", Enum.GetNames<SyntheticAppointmentRequestOutcome>());
    }

    [Fact]
    public void ExactRetryIsIdempotentAndChangedPayloadConflicts()
    {
        var first = SyntheticAppointmentRequestDomain.Apply(
            SyntheticAppointmentRequestSnapshot.Empty, Request());
        var command = first.Snapshot.History[0].Command;
        var replay = SyntheticAppointmentRequestDomain.Apply(first.Snapshot, command);
        Assert.Equal(SyntheticAppointmentRequestOutcome.Replayed, replay.Outcome);
        Assert.Same(first.Snapshot, replay.Snapshot);

        var changed = SyntheticAppointmentRequestDomain.Apply(first.Snapshot,
            command with { RequestedDoctorRef = "test:other-doctor" });
        Assert.Equal(SyntheticAppointmentRequestOutcome.ConflictingReplay, changed.Outcome);
        Assert.Same(first.Snapshot, changed.Snapshot);
    }

    [Fact]
    public void StaleRevisionDoesNotAppendAndDistinctRequestsAdvanceRevision()
    {
        var first = SyntheticAppointmentRequestDomain.Apply(
            SyntheticAppointmentRequestSnapshot.Empty, Request());
        var stale = SyntheticAppointmentRequestDomain.Apply(
            first.Snapshot, Request(revision: 0));
        Assert.Equal(SyntheticAppointmentRequestOutcome.StaleRevision, stale.Outcome);
        Assert.Same(first.Snapshot, stale.Snapshot);

        var next = SyntheticAppointmentRequestDomain.Apply(
            first.Snapshot, Request(revision: 1));
        Assert.Equal(SyntheticAppointmentRequestOutcome.RecordedRequest, next.Outcome);
        Assert.Equal(2, next.Snapshot.Revision);
        Assert.Equal(2, SyntheticAppointmentRequestProjection.Project(next.Snapshot).RequestCount);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("center")]
    [InlineData("department")]
    [InlineData("doctor")]
    [InlineData("slot")]
    [InlineData("empty-request-id")]
    [InlineData("negative-revision")]
    [InlineData("overflow-revision")]
    public void InvalidAndNonSyntheticReferencesAreRejected(string mutation)
    {
        var command = Request();
        command = mutation switch
        {
            "member" => command with { MemberRef = "real:member" },
            "center" => command with { CenterRef = "real:center" },
            "department" => command with { DepartmentRef = "real:department" },
            "doctor" => command with { RequestedDoctorRef = "real:doctor" },
            "slot" => command with { SlotRef = "real:slot" },
            "empty-request-id" => command with { RequestId = Guid.Empty },
            "negative-revision" => command with { ExpectedRevision = -1 },
            "overflow-revision" => command with { ExpectedRevision = long.MaxValue },
            _ => throw new ArgumentException()
        };
        var result = SyntheticAppointmentRequestDomain.Apply(
            SyntheticAppointmentRequestSnapshot.Empty, command);
        Assert.Equal(SyntheticAppointmentRequestOutcome.Invalid, result.Outcome);
        Assert.Empty(result.Snapshot.History);
    }

    [Fact]
    public void DomainContainsNoDiagnosisOrPrescriptionPayload()
    {
        var commandProperties = typeof(SyntheticAppointmentRequestCommand)
            .GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("Diagnosis", commandProperties);
        Assert.DoesNotContain("Prescription", commandProperties);
        Assert.DoesNotContain("ClinicalNote", commandProperties);
    }
}
