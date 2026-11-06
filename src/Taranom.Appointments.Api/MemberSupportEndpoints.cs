using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace Taranom.Appointments.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemberSupportCommand(Guid RequestId, string Topic, string Message);
public sealed record MemberSupportRequest(Guid RequestRef, Guid RequestId, string Topic, string Message,
    string State, DateTimeOffset CreatedAt, long Revision, string? Reply, DateTimeOffset? RepliedAt);

internal static class MemberSupportEndpoints
{
    private static readonly HashSet<string> Topics = new(StringComparer.Ordinal)
        { "account", "group", "appointments", "privacy" };

    internal static void MapMemberSupportEndpoints(this WebApplication app, string policy)
    {
        app.MapGet("/api/v1/me/support-requests", async (Guid? requestId, long? before,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var identity = Identity(context);
            if (identity is null) return Results.Forbid();
            context.Response.Headers.CacheControl = "no-store";
            if (before is <= 0 || requestId == Guid.Empty)
                return Results.BadRequest(new { code = "INVALID_SUPPORT_QUERY" });
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT sequence_no,request_ref,request_id,topic,message,state,created_at,revision,reply,replied_at
                FROM taranom_member_support_request
                WHERE issuer=@issuer AND subject=@subject
                  AND (@request_id IS NULL OR request_id=@request_id)
                  AND (@before IS NULL OR sequence_no<@before)
                ORDER BY sequence_no DESC LIMIT 21
                """, connection);
            BindIdentity(command, identity.Value);
            command.Parameters.AddWithValue("request_id", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)requestId ?? DBNull.Value);
            command.Parameters.AddWithValue("before", NpgsqlTypes.NpgsqlDbType.Bigint, (object?)before ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var items = new List<MemberSupportRequest>();
            long lastSequence = 0;
            var hasMore = false;
            while (await reader.ReadAsync(ct))
            {
                if (items.Count == 20) { hasMore = true; break; }
                lastSequence = reader.GetInt64(0);
                items.Add(new MemberSupportRequest(reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
            }
            return Results.Ok(new { requests = items,
                nextBefore = hasMore ? lastSequence.ToString(CultureInfo.InvariantCulture) : null });
        }).RequireAuthorization(policy);

        app.MapPost("/api/v1/me/support-requests", async (MemberSupportCommand input,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var identity = Identity(context);
            if (identity is null) return Results.Forbid();
            context.Response.Headers.CacheControl = "no-store";
            var message = input.Message?.Trim();
            if (input.RequestId == Guid.Empty || input.Topic is null || !Topics.Contains(input.Topic) ||
                string.IsNullOrWhiteSpace(message) || message.Length < 10 || message.Length > 4000 ||
                message.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
                return Results.BadRequest(new { code = "INVALID_SUPPORT_REQUEST" });
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { input.Topic, Message = message }))));
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var create = new NpgsqlCommand("""
                INSERT INTO taranom_member_support_request
                    (issuer,subject,request_id,request_ref,topic,message,fingerprint,state)
                VALUES (@issuer,@subject,@request_id,@request_ref,@topic,@message,@fingerprint,'Registered')
                ON CONFLICT (issuer,subject,request_id) DO NOTHING
                RETURNING request_ref,request_id,topic,message,state,created_at,revision,reply,replied_at
                """, connection, transaction))
            {
                BindIdentity(create, identity.Value);
                create.Parameters.AddWithValue("request_id", input.RequestId);
                create.Parameters.AddWithValue("request_ref", Guid.NewGuid());
                create.Parameters.AddWithValue("topic", input.Topic);
                create.Parameters.AddWithValue("message", message);
                create.Parameters.AddWithValue("fingerprint", fingerprint);
                MemberSupportRequest? created = null;
                await using (var reader = await create.ExecuteReaderAsync(ct))
                    if (await reader.ReadAsync(ct)) created = ReadRequest(reader);
                if (created is not null)
                {
                    await transaction.CommitAsync(ct);
                    return Results.Ok(new { outcome = "Applied", request = created });
                }
            }
            await using var existing = new NpgsqlCommand("""
                SELECT request_ref,request_id,topic,message,state,created_at,revision,reply,replied_at,fingerprint
                FROM taranom_member_support_request
                WHERE issuer=@issuer AND subject=@subject AND request_id=@request_id
                """, connection, transaction);
            BindIdentity(existing, identity.Value);
            existing.Parameters.AddWithValue("request_id", input.RequestId);
            await using var old = await existing.ExecuteReaderAsync(ct);
            if (!await old.ReadAsync(ct)) throw new InvalidOperationException("Support request receipt missing.");
            if (!string.Equals(old.GetString(9), fingerprint, StringComparison.Ordinal))
                return Results.Conflict(new { code = "SUPPORT_REQUEST_ID_CONFLICT" });
            return Results.Ok(new { outcome = "Replayed", request = ReadRequest(old) });
        }).RequireAuthorization(policy);
    }

    private static MemberSupportRequest ReadRequest(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetFieldValue<DateTimeOffset>(5), reader.GetInt64(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8));

    private static (string Issuer, string Subject)? Identity(HttpContext context)
    {
        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ||
            issuer.Length > 512 || subject.Length > 256 || issuer.Any(char.IsControl) || subject.Any(char.IsControl)) return null;
        return (issuer, subject);
    }

    private static void BindIdentity(NpgsqlCommand command, (string Issuer, string Subject) identity)
    {
        command.Parameters.AddWithValue("issuer", identity.Issuer);
        command.Parameters.AddWithValue("subject", identity.Subject);
    }
}
