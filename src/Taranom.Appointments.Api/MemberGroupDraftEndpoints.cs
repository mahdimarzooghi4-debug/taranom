using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Taranom.Appointments.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemberGroupDraftCommand(Guid RequestId, string Method, string RecipientLabel);
public sealed record MemberGroupDraftView(Guid DraftRef, Guid RequestId, string Method, string RecipientLabel,
    string State, DateTimeOffset CreatedAt, DateTimeOffset? WithdrawnAt);
public sealed record MemberGroupDraftInvitationSummary(Guid InvitationRef, string State,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RespondedAt);
public sealed record MemberGroupDraftListItem(Guid DraftRef, Guid RequestId, string Method, string RecipientLabel,
    string State, DateTimeOffset CreatedAt, DateTimeOffset? WithdrawnAt,
    MemberGroupDraftInvitationSummary? LatestInvitation);

internal static class MemberGroupDraftEndpoints
{
    internal static void MapMemberGroupDraftEndpoints(this WebApplication app, string policy)
    {
        app.MapGet("/api/v1/me/group-invitation-drafts", async (Guid? requestId, Guid? draftRef, long? before,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            if (requestId == Guid.Empty || draftRef == Guid.Empty || before is <= 0) return Results.BadRequest();
            // The label is the owner's own unverified planning note, never a recipient lookup.
            var supplied = context.Request.Query.TryGetValue("q", out var queryValues);
            string? search = null;
            if (supplied)
            {
                if (queryValues.Count != 1 || requestId.HasValue || draftRef.HasValue)
                    return Results.BadRequest();
                var raw = queryValues[0];
                search = raw?.Trim();
                if (raw is null || raw.Any(char.IsControl) ||
                    search is null || search.Length < 2 || search.Length > 100)
                    return Results.BadRequest();
            }
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT d.sequence_no,d.draft_ref,d.request_id,d.method,d.recipient_label,
                       d.state,d.created_at,d.withdrawn_at,
                       latest.invitation_ref,latest.effective_state,
                       latest.created_at,latest.expires_at,latest.responded_at
                FROM taranom_member_group_invite_draft d
                LEFT JOIN LATERAL (
                    SELECT i.invitation_ref,
                           CASE WHEN i.state='Pending' AND d.state='Withdrawn' THEN 'Revoked'
                                WHEN i.state='Pending' AND i.expires_at<=clock_timestamp() THEN 'Expired'
                                ELSE i.state END AS effective_state,
                           i.created_at,i.expires_at,i.responded_at
                    FROM taranom_member_group_invitation i
                    WHERE i.draft_ref=d.draft_ref
                    ORDER BY i.sequence_no DESC
                    LIMIT 1
                ) latest ON true
                WHERE d.issuer=@issuer AND d.subject=@subject
                  AND (@id IS NULL OR d.request_id=@id) AND (@ref IS NULL OR d.draft_ref=@ref)
                  AND (@before IS NULL OR d.sequence_no<@before)
                  AND (@q IS NULL OR strpos(lower(d.recipient_label),lower(@q))>0)
                ORDER BY d.sequence_no DESC LIMIT 21
                """, connection);
            Bind(command, owner.Value);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, (object?)requestId ?? DBNull.Value);
            command.Parameters.AddWithValue("ref", NpgsqlDbType.Uuid, (object?)draftRef ?? DBNull.Value);
            command.Parameters.AddWithValue("before", NpgsqlDbType.Bigint, (object?)before ?? DBNull.Value);
            command.Parameters.AddWithValue("q", NpgsqlDbType.Text, (object?)search ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var drafts = new List<MemberGroupDraftListItem>();
            long last = 0;
            var more = false;
            while (await reader.ReadAsync(ct))
            {
                if (drafts.Count == 20) { more = true; break; }
                last = reader.GetInt64(0);
                var draft = Read(reader, 1);
                MemberGroupDraftInvitationSummary? latest = reader.IsDBNull(8) ? null :
                    new MemberGroupDraftInvitationSummary(
                        reader.GetGuid(8), reader.GetString(9),
                        reader.GetFieldValue<DateTimeOffset>(10),
                        reader.GetFieldValue<DateTimeOffset>(11),
                        reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
                drafts.Add(new MemberGroupDraftListItem(
                    draft.DraftRef,draft.RequestId,draft.Method,draft.RecipientLabel,
                    draft.State,draft.CreatedAt,draft.WithdrawnAt,latest));
            }
            return Results.Ok(new { drafts, nextBefore = more ? last.ToString(CultureInfo.InvariantCulture) : null });
        }).RequireAuthorization(policy);

        app.MapPost("/api/v1/me/group-invitation-drafts", async (MemberGroupDraftCommand input,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            var label = input.RecipientLabel?.Trim();
            if (input.RequestId == Guid.Empty || input.Method is not ("NewRegistration" or "ExistingMember") ||
                string.IsNullOrWhiteSpace(label) || label.Length < 2 || label.Length > 100 || label.Any(char.IsControl))
                return Results.BadRequest(new { code = "INVALID_GROUP_DRAFT" });
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { input.Method, RecipientLabel = label }))));
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            MemberGroupDraftView? created = null;
            await using (var command = new NpgsqlCommand("""
                INSERT INTO taranom_member_group_invite_draft
                    (issuer,subject,draft_ref,request_id,method,recipient_label,fingerprint,state)
                VALUES (@issuer,@subject,@ref,@id,@method,@label,@fingerprint,'Draft')
                ON CONFLICT (issuer,subject,request_id) DO NOTHING
                RETURNING draft_ref,request_id,method,recipient_label,state,created_at,withdrawn_at
                """, connection, transaction))
            {
                Bind(command, owner.Value);
                command.Parameters.AddWithValue("ref", Guid.NewGuid());
                command.Parameters.AddWithValue("id", input.RequestId);
                command.Parameters.AddWithValue("method", input.Method);
                command.Parameters.AddWithValue("label", label);
                command.Parameters.AddWithValue("fingerprint", fingerprint);
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct)) created = Read(reader);
            }
            if (created is not null)
            {
                await transaction.CommitAsync(ct);
                return Results.Ok(new { outcome = "Applied", draft = created });
            }
            await using var replay = new NpgsqlCommand("""
                SELECT draft_ref,request_id,method,recipient_label,state,created_at,withdrawn_at,fingerprint
                FROM taranom_member_group_invite_draft WHERE issuer=@issuer AND subject=@subject AND request_id=@id
                """, connection, transaction);
            Bind(replay, owner.Value);
            replay.Parameters.AddWithValue("id", input.RequestId);
            await using var old = await replay.ExecuteReaderAsync(ct);
            if (!await old.ReadAsync(ct)) throw new InvalidOperationException("Group draft receipt missing.");
            if (old.GetString(7) != fingerprint) return Results.Conflict(new { code = "GROUP_DRAFT_REQUEST_CONFLICT" });
            return Results.Ok(new { outcome = "Replayed", draft = Read(old) });
        }).RequireAuthorization(policy);

        // Withdrawal is monotonic and naturally idempotent; it never sends or accepts an invitation.
        app.MapPost("/api/v1/me/group-invitation-drafts/{draftRef:guid}/withdraw", async (Guid draftRef,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var owner = Owner(context);
            if (owner is null) return Results.Forbid();
            if (draftRef == Guid.Empty) return Results.BadRequest();
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand("""
                UPDATE taranom_member_group_invite_draft SET state='Withdrawn',withdrawn_at=COALESCE(withdrawn_at,now())
                WHERE issuer=@issuer AND subject=@subject AND draft_ref=@ref
                RETURNING draft_ref,request_id,method,recipient_label,state,created_at,withdrawn_at
                """, connection);
            Bind(command, owner.Value);
            command.Parameters.AddWithValue("ref", draftRef);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Results.Ok(new { draft = Read(reader) }) : Results.NotFound();
        }).RequireAuthorization(policy);
    }
    private static MemberGroupDraftView Read(NpgsqlDataReader r, int o = 0) => new(
        r.GetGuid(o), r.GetGuid(o+1), r.GetString(o+2), r.GetString(o+3), r.GetString(o+4),
        r.GetFieldValue<DateTimeOffset>(o+5), r.IsDBNull(o+6) ? null : r.GetFieldValue<DateTimeOffset>(o+6));
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
