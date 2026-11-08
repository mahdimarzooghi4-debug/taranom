using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Npgsql;

namespace Taranom.Appointments.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GroupInvitationIssue(Guid RequestId, string Token);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GroupInvitationToken(string Token);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GroupInvitationDecision(string Token, string Decision, string ConsentVersion);
public sealed record GroupInvitationView(Guid InvitationRef, Guid RequestId, string State,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RespondedAt);

internal static class MemberGroupInvitationEndpoints
{
    private const string Consent = "group-invitation-v1";
    internal static void MapMemberGroupInvitationEndpoints(this WebApplication app, string policy)
    {
        app.MapGet("/api/v1/me/group-invitations", async (Guid? invitationRef, long? before,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var recipient = Owner(context);
            if (recipient is null) return Results.Forbid();
            if (invitationRef == Guid.Empty || before is <= 0) return Results.BadRequest();
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand(Select + " " + """
                 WHERE i.recipient_issuer=@issuer AND i.recipient_subject=@subject
                   AND (@ref IS NULL OR i.invitation_ref=@ref) AND (@before IS NULL OR i.sequence_no<@before)
                 ORDER BY i.sequence_no DESC LIMIT 21
                """, connection);
            Bind(command, recipient.Value);
            command.Parameters.AddWithValue("ref", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)invitationRef ?? DBNull.Value);
            command.Parameters.AddWithValue("before", NpgsqlTypes.NpgsqlDbType.Bigint, (object?)before ?? DBNull.Value);
            await using var r = await command.ExecuteReaderAsync(ct);
            var invitations = new List<object>(); long last = 0; var more = false;
            while (await r.ReadAsync(ct))
            {
                if (invitations.Count == 20) { more = true; break; }
                invitations.Add(Recipient(Read(r))); last = r.GetInt64(9);
            }
            return Results.Ok(new { invitations, nextBefore = more ? last.ToString(System.Globalization.CultureInfo.InvariantCulture) : null });
        }).RequireAuthorization(policy);


        // Owner's entire issuance ledger. Unlike recipient response history this
        // reads through the draft's trusted issuer/subject, never the bearer token.
        // Exact foreign refs are indistinguishable from unknown refs.
        app.MapGet("/api/v1/me/group-invitation-activity", async (Guid? invitationRef, long? before,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            var query = context.Request.Query;
            if (invitationRef == Guid.Empty || before is <= 0 ||
                (invitationRef.HasValue && before.HasValue) ||
                query.Any(p => p.Value.Count != 1 ||
                    p.Key is not ("invitationRef" or "before")))
                return Results.BadRequest();
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand(Select + " " + """
                WHERE d.issuer=@issuer AND d.subject=@subject
                  AND (@ref IS NULL OR i.invitation_ref=@ref)
                  AND (@before IS NULL OR i.sequence_no<@before)
                ORDER BY i.sequence_no DESC LIMIT 21
                """, connection);
            Bind(command, owner.Value);
            command.Parameters.AddWithValue("ref", NpgsqlTypes.NpgsqlDbType.Uuid,
                (object?)invitationRef ?? DBNull.Value);
            command.Parameters.AddWithValue("before", NpgsqlTypes.NpgsqlDbType.Bigint,
                (object?)before ?? DBNull.Value);
            await using var r = await command.ExecuteReaderAsync(ct);
            var invitations = new List<object>(); long last = 0; var more = false;
            while (await r.ReadAsync(ct))
            {
                if (invitations.Count == 20) { more = true; break; }
                var view = Read(r);
                // The private label is precisely the owner's own planning input.
                // Never project recipient_issuer/subject, token_hash or request UUID.
                invitations.Add(new
                {
                    view.InvitationRef, DraftRef = r.GetGuid(10),
                    RecipientLabel = r.GetString(11), DraftState = r.GetString(12),
                    view.State, view.CreatedAt, view.ExpiresAt, view.RespondedAt
                });
                last = r.GetInt64(9);
            }
            return Results.Ok(new
            {
                invitations,
                nextBefore = more ? last.ToString(System.Globalization.CultureInfo.InvariantCulture) : null
            });
        }).RequireAuthorization(policy);

