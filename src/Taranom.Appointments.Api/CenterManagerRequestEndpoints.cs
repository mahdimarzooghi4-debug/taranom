using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;

namespace Taranom.Appointments.Api;

// These requests are identity-bound *claims*, never proof of managerial authority.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CenterManagerRequestCommand(Guid RequestId, string CenterRef, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CenterManagerReviewCommand(Guid CommandId, string Decision, string Reason);

internal static class CenterManagerRequestEndpoints
{
    private const string Pending = "PENDING_REVIEW";
    private const string PendingProof = "REVIEWED_PENDING_VERIFICATION";
    private const string Declined = "DECLINED";

    internal static void MapCenterManagerRequestEndpoints(
        this WebApplication app, string authenticatedPolicy)
    {
        app.MapPost("/api/v1/me/center-manager-requests", SubmitAsync)
            .RequireAuthorization(authenticatedPolicy);
        app.MapGet("/api/v1/me/center-manager-requests", ListOwnAsync)
            .RequireAuthorization(authenticatedPolicy);
        app.MapGet("/api/v1/me/center-manager-requests/{requestId:guid}", ReadOwnAsync)
            .RequireAuthorization(authenticatedPolicy);
        app.MapGet("/api/v1/operations/center-manager-requests", ListAdminAsync)
            .RequirePanelCapability(PanelCapability.AdminCenterRegistryManage);
        app.MapPost("/api/v1/operations/center-manager-requests/{requestId:guid}/review",
            ReviewAsync).RequirePanelCapability(PanelCapability.AdminCenterRegistryManage);
        app.MapGet("/api/v1/operations/center-manager-requests/{requestId:guid}/review-receipts/{commandId:guid}",
            ReadReviewReceiptAsync).RequirePanelCapability(PanelCapability.AdminCenterRegistryManage);
    }

