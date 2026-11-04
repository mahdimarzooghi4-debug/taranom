using System.Text.Json;
using Npgsql;
using Taranom.AppointmentScheduling;

namespace Taranom.AppointmentScheduling.Postgres;

public sealed record AppointmentPersistenceChange(
    AppointmentCommandOutcome Outcome,
    AppointmentScheduleSnapshot Snapshot,
    AppointmentSlot? Slot,
    AppointmentBooking? Booking);

/// <summary>
/// PostgreSQL persistence for center-managed appointment schedules. The store
/// serializes each center's immutable state under a row lock and relies on the
/// domain revision and idempotency checks before committing a new snapshot.
/// </summary>
public sealed class PostgresAppointmentScheduleStore(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS taranom_appointment_schedule (
                center_ref text PRIMARY KEY,
                revision bigint NOT NULL CHECK (revision >= 0),
                snapshot jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AppointmentScheduleSnapshot?> ReadAsync(
        string centerRef,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT revision, snapshot FROM taranom_appointment_schedule WHERE center_ref = @center_ref",
            connection);
        command.Parameters.AddWithValue("center_ref", centerRef);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var revision = reader.GetInt64(0);
        var document = await reader.GetFieldValueAsync<string>(1, cancellationToken);
        return Restore(centerRef, revision, document);
    }

    public async Task<AppointmentPersistenceChange> ApplyAsync(
        string centerRef,
        Func<AppointmentScheduleSnapshot, AppointmentScheduleChange> apply,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var empty = AppointmentScheduleSnapshot.Create(centerRef);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var create = new NpgsqlCommand("""
            INSERT INTO taranom_appointment_schedule (center_ref, revision, snapshot)
            VALUES (@center_ref, 0, @snapshot::jsonb)
            ON CONFLICT (center_ref) DO NOTHING
            """, connection, transaction))
        {
            create.Parameters.AddWithValue("center_ref", centerRef);
            create.Parameters.AddWithValue("snapshot", Serialize(empty));
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        long currentRevision;
        AppointmentScheduleSnapshot current;
        await using (var read = new NpgsqlCommand("""
            SELECT revision, snapshot
            FROM taranom_appointment_schedule
            WHERE center_ref = @center_ref
            FOR UPDATE
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("center_ref", centerRef);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("APPOINTMENT_SCHEDULE_ROW_MISSING");
            currentRevision = reader.GetInt64(0);
            current = Restore(centerRef, currentRevision,
                await reader.GetFieldValueAsync<string>(1, cancellationToken));
        }

        var change = apply(current);
        if (!ReferenceEquals(change.Snapshot, current) &&
            change.Outcome is AppointmentCommandOutcome.Applied)
        {
            if (change.Snapshot.Revision != checked(currentRevision + 1))
                throw new InvalidOperationException("APPOINTMENT_SCHEDULE_REVISION_INVALID");
            await using var update = new NpgsqlCommand("""
                UPDATE taranom_appointment_schedule
                SET revision = @revision, snapshot = @snapshot::jsonb, updated_at = now()
                WHERE center_ref = @center_ref AND revision = @expected_revision
                """, connection, transaction);
            update.Parameters.AddWithValue("center_ref", centerRef);
            update.Parameters.AddWithValue("expected_revision", currentRevision);
            update.Parameters.AddWithValue("revision", change.Snapshot.Revision);
            update.Parameters.AddWithValue("snapshot", Serialize(change.Snapshot));
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("APPOINTMENT_SCHEDULE_REVISION_CONFLICT");
        }

        await transaction.CommitAsync(cancellationToken);
        return new(change.Outcome, change.Snapshot, change.Slot, change.Booking);
    }

    private static string Serialize(AppointmentScheduleSnapshot snapshot) =>
        JsonSerializer.Serialize(new SnapshotDocument(
            snapshot.Slots.Values.ToArray(),
            snapshot.Bookings.Values.ToArray(),
            snapshot.Receipts.Values.ToArray()), JsonOptions);

    private static AppointmentScheduleSnapshot Restore(
        string centerRef,
        long revision,
        string json)
    {
        var document = JsonSerializer.Deserialize<SnapshotDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("APPOINTMENT_SCHEDULE_SNAPSHOT_INVALID");
        return AppointmentScheduleSnapshot.Restore(centerRef, revision,
            document.Slots, document.Bookings, document.Receipts);
    }

    private sealed record SnapshotDocument(
        AppointmentSlot[] Slots,
        AppointmentBooking[] Bookings,
        AppointmentCommandReceipt[] Receipts);
}
