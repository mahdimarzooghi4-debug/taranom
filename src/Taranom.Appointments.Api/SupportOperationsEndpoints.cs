using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;

namespace Taranom.Appointments.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SupportReplyCommand(Guid RequestId, long ExpectedRevision, string Reply);

internal static class SupportOperationsEndpoints
{
    internal static void MapSupportOperationsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/operations/support-requests", async (long? before, Guid? requestRef,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (before is <= 0 || requestRef == Guid.Empty) return Results.BadRequest();
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT sequence_no,request_ref,request_id,topic,message,state,created_at,revision,reply,replied_at
                FROM taranom_member_support_request WHERE issuer=@issuer
                  AND (@before IS NULL OR sequence_no<@before)
                  AND (@ref IS NULL OR request_ref=@ref)
                ORDER BY sequence_no DESC LIMIT 21
                """, connection);
            command.Parameters.AddWithValue("issuer", context.User.FindFirst("iss")!.Value);
            command.Parameters.AddWithValue("before", NpgsqlDbType.Bigint, (object?)before ?? DBNull.Value);
            command.Parameters.AddWithValue("ref", NpgsqlDbType.Uuid, (object?)requestRef ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var requests = new List<MemberSupportRequest>();
            long last = 0;
            var more = false;
            while (await reader.ReadAsync(ct))
            {
                if (requests.Count == 20) { more = true; break; }
                last = reader.GetInt64(0);
                requests.Add(Read(reader, 1));
            }
            return Results.Ok(new { requests, nextBefore = more ? last.ToString(CultureInfo.InvariantCulture) : null });
        }).RequirePanelCapability(PanelCapability.SupportRequestsManage);

        app.MapPost("/api/v1/operations/support-requests/{requestRef:guid}/reply", async (
            Guid requestRef, SupportReplyCommand input, HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var reply = input.Reply?.Trim();
            if (requestRef == Guid.Empty || input.RequestId == Guid.Empty || input.ExpectedRevision != 0 ||
                string.IsNullOrWhiteSpace(reply) || reply.Length < 10 || reply.Length > 4000 ||
                reply.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                return Results.BadRequest(new { code = "INVALID_SUPPORT_REPLY" });
            var issuer = context.User.FindFirst("iss")!.Value;
            var subject = context.User.FindFirst("sub")!.Value;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { requestRef, input.ExpectedRevision, Reply = reply }))));
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            // Serialize the actor's receipt before locking a ticket, including retries against other tickets.
            await using (var serial = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", connection, transaction))
            {
                serial.Parameters.AddWithValue("key", JsonSerializer.Serialize(new { issuer, subject, input.RequestId }));
                await serial.ExecuteNonQueryAsync(ct);
            }
            bool replay;
            await using (var receipt = new NpgsqlCommand("""
                SELECT fingerprint FROM taranom_support_reply_receipt
                WHERE issuer=@issuer AND subject=@subject AND request_id=@id
                """, connection, transaction))
            {
                Bind(receipt, issuer, subject, input.RequestId);
                var previous = await receipt.ExecuteScalarAsync(ct) as string;
                if (previous is not null && previous != fingerprint)
                    return Results.Conflict(new { code = "SUPPORT_REPLY_ID_CONFLICT" });
                replay = previous is not null;
            }
            MemberSupportRequest ticket;
            await using (var read = new NpgsqlCommand("""
                SELECT request_ref,request_id,topic,message,state,created_at,revision,reply,replied_at
                FROM taranom_member_support_request WHERE issuer=@issuer AND request_ref=@ref FOR UPDATE
                """, connection, transaction))
            {
                read.Parameters.AddWithValue("issuer", issuer);
                read.Parameters.AddWithValue("ref", requestRef);
                await using var reader = await read.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) return Results.NotFound();
                ticket = Read(reader);
            }
            if (replay) return Results.Ok(new { outcome = "Replayed", request = ticket });
            if (ticket.Revision != input.ExpectedRevision || ticket.State != "Registered")
                return Results.Conflict(new { code = "SUPPORT_REPLY_STALE" });
            await using (var update = new NpgsqlCommand("""
                UPDATE taranom_member_support_request SET state='Answered',revision=1,reply=@reply,replied_at=now()
                WHERE issuer=@issuer AND request_ref=@ref
                RETURNING request_ref,request_id,topic,message,state,created_at,revision,reply,replied_at
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("issuer", issuer);
                update.Parameters.AddWithValue("ref", requestRef);
                update.Parameters.AddWithValue("reply", reply);
                await using var reader = await update.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Support update missing.");
                ticket = Read(reader);
            }
            await using (var audit = new NpgsqlCommand("""
                INSERT INTO taranom_support_reply_receipt(issuer,subject,request_id,request_ref,fingerprint)
                VALUES (@issuer,@subject,@id,@ref,@fingerprint)
                """, connection, transaction))
            {
                Bind(audit, issuer, subject, input.RequestId);
                audit.Parameters.AddWithValue("ref", requestRef);
                audit.Parameters.AddWithValue("fingerprint", fingerprint);
                await audit.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return Results.Ok(new { outcome = "Applied", request = ticket });
        }).RequirePanelCapability(PanelCapability.SupportRequestsManage);
    }

    private static void Bind(NpgsqlCommand command, string issuer, string subject, Guid id)
    {
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("id", id);
    }

    private static MemberSupportRequest Read(NpgsqlDataReader r, int o = 0) => new(
        r.GetGuid(o), r.GetGuid(o+1), r.GetString(o+2), r.GetString(o+3), r.GetString(o+4),
        r.GetFieldValue<DateTimeOffset>(o+5), r.GetInt64(o+6), r.IsDBNull(o+7) ? null : r.GetString(o+7),
        r.IsDBNull(o+8) ? null : r.GetFieldValue<DateTimeOffset>(o+8));
}
