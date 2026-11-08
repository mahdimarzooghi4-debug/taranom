using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Taranom.AppointmentScheduling;

public enum AppointmentSlotState
{
    Open = 1,
    Booked = 2,
    Withdrawn = 3
}

public enum AppointmentBookingState
{
    Booked = 1,
    CancelledByMember = 2
}

public enum AppointmentCommandOutcome
{
    Applied = 1,
    Replayed = 2,
    Invalid = 3,
    ScopeDenied = 4,
    StaleRevision = 5,
    SlotUnavailable = 6,
    NotOwner = 7,
    NotFound = 8,
    ConflictingReplay = 9
}

public sealed record AppointmentSlot(
    string SlotRef,
    string CenterRef,
    string DoctorRef,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AppointmentSlotState State,
    Guid? BookingRef,
    long Revision);

public sealed record AppointmentBooking(
    Guid BookingRef,
    string MemberRef,
    string SlotRef,
    string CenterRef,
    string DoctorRef,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AppointmentBookingState State,
    long Revision);

public sealed record AppointmentCommandReceipt(
    Guid RequestId,
    string ActorRef,
    string TargetRef,
    string Action,
    AppointmentCommandOutcome Outcome,
    long ResultRevision,
    Guid? AppointmentRef,
    string? CommandFingerprint = null);

public sealed class AppointmentScheduleSnapshot
{
    private readonly ImmutableDictionary<string, AppointmentSlot> _slots;
    private readonly ImmutableDictionary<Guid, AppointmentBooking> _bookings;
    private readonly ImmutableDictionary<Guid, AppointmentCommandReceipt> _receipts;

    private AppointmentScheduleSnapshot(
        string centerRef,
        long revision,
        ImmutableDictionary<string, AppointmentSlot> slots,
        ImmutableDictionary<Guid, AppointmentBooking> bookings,
        ImmutableDictionary<Guid, AppointmentCommandReceipt> receipts)
    {
        CenterRef = centerRef;
        Revision = revision;
        _slots = slots;
        _bookings = bookings;
        _receipts = receipts;
    }

    public string CenterRef { get; }
    public long Revision { get; }
    public IReadOnlyDictionary<string, AppointmentSlot> Slots => _slots;
    public IReadOnlyDictionary<Guid, AppointmentBooking> Bookings => _bookings;
    public IReadOnlyDictionary<Guid, AppointmentCommandReceipt> Receipts => _receipts;

    public static AppointmentScheduleSnapshot Create(string centerRef)
    {
        if (!ValidRef(centerRef))
            throw new ArgumentException("A valid center reference is required.", nameof(centerRef));

        return new AppointmentScheduleSnapshot(centerRef, 0,
            ImmutableDictionary<string, AppointmentSlot>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableDictionary<Guid, AppointmentBooking>.Empty,
            ImmutableDictionary<Guid, AppointmentCommandReceipt>.Empty);
    }

    public static AppointmentScheduleSnapshot Restore(
        string centerRef,
        long revision,
        IEnumerable<AppointmentSlot> slots,
        IEnumerable<AppointmentBooking> bookings,
        IEnumerable<AppointmentCommandReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(receipts);
        if (!ValidRef(centerRef) || revision < 0)
            throw new ArgumentException("Invalid appointment schedule snapshot.");

        var slotMap = slots.ToImmutableDictionary(item => item.SlotRef, StringComparer.Ordinal);
        var bookingMap = bookings.ToImmutableDictionary(item => item.BookingRef);
        var receiptMap = receipts.ToImmutableDictionary(item => item.RequestId);
        if (slotMap.Values.Any(item => item.CenterRef != centerRef || item.Revision > revision) ||
            bookingMap.Values.Any(item => item.CenterRef != centerRef || item.Revision > revision) ||
            receiptMap.Values.Any(item => item.ResultRevision > revision))
            throw new ArgumentException("Appointment schedule snapshot does not match its center or revision.");

        return new AppointmentScheduleSnapshot(centerRef, revision, slotMap, bookingMap, receiptMap);
    }

