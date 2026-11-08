using System.Data;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Taranom.Appointments.Api;

public sealed record MemberNotificationView(Guid NotificationRef, Guid SourceRequestId,
    string Kind, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record GroupInvitationNotificationView(Guid NotificationRef, Guid SourceInvitationRef,
    string Kind, string ResponseState, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

internal static class MemberNotificationEndpoints
{
    // Read authorization always resolves against the canonical persisted
    // support request or invitation draft, never user-provided owner fields.
    private const string OwnerSource = """
        FROM taranom_member_notification_journal n
        LEFT JOIN taranom_member_support_request s
            ON n.kind='SupportAnswered' AND s.request_ref=n.source_ref
        LEFT JOIN taranom_member_group_invitation i
            ON n.kind='GroupInvitationResponded' AND i.invitation_ref=n.source_ref
        LEFT JOIN taranom_member_group_invite_draft d ON d.draft_ref=i.draft_ref
        WHERE ((n.kind='SupportAnswered' AND s.issuer=@issuer AND s.subject=@subject)
            OR (n.kind='GroupInvitationResponded' AND d.issuer=@issuer AND d.subject=@subject))
        """;

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
            await using (var count = new NpgsqlCommand("SELECT count(*) " + OwnerSource +
                " AND n.read_at IS NULL", connection, transaction))
            {
                Bind(count, owner.Value);
                unread = (long)(await count.ExecuteScalarAsync(ct))!;
            }
            await using var command = new NpgsqlCommand("""
                SELECT n.sequence_no,n.notification_ref,n.kind,s.request_id,
                       i.invitation_ref,i.state,n.created_at,n.read_at
                """ + OwnerSource + """
                  AND (@before IS NULL OR n.sequence_no<@before)
                  AND (@ref IS NULL OR n.notification_ref=@ref)
                ORDER BY n.sequence_no DESC LIMIT 21
                """, connection, transaction);
            Bind(command, owner.Value);
            command.Parameters.AddWithValue("before", NpgsqlDbType.Bigint, (object?)before ?? DBNull.Value);
            command.Parameters.AddWithValue("ref", NpgsqlDbType.Uuid, (object?)notificationRef ?? DBNull.Value);
            var items = new List<object>();
            long last = 0;
            var more = false;
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                {
                    if (items.Count == 20) { more = true; break; }
                    last = reader.GetInt64(0);
                    var id = reader.GetGuid(1);
                    var kind = reader.GetString(2);
                    var createdAt = reader.GetFieldValue<DateTimeOffset>(6);
                    var readAt = reader.IsDBNull(7) ? (DateTimeOffset?)null
                        : reader.GetFieldValue<DateTimeOffset>(7);
                    if (kind == "SupportAnswered")
                        items.Add(new MemberNotificationView(id, reader.GetGuid(3),
                            kind, createdAt, readAt));
                    else if (kind == "GroupInvitationResponded")
                    {
                        var state = reader.GetString(5);
                        if (state is not ("Accepted" or "Declined"))
                            throw new InvalidOperationException("Invalid final invitation notification source.");
                        items.Add(new GroupInvitationNotificationView(id, reader.GetGuid(4),
                            kind, state, createdAt, readAt));
                    }
                    else throw new InvalidOperationException("Unknown notification source.");
                }
            await transaction.CommitAsync(ct);
            return Results.Ok(new { notifications = items, unreadCount = unread,
                nextBefore = more ? last.ToString(CultureInfo.InvariantCulture) : null });
        }).RequireAuthorization(policy);

        // One monotonic owner-only read acknowledgment for either source kind.
        // Exact retries retain the original read_at timestamp.
        app.MapPost("/api/v1/me/notifications/{notificationRef:guid}/read", async (Guid notificationRef,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            if (notificationRef == Guid.Empty) return Results.BadRequest();
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand("""
                UPDATE taranom_member_notification_journal n
                SET read_at=COALESCE(n.read_at,now())
                WHERE n.notification_ref=@ref AND (
                    (n.kind='SupportAnswered' AND EXISTS (
                        SELECT 1 FROM taranom_member_support_request s
                        WHERE s.request_ref=n.source_ref AND s.issuer=@issuer AND s.subject=@subject))
                    OR (n.kind='GroupInvitationResponded' AND EXISTS (
                        SELECT 1 FROM taranom_member_group_invitation i
                        JOIN taranom_member_group_invite_draft d ON d.draft_ref=i.draft_ref
                        WHERE i.invitation_ref=n.source_ref AND d.issuer=@issuer AND d.subject=@subject)))
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
            issuer.Length > 512 || subject.Length > 256 || issuer.Any(char.IsControl) || subject.Any(char.IsControl))
            return null;
        return (issuer, subject);
    }
    private static void Bind(NpgsqlCommand command, (string Issuer, string Subject) owner)
    {
        command.Parameters.AddWithValue("issuer", owner.Issuer);
        command.Parameters.AddWithValue("subject", owner.Subject);
    }
}
