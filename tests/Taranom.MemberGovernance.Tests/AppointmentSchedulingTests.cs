using Taranom.AppointmentScheduling;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class AppointmentSchedulingTests
{
    [Fact]
    public void CenterDefinedSlotCanBeBookedAndCancelledOnlyByItsMember()
    {
        var state = AppointmentScheduleSnapshot.Create("center-a");
        var defined = state.DefineSlot(Guid.NewGuid(), 0, "center-a", "slot-a", "doctor-a",
            DateTimeOffset.Parse("2026-12-01T09:00:00Z"),
            DateTimeOffset.Parse("2026-12-01T09:30:00Z"));
        Assert.Equal(AppointmentCommandOutcome.Applied, defined.Outcome);

        var booked = defined.Snapshot.Book(Guid.NewGuid(), 1, "member-a", "slot-a");
        Assert.Equal(AppointmentCommandOutcome.Applied, booked.Outcome);
        Assert.Equal(AppointmentSlotState.Booked, booked.Slot!.State);

        var wrongMember = booked.Snapshot.Cancel(Guid.NewGuid(), 2, "member-b", "slot-a");
        Assert.Equal(AppointmentCommandOutcome.NotOwner, wrongMember.Outcome);
        Assert.Equal(AppointmentSlotState.Booked, wrongMember.Snapshot.Slots["slot-a"].State);

        var cancelled = booked.Snapshot.Cancel(Guid.NewGuid(), 2, "member-a", "slot-a");
        Assert.Equal(AppointmentCommandOutcome.Applied, cancelled.Outcome);
        Assert.Equal(AppointmentSlotState.Open, cancelled.Slot!.State);
        Assert.Equal(AppointmentBookingState.CancelledByMember,
            cancelled.Snapshot.Bookings[booked.Booking!.BookingRef].State);
    }

    [Fact]
    public void RejectsCrossCenterSlotDefinitionAndOverlappingDoctorTimes()
    {
        var initial = AppointmentScheduleSnapshot.Create("center-a");
        var crossCenter = initial.DefineSlot(Guid.NewGuid(), 0, "center-b", "slot-a", "doctor-a",
            DateTimeOffset.Parse("2026-12-01T09:00:00Z"),
            DateTimeOffset.Parse("2026-12-01T09:30:00Z"));
        Assert.Equal(AppointmentCommandOutcome.ScopeDenied, crossCenter.Outcome);

        var first = initial.DefineSlot(Guid.NewGuid(), 0, "center-a", "slot-a", "doctor-a",
            DateTimeOffset.Parse("2026-12-01T09:00:00Z"),
            DateTimeOffset.Parse("2026-12-01T09:30:00Z"));
        var overlap = first.Snapshot.DefineSlot(Guid.NewGuid(), 1, "center-a", "slot-b", "doctor-a",
            DateTimeOffset.Parse("2026-12-01T09:15:00Z"),
            DateTimeOffset.Parse("2026-12-01T09:45:00Z"));
        Assert.Equal(AppointmentCommandOutcome.SlotUnavailable, overlap.Outcome);
    }

    [Fact]
    public void IdempotentReplayDoesNotReopenOrDuplicateBooking()
    {
        var initial = AppointmentScheduleSnapshot.Create("center-a");
        var defined = initial.DefineSlot(Guid.NewGuid(), 0, "center-a", "slot-a", "doctor-a",
            DateTimeOffset.Parse("2026-12-01T09:00:00Z"),
            DateTimeOffset.Parse("2026-12-01T09:30:00Z"));
        var requestId = Guid.NewGuid();
        var booked = defined.Snapshot.Book(requestId, 1, "member-a", "slot-a");
        var replayed = booked.Snapshot.Book(requestId, 1, "member-a", "slot-a");
        Assert.Equal(AppointmentCommandOutcome.Replayed, replayed.Outcome);
        Assert.Equal(2, replayed.Snapshot.Revision);

        var conflicting = replayed.Snapshot.Book(requestId, 2, "member-b", "slot-a");
        Assert.Equal(AppointmentCommandOutcome.ConflictingReplay, conflicting.Outcome);
        Assert.Equal("member-a", conflicting.Snapshot.Bookings[requestId].MemberRef);
    }

    [Fact]
    public void RestoredSnapshotKeepsCancelledBookingHistory()
    {
        var initial = AppointmentScheduleSnapshot.Create("center-a");
        var defined = initial.DefineSlot(Guid.NewGuid(), 0, "center-a", "slot-a", "doctor-a",
            DateTimeOffset.Parse("2026-12-01T09:00:00Z"),
            DateTimeOffset.Parse("2026-12-01T09:30:00Z"));
        var booked = defined.Snapshot.Book(Guid.NewGuid(), 1, "member-a", "slot-a");
        var cancelled = booked.Snapshot.Cancel(Guid.NewGuid(), 2, "member-a", "slot-a");

        var restored = AppointmentScheduleSnapshot.Restore(
            cancelled.Snapshot.CenterRef,
            cancelled.Snapshot.Revision,
            cancelled.Snapshot.Slots.Values,
            cancelled.Snapshot.Bookings.Values,
            cancelled.Snapshot.Receipts.Values);

        Assert.Equal(3, restored.Revision);
        Assert.Equal(AppointmentSlotState.Open, restored.Slots["slot-a"].State);
        Assert.Equal(AppointmentBookingState.CancelledByMember,
            restored.Bookings[booked.Booking!.BookingRef].State);
    }
}