        // The raw random secret is supplied by the browser and never stored or returned by this API.
        app.MapPost("/api/v1/me/group-invitation-drafts/{draftRef:guid}/invitations", async (
            Guid draftRef, GroupInvitationIssue input, HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            if (draftRef == Guid.Empty || input.RequestId == Guid.Empty || !ValidToken(input.Token)) return Results.BadRequest();
            var hash = Hash(input.Token);
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);
            await using (var draft = new NpgsqlCommand("SELECT state FROM taranom_member_group_invite_draft WHERE draft_ref=@ref AND issuer=@issuer AND subject=@subject FOR UPDATE", connection, tx))
            {
                draft.Parameters.AddWithValue("ref", draftRef); Bind(draft, owner.Value);
                var state = await draft.ExecuteScalarAsync(ct) as string;
                if (state is null) return Results.NotFound();
                // Replay is allowed after archive, but cannot revive a token.
                await using var replay = new NpgsqlCommand(Select + " WHERE i.draft_ref=@ref AND i.request_id=@id", connection, tx);
                replay.Parameters.AddWithValue("ref", draftRef); replay.Parameters.AddWithValue("id", input.RequestId);
                await using (var r = await replay.ExecuteReaderAsync(ct))
                {
                    if (await r.ReadAsync(ct))
                        return r.GetString(6) == hash ? Results.Ok(new { outcome = "Replayed", invitation = Read(r) }) : Results.Conflict();
                }
                if (state != "Draft") return Results.Conflict();
            }
            await using (var accepted = new NpgsqlCommand("SELECT 1 FROM taranom_member_group_invitation WHERE draft_ref=@ref AND state='Accepted'", connection, tx))
            {
                accepted.Parameters.AddWithValue("ref", draftRef);
                if (await accepted.ExecuteScalarAsync(ct) is not null) return Results.Conflict();
            }
            // Issuing a replacement invalidates all previous unanswered secrets atomically.
            await using (var replace = new NpgsqlCommand("UPDATE taranom_member_group_invitation SET state='Revoked' WHERE draft_ref=@ref AND state='Pending'", connection, tx))
            { replace.Parameters.AddWithValue("ref", draftRef); await replace.ExecuteNonQueryAsync(ct); }
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO taranom_member_group_invitation(invitation_ref,draft_ref,request_id,token_hash,state,expires_at)
                VALUES(@ref,@draft,@id,@hash,'Pending',now()+interval '7 days') ON CONFLICT(token_hash) DO NOTHING
                """, connection, tx))
            {
                insert.Parameters.AddWithValue("ref", Guid.NewGuid()); insert.Parameters.AddWithValue("draft", draftRef);
                insert.Parameters.AddWithValue("id", input.RequestId); insert.Parameters.AddWithValue("hash", hash);
                if (await insert.ExecuteNonQueryAsync(ct) != 1) return Results.Conflict();
            }
            GroupInvitationView result;
            await using (var read = new NpgsqlCommand(Select + " WHERE i.draft_ref=@ref AND i.request_id=@id", connection, tx))
            {
                read.Parameters.AddWithValue("ref", draftRef); read.Parameters.AddWithValue("id", input.RequestId);
                await using var r = await read.ExecuteReaderAsync(ct); await r.ReadAsync(ct); result = Read(r);
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new { outcome = "Applied", invitation = result });
        }).RequireAuthorization(policy);

        app.MapGet("/api/v1/me/group-invitation-drafts/{draftRef:guid}/invitations", async (
            Guid draftRef, Guid? requestId, HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            if (draftRef == Guid.Empty || requestId == Guid.Empty) return Results.BadRequest();
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand(Select + " WHERE d.draft_ref=@ref AND d.issuer=@issuer AND d.subject=@subject AND (@id IS NULL OR i.request_id=@id) ORDER BY i.sequence_no DESC LIMIT 1", connection);
            Bind(command, owner.Value); command.Parameters.AddWithValue("ref", draftRef);
            command.Parameters.AddWithValue("id", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)requestId ?? DBNull.Value);
            await using var r = await command.ExecuteReaderAsync(ct);
            return Results.Ok(new { invitation = await r.ReadAsync(ct) ? Read(r) : null });
        }).RequireAuthorization(policy);

        // POST keeps the secret out of URL, referrer and access-log query strings. Preview is read-only.
        app.MapPost("/api/v1/me/group-invitations/preview", (GroupInvitationToken input,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) => Respond(input.Token, null, context, db, ct))
            .RequireAuthorization(policy);
        app.MapPost("/api/v1/me/group-invitations/respond", (GroupInvitationDecision input,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
                input.Decision is not ("Accepted" or "Declined") || input.ConsentVersion != Consent
                    ? Task.FromResult<IResult>(Results.BadRequest()) : Respond(input.Token, input.Decision, context, db, ct))
            .RequireAuthorization(policy);
    }

    private static async Task<IResult> Respond(string token, string? decision, HttpContext context, NpgsqlDataSource db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var actor = Owner(context);
        if (actor is null) return Results.Forbid();
        if (!ValidToken(token)) return Results.BadRequest();
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        Guid draftRef;
        await using (var find = new NpgsqlCommand("SELECT draft_ref FROM taranom_member_group_invitation WHERE token_hash=@hash", connection, tx))
        {
            find.Parameters.AddWithValue("hash", Hash(token));
            var value = await find.ExecuteScalarAsync(ct);
            if (value is not Guid found) return Results.NotFound();
            draftRef = found;
        }
        // Same lock order as issue and draft archive: draft first, then invitation.
        await using (var guard = new NpgsqlCommand("SELECT issuer,subject FROM taranom_member_group_invite_draft WHERE draft_ref=@ref FOR UPDATE", connection, tx))
        {
            guard.Parameters.AddWithValue("ref", draftRef);
            await using var r = await guard.ExecuteReaderAsync(ct); await r.ReadAsync(ct);
            if (r.GetString(0) == actor.Value.Issuer && r.GetString(1) == actor.Value.Subject) return Results.Forbid();
        }
        GroupInvitationView view;
        await using (var read = new NpgsqlCommand(Select + " WHERE i.token_hash=@hash", connection, tx))
        {
            read.Parameters.AddWithValue("hash", Hash(token));
            await using var r = await read.ExecuteReaderAsync(ct); await r.ReadAsync(ct); view = Read(r);
            if (!r.IsDBNull(7) && (r.GetString(7) != actor.Value.Issuer || r.GetString(8) != actor.Value.Subject)) return Results.NotFound();
        }
        if (decision is null) return Results.Ok(new { invitation = Recipient(view), consentVersion = Consent });
        if (view.State is "Accepted" or "Declined")
            return view.State == decision ? Results.Ok(new { invitation = Recipient(view), consentVersion = Consent }) : Results.Conflict();
        if (view.State != "Pending") return Results.Conflict();
        await using (var update = new NpgsqlCommand("""
            UPDATE taranom_member_group_invitation SET state=@state,responded_at=now(),recipient_issuer=@issuer,
                recipient_subject=@subject,consent_version=@consent WHERE invitation_ref=@ref
            RETURNING responded_at
            """, connection, tx))
        {
            Bind(update, actor.Value); update.Parameters.AddWithValue("state", decision); update.Parameters.AddWithValue("consent", Consent);
            update.Parameters.AddWithValue("ref", view.InvitationRef);
            var at = (DateTime)(await update.ExecuteScalarAsync(ct))!;
            view = view with { State = decision, RespondedAt = new DateTimeOffset(at) };
        }
        await tx.CommitAsync(ct);
        return Results.Ok(new { invitation = Recipient(view), consentVersion = Consent });
    }
    // Owner's private label, request id and recipient identity are never disclosed to the bearer.
    private static object Recipient(GroupInvitationView v) => new { v.InvitationRef, v.State, v.CreatedAt, v.ExpiresAt, v.RespondedAt };
    private const string Select = """
        SELECT i.invitation_ref,i.request_id,
        CASE WHEN i.state='Pending' AND d.state='Withdrawn' THEN 'Revoked'
             WHEN i.state='Pending' AND i.expires_at<=clock_timestamp() THEN 'Expired' ELSE i.state END,
        i.created_at,i.expires_at,i.responded_at,i.token_hash,i.recipient_issuer,i.recipient_subject,i.sequence_no,
        d.draft_ref,d.recipient_label,d.state
        FROM taranom_member_group_invitation i JOIN taranom_member_group_invite_draft d ON d.draft_ref=i.draft_ref
        """;
    private static GroupInvitationView Read(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2),
        r.GetFieldValue<DateTimeOffset>(3), r.GetFieldValue<DateTimeOffset>(4), r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5));
    private static bool ValidToken(string? token) => token is { Length: 64 } && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static (string Issuer, string Subject)? Owner(HttpContext context)
    {
        var issuer = context.User.FindFirst("iss")?.Value; var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) || issuer.Length > 512 || subject.Length > 256 || issuer.Any(char.IsControl) || subject.Any(char.IsControl)) return null;
        return (issuer, subject);
    }
    private static void Bind(NpgsqlCommand c, (string Issuer, string Subject) owner)
    { c.Parameters.AddWithValue("issuer", owner.Issuer); c.Parameters.AddWithValue("subject", owner.Subject); }
}