    public AppointmentScheduleChange DefineSlot(
        Guid requestId,
        long expectedRevision,
        string actingCenterRef,
        string slotRef,
        string doctorRef,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        if (requestId == Guid.Empty || expectedRevision < 0 ||
            !ValidRef(actingCenterRef) || !ValidRef(slotRef) || !ValidRef(doctorRef) ||
            startsAt == default || endsAt <= startsAt)
            return Denied(AppointmentCommandOutcome.Invalid);

        var fingerprint = Fingerprint(
            actingCenterRef, slotRef, doctorRef,
            expectedRevision.ToString(CultureInfo.InvariantCulture),
            startsAt.ToString("O", CultureInfo.InvariantCulture),
            endsAt.ToString("O", CultureInfo.InvariantCulture));
        if (TryReplay(requestId, actingCenterRef, slotRef, "define-slot",
                fingerprint, out var replay))
            return replay;
        if (!string.Equals(actingCenterRef, CenterRef, StringComparison.Ordinal))
            return Denied(AppointmentCommandOutcome.ScopeDenied);
        if (expectedRevision != Revision)
            return Denied(AppointmentCommandOutcome.StaleRevision);
        if (_slots.ContainsKey(slotRef) || _slots.Values.Any(slot =>
                slot.DoctorRef == doctorRef && slot.State != AppointmentSlotState.Withdrawn &&
                startsAt < slot.EndsAt && slot.StartsAt < endsAt))
            return Denied(AppointmentCommandOutcome.SlotUnavailable);
        if (Revision == long.MaxValue)
            return Denied(AppointmentCommandOutcome.Invalid);

        var nextRevision = Revision + 1;
        var slot = new AppointmentSlot(slotRef, CenterRef, doctorRef, startsAt, endsAt,
            AppointmentSlotState.Open, null, nextRevision);
        var next = Copy(nextRevision, _slots.Add(slotRef, slot), _bookings,
            AddReceipt(requestId, actingCenterRef, slotRef, "define-slot",
                AppointmentCommandOutcome.Applied, nextRevision, fingerprint));
        return new(AppointmentCommandOutcome.Applied, next, slot, null);
    }

    public AppointmentScheduleChange Book(
        Guid requestId,
        long expectedRevision,
        string memberRef,
        string slotRef)
    {
        if (requestId == Guid.Empty || expectedRevision < 0 ||
            !ValidRef(memberRef) || !ValidRef(slotRef))
            return Denied(AppointmentCommandOutcome.Invalid);

        var fingerprint = Fingerprint(
            CenterRef, memberRef, slotRef,
            expectedRevision.ToString(CultureInfo.InvariantCulture));
        if (TryReplay(requestId, memberRef, slotRef, "book",
                fingerprint, out var replay))
            return replay;
        if (!_slots.TryGetValue(slotRef, out var slot))
            return Denied(AppointmentCommandOutcome.NotFound);
        if (expectedRevision != Revision)
            return Denied(AppointmentCommandOutcome.StaleRevision);
        if (slot.State != AppointmentSlotState.Open)
            return Denied(AppointmentCommandOutcome.SlotUnavailable);
        if (Revision == long.MaxValue)
            return Denied(AppointmentCommandOutcome.Invalid);

        var nextRevision = Revision + 1;
        var booked = slot with
        {
            State = AppointmentSlotState.Booked,
            BookingRef = requestId,
            Revision = nextRevision
        };
        var booking = new AppointmentBooking(requestId, memberRef, slotRef, CenterRef,
            slot.DoctorRef, slot.StartsAt, slot.EndsAt,
            AppointmentBookingState.Booked, nextRevision);
        var next = Copy(nextRevision, _slots.SetItem(slotRef, booked),
            _bookings.Add(requestId, booking),
            AddReceipt(requestId, memberRef, slotRef, "book",
                AppointmentCommandOutcome.Applied, nextRevision, fingerprint, requestId));
        return new(AppointmentCommandOutcome.Applied, next, booked, booking);
    }