    private static bool ValidRef(string? value, int max) =>
        value is { Length: > 0 } && value.Length <= max &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static bool ValidCenter(string? value) =>
        value is { Length: 39 } && value.StartsWith("center-", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static bool ValidReason(string? value) =>
        value is { Length: >= 10 and <= 500 } &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static (string Issuer, string Subject)? Identity(HttpContext context)
    {
        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        return ValidRef(issuer, 512) && ValidRef(subject, 256)
            ? (issuer!, subject!) : null;
    }

    private static string Fingerprint(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(parts))));

    private static async Task LockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string key,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", conn, tx);
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IResult> SubmitAsync(
        CenterManagerRequestCommand input, HttpContext context,
        NpgsqlDataSource db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var id = Identity(context);
        if (id is null) return Results.Forbid();
        var reason = input.Reason?.Trim();
        if (input.RequestId == Guid.Empty || !ValidCenter(input.CenterRef) ||
            !ValidReason(reason))
            return Results.BadRequest(new { code = "INVALID_MANAGER_REQUEST" });

        var fingerprint = Fingerprint(input.CenterRef, reason!);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // One unverified manager claim per OIDC identity and canonical center.
        // This also serializes two different UUIDs submitted in parallel.
        // A UUID collision between unrelated actors must resolve as an
        // explicit conflict instead of surfacing a database uniqueness error.
        await LockAsync(conn, tx, "manager-request-uuid:" +
            input.RequestId.ToString("D"), ct);
        await LockAsync(conn, tx,
            "manager-request:" + id.Value.Issuer + ":" + id.Value.Subject +
            ":" + input.CenterRef, ct);

        await using (var center = new NpgsqlCommand("""
            SELECT 1 FROM taranom_canonical_center
            WHERE center_ref=@center FOR SHARE
            """, conn, tx))
        {
            center.Parameters.AddWithValue("center", input.CenterRef);
            if (await center.ExecuteScalarAsync(ct) is null)
                return Results.NotFound(new { code = "CENTER_NOT_ADMIN_DEFINED" });
        }

        await using (var replay = new NpgsqlCommand("""
            SELECT issuer,subject,center_ref,fingerprint
            FROM taranom_center_manager_request WHERE request_id=@request
            """, conn, tx))
        {
            replay.Parameters.AddWithValue("request", input.RequestId);
            await using var reader = await replay.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var same = reader.GetString(0) == id.Value.Issuer &&
                    reader.GetString(1) == id.Value.Subject &&
                    reader.GetString(2) == input.CenterRef &&
                    reader.GetString(3) == fingerprint;
                return same ? Results.Ok(new { outcome = "Replayed",
                    requestId = input.RequestId, centerRef = input.CenterRef,
                    state = Pending, managerAssigned = false })
                    : Results.Conflict(new { code = "MANAGER_REQUEST_ID_CONFLICT" });
            }
        }
        await using (var existing = new NpgsqlCommand("""
            SELECT 1 FROM taranom_center_manager_request
            WHERE issuer=@issuer AND subject=@subject AND center_ref=@center
            """, conn, tx))
        {
            existing.Parameters.AddWithValue("issuer", id.Value.Issuer);
            existing.Parameters.AddWithValue("subject", id.Value.Subject);
            existing.Parameters.AddWithValue("center", input.CenterRef);
            if (await existing.ExecuteScalarAsync(ct) is not null)
                return Results.Conflict(new { code = "MANAGER_REQUEST_ALREADY_EXISTS" });
        }
        await using (var create = new NpgsqlCommand("""
            INSERT INTO taranom_center_manager_request
                (request_id,issuer,subject,center_ref,reason,fingerprint)
            VALUES (@request,@issuer,@subject,@center,@reason,@fingerprint)
            """, conn, tx))
        {
            create.Parameters.AddWithValue("request", input.RequestId);
            create.Parameters.AddWithValue("issuer", id.Value.Issuer);
            create.Parameters.AddWithValue("subject", id.Value.Subject);
            create.Parameters.AddWithValue("center", input.CenterRef);
            create.Parameters.AddWithValue("reason", reason!);
            create.Parameters.AddWithValue("fingerprint", fingerprint);
            await create.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Results.Ok(new { outcome = "Applied", requestId = input.RequestId,
            centerRef = input.CenterRef, state = Pending, managerAssigned = false });
    }

    // Read-only recovery for a lost POST acknowledgement. The lookup is
    // restricted to the original OIDC issuer/subject and never reissues POST.
    private static async Task<IResult> ReadOwnAsync(
        Guid requestId, HttpContext context, NpgsqlDataSource db,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var id = Identity(context);
        if (id is null) return Results.Forbid();
        if (requestId == Guid.Empty) return Results.BadRequest();
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT r.center_ref,COALESCE(review.decision,'PENDING_REVIEW')
            FROM taranom_center_manager_request AS r
            LEFT JOIN taranom_center_manager_request_review AS review
              ON review.request_id=r.request_id
            WHERE r.request_id=@request AND r.issuer=@issuer AND r.subject=@subject
            """, conn);
        cmd.Parameters.AddWithValue("request", requestId);
        cmd.Parameters.AddWithValue("issuer", id.Value.Issuer);
        cmd.Parameters.AddWithValue("subject", id.Value.Subject);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return Results.NotFound(new { code = "MANAGER_REQUEST_NOT_OBSERVED" });
        return Results.Ok(new { outcome="Observed", requestId,
            centerRef=reader.GetString(0), state=reader.GetString(1),
            managerAssigned=false, associationGateRequired=true });
    }

    private static async Task<IResult> ListOwnAsync(
        string? after, HttpContext context, NpgsqlDataSource db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var id = Identity(context);
        if (id is null) return Results.Forbid();
        return await ListAsync(db, id.Value, after, admin: false, ct);
    }

    private static async Task<IResult> ListAdminAsync(
        string? after, HttpContext context, NpgsqlDataSource db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var id = Identity(context);
        if (id is null) return Results.Forbid();
        return await ListAsync(db, id.Value, after, admin: true, ct);
    }

    private static async Task<IResult> ListAsync(
        NpgsqlDataSource db, (string Issuer, string Subject) actor,
        string? after, bool admin, CancellationToken ct)
    {
        if (after is not null &&
            (!Guid.TryParseExact(after, "D", out var parsed) ||
             !string.Equals(parsed.ToString("D"), after, StringComparison.Ordinal)))
            return Results.BadRequest(new { code = "INVALID_MANAGER_REQUEST_CURSOR" });
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT r.request_id,r.center_ref,r.issuer,r.subject,r.reason,r.created_at,
                   review.decision,review.reason,review.reviewed_at
            FROM taranom_center_manager_request AS r
            LEFT JOIN taranom_center_manager_request_review AS review
                ON review.request_id=r.request_id
            WHERE (@admin OR (r.issuer=@issuer AND r.subject=@subject))
              AND r.request_id::text > @after
            ORDER BY r.request_id::text
            LIMIT 26
            """, conn);
        cmd.Parameters.AddWithValue("admin", admin);
        cmd.Parameters.AddWithValue("issuer", actor.Issuer);
        cmd.Parameters.AddWithValue("subject", actor.Subject);
        cmd.Parameters.AddWithValue("after", after ?? "");
        var rows = new List<object>();
        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var requestId = reader.GetGuid(0).ToString("D");
            ids.Add(requestId);
            rows.Add(new {
                requestId,
                centerRef = reader.GetString(1),
                applicantIssuer = reader.GetString(2),
                applicantSubject = reader.GetString(3),
                reason = reader.GetString(4),
                submittedAt = reader.GetFieldValue<DateTimeOffset>(5),
                state = reader.IsDBNull(6) ? Pending : reader.GetString(6),
                reviewReason = reader.IsDBNull(7) ? null : reader.GetString(7),
                reviewedAt = reader.IsDBNull(8)
                    ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(8),
                managerAssigned = false
            });
        }
        return Results.Ok(new {
            requests = rows.Take(25).ToArray(),
            nextAfter = rows.Count > 25 ? ids[24] : null,
            associationGateRequired = true
        });
    }

    // Exact, actor-bound readback of a review command after an uncertain
    // network acknowledgement. Pagination or a matching human reason
    // cannot be used as evidence that THIS actor's command was committed.
    private static async Task<IResult> ReadReviewReceiptAsync(
        Guid requestId, Guid commandId, HttpContext context,
        NpgsqlDataSource db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var actor = Identity(context);
        if (actor is null) return Results.Forbid();
        if (requestId == Guid.Empty || commandId == Guid.Empty)
            return Results.BadRequest(new { code = "INVALID_MANAGER_REVIEW_RECEIPT" });
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT decision,reason,reviewed_at
            FROM taranom_center_manager_request_review
            WHERE request_id=@request AND command_id=@command
              AND reviewer_issuer=@issuer AND reviewer_subject=@subject
            """, conn);
        cmd.Parameters.AddWithValue("request", requestId);
        cmd.Parameters.AddWithValue("command", commandId);
        cmd.Parameters.AddWithValue("issuer", actor.Value.Issuer);
        cmd.Parameters.AddWithValue("subject", actor.Value.Subject);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return Results.NotFound(new { code = "MANAGER_REVIEW_RECEIPT_NOT_OBSERVED" });
        return Results.Ok(new {
            outcome = "Observed", requestId, commandId,
            decision = reader.GetString(0),
            reason = reader.GetString(1),
            reviewedAt = reader.GetFieldValue<DateTimeOffset>(2),
            managerAssigned = false
        });
    }

    private static async Task<IResult> ReviewAsync(
        Guid requestId, CenterManagerReviewCommand input, HttpContext context,
        NpgsqlDataSource db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var id = Identity(context);
        if (id is null) return Results.Forbid();
        var reason = input.Reason?.Trim();
        if (requestId == Guid.Empty || input.CommandId == Guid.Empty ||
            (input.Decision != PendingProof && input.Decision != Declined) ||
            !ValidReason(reason))
            return Results.BadRequest(new { code = "INVALID_MANAGER_REVIEW" });

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Stable lock order: reviewer+command first, request second.
        await LockAsync(conn, tx, "manager-review-command:" + id.Value.Issuer + ":" +
            id.Value.Subject + ":" + input.CommandId.ToString("D"), ct);
        await LockAsync(conn, tx, "manager-review-request:" + requestId.ToString("D"), ct);

        await using (var grant = new NpgsqlCommand("""
            SELECT 1 FROM taranom_panel_access_grant
            WHERE issuer=@issuer AND subject=@subject
              AND capability='AdminCenterRegistryManage'
              AND scope_ref='taranom-center-registry'
              AND grant_state='active' AND subject_mapping_current
              AND permission_version_current AND grant_not_revoked AND audit_ready
            FOR SHARE
            """, conn, tx))
        {
            grant.Parameters.AddWithValue("issuer", id.Value.Issuer);
            grant.Parameters.AddWithValue("subject", id.Value.Subject);
            if (await grant.ExecuteScalarAsync(ct) is null)
                return Results.Forbid();
        }

        await using (var request = new NpgsqlCommand("""
            SELECT issuer,subject FROM taranom_center_manager_request
            WHERE request_id=@request
            """, conn, tx))
        {
            request.Parameters.AddWithValue("request", requestId);
            await using var reader = await request.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return Results.NotFound(new { code = "MANAGER_REQUEST_NOT_FOUND" });
            // Requester cannot review their own claim, even if they hold
            // an unrelated Admin registry grant.
            if (reader.GetString(0) == id.Value.Issuer &&
                reader.GetString(1) == id.Value.Subject)
                return Results.Forbid();
        }

        await using (var prior = new NpgsqlCommand("""
            SELECT reviewer_issuer,reviewer_subject,command_id,decision,reason
            FROM taranom_center_manager_request_review WHERE request_id=@request
            """, conn, tx))
        {
            prior.Parameters.AddWithValue("request", requestId);
            await using var reader = await prior.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var same = reader.GetString(0) == id.Value.Issuer &&
                    reader.GetString(1) == id.Value.Subject &&
                    reader.GetGuid(2) == input.CommandId &&
                    reader.GetString(3) == input.Decision &&
                    reader.GetString(4) == reason;
                return same ? Results.Ok(new {
                    outcome = "Replayed", requestId,
                    state = input.Decision, managerAssigned = false })
                    : Results.Conflict(new { code = "MANAGER_REVIEW_ALREADY_RECORDED" });
            }
        }

        await using (var used = new NpgsqlCommand("""
            SELECT 1 FROM taranom_center_manager_request_review
            WHERE reviewer_issuer=@issuer AND reviewer_subject=@subject
              AND command_id=@command
            """, conn, tx))
        {
            used.Parameters.AddWithValue("issuer", id.Value.Issuer);
            used.Parameters.AddWithValue("subject", id.Value.Subject);
            used.Parameters.AddWithValue("command", input.CommandId);
            if (await used.ExecuteScalarAsync(ct) is not null)
                return Results.Conflict(new { code = "MANAGER_REVIEW_COMMAND_ID_CONFLICT" });
        }

        await using (var record = new NpgsqlCommand("""
            INSERT INTO taranom_center_manager_request_review
                (request_id,reviewer_issuer,reviewer_subject,command_id,decision,reason)
            VALUES (@request,@issuer,@subject,@command,@decision,@reason)
            """, conn, tx))
        {
            record.Parameters.AddWithValue("request", requestId);
            record.Parameters.AddWithValue("issuer", id.Value.Issuer);
            record.Parameters.AddWithValue("subject", id.Value.Subject);
            record.Parameters.AddWithValue("command", input.CommandId);
            record.Parameters.AddWithValue("decision", input.Decision);
            record.Parameters.AddWithValue("reason", reason!);
            await record.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Results.Ok(new {
            outcome = "Applied", requestId,
            state = input.Decision, managerAssigned = false
        });
    }
}
