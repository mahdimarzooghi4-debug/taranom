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


public sealed class AppointmentScheduleReadServiceTests
{
    private static AppointmentScheduleQuery Query() =>
        new("center:future-provider-01", null, "doctor:patient-selected-01",
            DateTimeOffset.Parse("2026-10-28T00:00:00Z"),
            DateTimeOffset.Parse("2026-10-29T00:00:00Z"));

    private static AppointmentScheduleObservation Observation(
        AppointmentScheduleQuery? query = null,
        IReadOnlyList<AppointmentScheduleSlotClaim>? slots = null)
    {
        var selected = query ?? Query();
        return new("source:future-provider", "revision-7",
            selected.CenterRef, selected.DepartmentRef, selected.RequestedDoctorRef,
            DateTimeOffset.Parse("2026-10-27T09:00:00Z"),
            slots ?? Array.Empty<AppointmentScheduleSlotClaim>());
    }

    [Fact]
    public async Task MissingSourceReturnsNoSlotsAndNoAuthority()
    {
        var result = await new AppointmentScheduleReadService(null).ReadAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(AppointmentScheduleReadOutcome.SourceNotConfigured, result.Outcome);
        Assert.Empty(result.SourceClaimedSlots);
        Assert.Null(result.UnverifiedSourceMetadata);
        Assert.False(result.SourceAuthenticated);
        Assert.False(result.SlotAvailabilityVerified);
        Assert.False(result.BookingConfirmed);
        Assert.False(result.ClinicalActionAllowed);
    }

    [Fact]
    public async Task ProviderClaimIsScopeBoundButNeverPromotedToVerifiedAvailability()
    {
        var query = Query();
        var starts = DateTimeOffset.Parse("2026-10-28T10:00:00Z");
        var claim = new AppointmentScheduleSlotClaim(
            "slot:provider-123", starts, starts.AddMinutes(30));
        var result = await new AppointmentScheduleReadService(
            new FixedScheduleSource(Observation(query, [claim])))
            .ReadAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(AppointmentScheduleReadOutcome.SourceClaimReceived, result.Outcome);
        Assert.Equal(claim, Assert.Single(result.SourceClaimedSlots));
        Assert.Equal(new AppointmentScheduleSourceMetadata(
            "source:future-provider", "revision-7",
            DateTimeOffset.Parse("2026-10-27T09:00:00Z")),
            result.UnverifiedSourceMetadata);
        Assert.False(result.SourceAuthenticated);
        Assert.False(result.SlotAvailabilityVerified);
        Assert.False(result.BookingConfirmed);
        Assert.False(result.ClinicalActionAllowed);
    }

    [Fact]
    public async Task MismatchedCenterOrPatientSelectedDoctorIsRejectedWithoutSlots()
    {
        var query = Query();
        var starts = DateTimeOffset.Parse("2026-10-28T10:00:00Z");
        var claim = new AppointmentScheduleSlotClaim(
            "slot:provider-123", starts, starts.AddMinutes(30));
        var wrongScope = Observation(query with { RequestedDoctorRef = "doctor:other" }, [claim]);

        var result = await new AppointmentScheduleReadService(
            new FixedScheduleSource(wrongScope)).ReadAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(AppointmentScheduleReadOutcome.InvalidSourceResponse, result.Outcome);
        Assert.Empty(result.SourceClaimedSlots);
        Assert.False(result.SlotAvailabilityVerified);
    }

    [Fact]
    public async Task InvalidIntervalsDuplicateSlotRefsAndUnorderedClaimsAreRejected()
    {
        var query = Query();
        var first = DateTimeOffset.Parse("2026-10-28T10:00:00Z");
        var later = first.AddHours(1);
        var cases = new[]
        {
            new[] { new AppointmentScheduleSlotClaim("slot:bad", first, first) },
            new[]
            {
                new AppointmentScheduleSlotClaim("slot:dup", first, first.AddMinutes(30)),
                new AppointmentScheduleSlotClaim("slot:dup", later, later.AddMinutes(30))
            },
            new[]
            {
                new AppointmentScheduleSlotClaim("slot:later", later, later.AddMinutes(30)),
                new AppointmentScheduleSlotClaim("slot:earlier", first, first.AddMinutes(30))
            }
        };

        foreach (var slots in cases)
        {
            var result = await new AppointmentScheduleReadService(
                new FixedScheduleSource(Observation(query, slots))).ReadAsync(query, TestContext.Current.CancellationToken);
            Assert.Equal(AppointmentScheduleReadOutcome.InvalidSourceResponse, result.Outcome);
            Assert.Empty(result.SourceClaimedSlots);
        }
    }

    [Fact]
    public async Task SourceFailureReturnsUnavailableWithoutLeakingAnAvailabilityClaim()
    {
        var result = await new AppointmentScheduleReadService(new FailingScheduleSource())
            .ReadAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(AppointmentScheduleReadOutcome.SourceUnavailable, result.Outcome);
        Assert.Empty(result.SourceClaimedSlots);
        Assert.False(result.SourceAuthenticated);
        Assert.False(result.SlotAvailabilityVerified);
        Assert.False(result.BookingConfirmed);
    }


    [Fact]
    public async Task QueryWindowMustBeValidAndSourceClaimsMustStayInsideIt()
    {
        var query = Query();
        var invalidQuery = await new AppointmentScheduleReadService(
            new FixedScheduleSource(Observation(query))).ReadAsync(
                query with { EndsAt = query.StartsAt },
                TestContext.Current.CancellationToken);
        Assert.Equal(AppointmentScheduleReadOutcome.InvalidQuery, invalidQuery.Outcome);
        Assert.Empty(invalidQuery.SourceClaimedSlots);

        var outsideWindow = new AppointmentScheduleSlotClaim(
            "slot:outside", query.StartsAt.AddMinutes(-1), query.StartsAt.AddMinutes(10));
        var invalidClaim = await new AppointmentScheduleReadService(
            new FixedScheduleSource(Observation(query, [outsideWindow])))
            .ReadAsync(query, TestContext.Current.CancellationToken);
        Assert.Equal(AppointmentScheduleReadOutcome.InvalidSourceResponse, invalidClaim.Outcome);
        Assert.Empty(invalidClaim.SourceClaimedSlots);
    }


    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfBecomingSourceUnavailable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AppointmentScheduleReadService(new CancellationAwareScheduleSource())
                .ReadAsync(Query(), cancellation.Token).AsTask());
    }

    private sealed class FixedScheduleSource(
        AppointmentScheduleObservation? observation) : IAppointmentScheduleSource
    {
        public ValueTask<AppointmentScheduleObservation?> ReadAsync(
            AppointmentScheduleQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(observation);
    }

    private sealed class CancellationAwareScheduleSource : IAppointmentScheduleSource
    {
        public ValueTask<AppointmentScheduleObservation?> ReadAsync(
            AppointmentScheduleQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<AppointmentScheduleObservation?>(Observation(query));
        }
    }

    private sealed class FailingScheduleSource : IAppointmentScheduleSource
    {
        public ValueTask<AppointmentScheduleObservation?> ReadAsync(
            AppointmentScheduleQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromException<AppointmentScheduleObservation?>(
                new IOException("synthetic source failure"));
    }
}