    public AppointmentScheduleChange Cancel(
        Guid requestId,
        long expectedRevision,
        string memberRef,
        string slotRef)
    {
        if (requestId == Guid.Empty || expectedRevision < 0 ||
            !ValidRef(memberRef) || !ValidRef(slotRef))
            return Denied(AppointmentCommandOutcome.Invalid);

        var fingerprint = Fingerprint(
            CenterRef, memberRef, slotRef,
            expectedRevision.ToString(CultureInfo.InvariantCulture));
        if (TryReplay(requestId, memberRef, slotRef, "cancel",
                fingerprint, out var replay))
            return replay;
        if (!_slots.TryGetValue(slotRef, out var slot))
            return Denied(AppointmentCommandOutcome.NotFound);
        if (slot.BookingRef is not Guid bookingRef ||
            !_bookings.TryGetValue(bookingRef, out var booking))
            return Denied(AppointmentCommandOutcome.NotFound);
        if (!string.Equals(booking.MemberRef, memberRef, StringComparison.Ordinal))
            return Denied(AppointmentCommandOutcome.NotOwner);
        if (expectedRevision != Revision)
            return Denied(AppointmentCommandOutcome.StaleRevision);
        if (slot.State != AppointmentSlotState.Booked)
            return Denied(AppointmentCommandOutcome.SlotUnavailable);
        if (Revision == long.MaxValue)
            return Denied(AppointmentCommandOutcome.Invalid);

        var nextRevision = Revision + 1;
        var reopened = slot with
        {
            State = AppointmentSlotState.Open,
            BookingRef = null,
            Revision = nextRevision
        };
        var cancelled = booking with
        {
            State = AppointmentBookingState.CancelledByMember,
            Revision = nextRevision
        };
        var next = Copy(nextRevision, _slots.SetItem(slotRef, reopened),
            _bookings.SetItem(bookingRef, cancelled),
            AddReceipt(requestId, memberRef, slotRef, "cancel",
                AppointmentCommandOutcome.Applied, nextRevision, fingerprint, bookingRef));
        return new(AppointmentCommandOutcome.Applied, next, reopened, cancelled);
    }

    private AppointmentScheduleChange Denied(AppointmentCommandOutcome outcome) =>
        new(outcome, this, null, null);

    private AppointmentScheduleSnapshot Copy(
        long revision,
        ImmutableDictionary<string, AppointmentSlot> slots,
        ImmutableDictionary<Guid, AppointmentBooking> bookings,
        ImmutableDictionary<Guid, AppointmentCommandReceipt> receipts) =>
        new(CenterRef, revision, slots, bookings, receipts);

    private ImmutableDictionary<Guid, AppointmentCommandReceipt> AddReceipt(
        Guid requestId,
        string actorRef,
        string targetRef,
        string action,
        AppointmentCommandOutcome outcome,
        long revision,
        string commandFingerprint,
        Guid? appointmentRef = null) =>
        _receipts.Add(requestId,
            new(requestId, actorRef, targetRef, action, outcome, revision,
                appointmentRef, commandFingerprint));

    private bool TryReplay(
        Guid requestId,
        string actorRef,
        string targetRef,
        string action,
        string commandFingerprint,
        out AppointmentScheduleChange change)
    {
        if (!_receipts.TryGetValue(requestId, out var prior))
        {
            change = null!;
            return false;
        }

        var exact = string.Equals(prior.ActorRef, actorRef, StringComparison.Ordinal) &&
            string.Equals(prior.TargetRef, targetRef, StringComparison.Ordinal) &&
            string.Equals(prior.Action, action, StringComparison.Ordinal) &&
            prior.CommandFingerprint is not null &&
            string.Equals(prior.CommandFingerprint, commandFingerprint, StringComparison.Ordinal);
        var bookingRef = prior.AppointmentRef;
        change = exact
            ? new(AppointmentCommandOutcome.Replayed, this,
                _slots.TryGetValue(targetRef, out var slot) ? slot : null,
                bookingRef is Guid value && _bookings.TryGetValue(value, out var booking)
                    ? booking
                    : null)
            : Denied(AppointmentCommandOutcome.ConflictingReplay);
        return true;
    }

    private static string Fingerprint(params string[] values)
    {
        var canonical = new StringBuilder();
        foreach (var value in values)
            canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool ValidRef(string? value) =>
        value is { Length: > 0 and <= 128 } &&
        !string.IsNullOrWhiteSpace(value) &&
        !value.Any(char.IsControl);
}

public sealed record AppointmentScheduleChange(
    AppointmentCommandOutcome Outcome,
    AppointmentScheduleSnapshot Snapshot,
    AppointmentSlot? Slot,
    AppointmentBooking? Booking);
