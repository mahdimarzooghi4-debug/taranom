using Npgsql;
using Taranom.AppointmentScheduling;
using Taranom.AppointmentScheduling.Postgres;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class AppointmentPostgresStoreTests
{
    private static readonly AppointmentAuditActor TestActor = new(
        "https://appointments.test.invalid/realms/taranom", "postgres-test-subject");
    [Fact]
    public async Task ScheduleAndMemberCancellationSurviveDatabaseReopen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
            ?? throw new InvalidOperationException("TARANOM_TEST_PG is required for PostgreSQL integration tests.");
        var centerRef = "integration-" + Guid.NewGuid().ToString("N");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var store = new PostgresAppointmentScheduleStore(dataSource);
        await store.InitializeAsync(cancellationToken);

        var startsAt = DateTimeOffset.UtcNow.AddDays(2);
        var slot = await store.ApplyAsync(centerRef, TestActor, snapshot => snapshot.DefineSlot(
            Guid.NewGuid(), snapshot.Revision, centerRef, "morning-1", "doctor-opaque-1",
            startsAt, startsAt.AddMinutes(30)), cancellationToken);
        Assert.Equal(AppointmentCommandOutcome.Applied, slot.Outcome);
        Assert.Equal(1, slot.Snapshot.Revision);

        var requestId = Guid.NewGuid();
        var booking = await store.ApplyAsync(centerRef, TestActor, snapshot => snapshot.Book(
            requestId, snapshot.Revision, "member-opaque-1", "morning-1"), cancellationToken);
        Assert.Equal(AppointmentCommandOutcome.Applied, booking.Outcome);
        Assert.NotNull(booking.Booking);
        var bookingRef = booking.Booking!.BookingRef;

        // Use a second data source to ensure state is reconstructed from PostgreSQL,
        // rather than returned from an in-memory snapshot cache.
        await using var reopenedDataSource = NpgsqlDataSource.Create(connectionString);
        var reopenedStore = new PostgresAppointmentScheduleStore(reopenedDataSource);
        var reopened = await reopenedStore.ReadAsync(centerRef, cancellationToken);
        Assert.NotNull(reopened);
        Assert.Equal(2, reopened.Revision);
        Assert.Equal(AppointmentSlotState.Booked, reopened.Slots["morning-1"].State);
        Assert.Equal("member-opaque-1", reopened.Bookings[bookingRef].MemberRef);

        var exactReplay = await reopenedStore.ApplyAsync(centerRef, TestActor, snapshot => snapshot.Book(
            requestId, 1, "member-opaque-1", "morning-1"), cancellationToken);
        Assert.Equal(AppointmentCommandOutcome.Replayed, exactReplay.Outcome);

        var changedRevisionReplay = await reopenedStore.ApplyAsync(centerRef, TestActor, snapshot => snapshot.Book(
            requestId, 2, "member-opaque-1", "morning-1"), cancellationToken);
        Assert.Equal(AppointmentCommandOutcome.ConflictingReplay, changedRevisionReplay.Outcome);
        Assert.Equal(2, changedRevisionReplay.Snapshot.Revision);

        var deniedCancellation = await reopenedStore.ApplyAsync(centerRef, TestActor, snapshot =>
            snapshot.Cancel(Guid.NewGuid(), snapshot.Revision, "another-member", "morning-1"), cancellationToken);
        Assert.Equal(AppointmentCommandOutcome.NotOwner, deniedCancellation.Outcome);
        Assert.Equal(2, deniedCancellation.Snapshot.Revision);

        var cancelled = await reopenedStore.ApplyAsync(centerRef, TestActor, snapshot =>
            snapshot.Cancel(Guid.NewGuid(), snapshot.Revision, "member-opaque-1", "morning-1"), cancellationToken);
        Assert.Equal(AppointmentCommandOutcome.Applied, cancelled.Outcome);
        Assert.Equal(3, cancelled.Snapshot.Revision);

        var final = await store.ReadAsync(centerRef, cancellationToken);
        Assert.NotNull(final);
        Assert.Equal(AppointmentSlotState.Open, final.Slots["morning-1"].State);
        Assert.Equal(AppointmentBookingState.CancelledByMember,
            final.Bookings[bookingRef].State);
    }

    [Fact]
    public async Task ScheduleMutationRollsBackWhenAuditAppendFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
            ?? throw new InvalidOperationException("TARANOM_TEST_PG is required for PostgreSQL integration tests.");
        var centerRef = "integration-audit-rollback-" + Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var store = new PostgresAppointmentScheduleStore(dataSource);
        await store.InitializeAsync(cancellationToken);

        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand("""
            INSERT INTO taranom_appointment_audit_event
                (event_id, request_id, center_ref, actor_issuer, actor_subject,
                 action, target_ref, command_fingerprint, schedule_revision)
            VALUES
                (@event_id, @request_id, @center_ref, @issuer, @subject,
                 'define-slot', 'already-used', @fingerprint, 1)
            """, connection))
        {
            command.Parameters.AddWithValue("event_id", Guid.NewGuid());
            command.Parameters.AddWithValue("request_id", requestId);
            command.Parameters.AddWithValue("center_ref", centerRef);
            command.Parameters.AddWithValue("issuer", TestActor.Issuer);
            command.Parameters.AddWithValue("subject", TestActor.Subject);
            command.Parameters.AddWithValue("fingerprint", new string('0', 64));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var startsAt = DateTimeOffset.UtcNow.AddDays(2);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            store.ApplyAsync(centerRef, TestActor, snapshot => snapshot.DefineSlot(
                requestId, 0, centerRef, "rollback-slot", "doctor-rollback",
                startsAt, startsAt.AddMinutes(30)), cancellationToken));
        Assert.Equal("23505", failure.SqlState);

        Assert.Null(await store.ReadAsync(centerRef, cancellationToken));
    }

    [Fact]
    public async Task CenterSchedulesRemainIsolatedAndStaleWritesDoNotCommit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
            ?? throw new InvalidOperationException("TARANOM_TEST_PG is required for PostgreSQL integration tests.");
        var suffix = Guid.NewGuid().ToString("N");
        var centerA = "integration-a-" + suffix;
        var centerB = "integration-b-" + suffix;
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var store = new PostgresAppointmentScheduleStore(dataSource);
        await store.InitializeAsync(cancellationToken);

        var startsAt = DateTimeOffset.UtcNow.AddDays(3);
        var first = await store.ApplyAsync(centerA, TestActor, snapshot => snapshot.DefineSlot(
            Guid.NewGuid(), 0, centerA, "slot-a", "doctor-a", startsAt, startsAt.AddMinutes(30)), cancellationToken);
        var stale = await store.ApplyAsync(centerA, TestActor, snapshot => snapshot.DefineSlot(
            Guid.NewGuid(), 0, centerA, "slot-stale", "doctor-a", startsAt.AddHours(1), startsAt.AddHours(1.5)), cancellationToken);
        Assert.Equal(AppointmentCommandOutcome.Applied, first.Outcome);
        Assert.Equal(AppointmentCommandOutcome.StaleRevision, stale.Outcome);

        var other = await store.ReadAsync(centerB, cancellationToken);
        Assert.Null(other);
        var persisted = await store.ReadAsync(centerA, cancellationToken);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.Revision);
        Assert.Single(persisted.Slots);
        Assert.DoesNotContain("slot-stale", persisted.Slots.Keys);
    }

    [Fact]
    public async Task ConcurrentCommandsAtTheSameRevisionCommitOnlyOneScheduleChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
            ?? throw new InvalidOperationException("TARANOM_TEST_PG is required for PostgreSQL integration tests.");
        var centerRef = "integration-race-" + Guid.NewGuid().ToString("N");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var store = new PostgresAppointmentScheduleStore(dataSource);
        await store.InitializeAsync(cancellationToken);
        var startsAt = DateTimeOffset.UtcNow.AddDays(4);

        async Task<AppointmentPersistenceChange> Define(string slotRef) =>
            await store.ApplyAsync(centerRef, TestActor, snapshot => snapshot.DefineSlot(
                Guid.NewGuid(), 0, centerRef, slotRef, "doctor-race",
                startsAt, startsAt.AddMinutes(30)), cancellationToken);

        var results = await Task.WhenAll(Define("race-a"), Define("race-b"));
        Assert.Single(results, item => item.Outcome == AppointmentCommandOutcome.Applied);
        Assert.Single(results, item => item.Outcome == AppointmentCommandOutcome.StaleRevision);

        var persisted = await store.ReadAsync(centerRef, cancellationToken);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.Revision);
        Assert.Single(persisted.Slots);
    }
}
