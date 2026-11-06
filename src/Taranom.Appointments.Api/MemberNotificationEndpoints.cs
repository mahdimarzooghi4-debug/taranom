using System.Data;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Taranom.Appointments.Api;

public sealed record MemberNotificationView(Guid NotificationRef, Guid SourceRequestId,
    string Kind, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

internal static class MemberNotificationEndpoints
{
    internal static void MapMemberNotificationEndpoints(this WebApplication app, string policy)
    {
        app.MapGet("/api/v1/me/notifications", async (long? before, Guid? notificationRef,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            if (before is <= 0 || notificationRef == Guid.Empty) return Results.BadRequest();
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            long unread;
            await using (var count = new NpgsqlCommand("""
                SELECT count(*) FROM taranom_member_support_notification n
                JOIN taranom_member_support_request s ON s.request_ref=n.request_ref
                WHERE s.issuer=@issuer AND s.subject=@subject AND n.read_at IS NULL
                """, connection, transaction))
            {
                Bind(count, owner.Value);
                unread = (long)(await count.ExecuteScalarAsync(ct))!;
            }
            await using var command = new NpgsqlCommand("""
                SELECT n.sequence_no,n.request_ref,s.request_id,n.created_at,n.read_at
                FROM taranom_member_support_notification n
                JOIN taranom_member_support_request s ON s.request_ref=n.request_ref
                WHERE s.issuer=@issuer AND s.subject=@subject
                  AND (@before IS NULL OR n.sequence_no<@before)
                  AND (@ref IS NULL OR n.request_ref=@ref)
                ORDER BY n.sequence_no DESC LIMIT 21
                """, connection, transaction);
            Bind(command, owner.Value);
            command.Parameters.AddWithValue("before", NpgsqlDbType.Bigint, (object?)before ?? DBNull.Value);
            command.Parameters.AddWithValue("ref", NpgsqlDbType.Uuid, (object?)notificationRef ?? DBNull.Value);
            var items = new List<MemberNotificationView>();
            long last = 0;
            var more = false;
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                {
                    if (items.Count == 20) { more = true; break; }
                    last = reader.GetInt64(0);
                    items.Add(new MemberNotificationView(reader.GetGuid(1), reader.GetGuid(2), "SupportAnswered",
                        reader.GetFieldValue<DateTimeOffset>(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
                }
            await transaction.CommitAsync(ct);
            return Results.Ok(new { notifications = items, unreadCount = unread,
                nextBefore = more ? last.ToString(CultureInfo.InvariantCulture) : null });
        }).RequireAuthorization(policy);

        // Monotonic, naturally idempotent acknowledgement: exact retries retain the first read timestamp.
        app.MapPost("/api/v1/me/notifications/{notificationRef:guid}/read", async (Guid notificationRef,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            if (notificationRef == Guid.Empty) return Results.BadRequest();
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand("""
                UPDATE taranom_member_support_notification n SET read_at=COALESCE(n.read_at,now())
                FROM taranom_member_support_request s
                WHERE n.request_ref=s.request_ref AND n.request_ref=@ref
                  AND s.issuer=@issuer AND s.subject=@subject
                RETURNING n.read_at
                """, connection);
            Bind(command, owner.Value);
            command.Parameters.AddWithValue("ref", notificationRef);
            var timestamp = await command.ExecuteScalarAsync(ct);
            return timestamp is null ? Results.NotFound() : Results.Ok(new { notificationRef, readAt = timestamp });
        }).RequireAuthorization(policy);
    }

    private static (string Issuer, string Subject)? Owner(HttpContext context)
    {
        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ||
            issuer.Length > 512 || subject.Length > 256 || issuer.Any(char.IsControl) || subject.Any(char.IsControl)) return null;
        return (issuer, subject);
    }
    private static void Bind(NpgsqlCommand command, (string Issuer, string Subject) owner)
    {
        command.Parameters.AddWithValue("issuer", owner.Issuer);
        command.Parameters.AddWithValue("subject", owner.Subject);
    }
}
