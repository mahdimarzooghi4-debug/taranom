using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;

namespace Taranom.Appointments.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CenterDirectoryDraftMutation(
    Guid RequestId, long ExpectedRevision, string DisplayName, string City);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CenterDoctorDraftMutation(
    Guid RequestId, long ExpectedRevision, string DisplayName, string Specialty);

internal static class CenterDirectoryDraftEndpoints
{
    internal static void MapCenterDirectoryDraftEndpoints(this WebApplication app, string identityPolicy)
    {
        app.MapGet("/api/v1/me/center-directory-draft-scopes", async (
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var identity = Identity(context);
            if (identity is null) return Results.Forbid();
            const string sql = """
                SELECT capability, scope_ref FROM taranom_panel_access_grant AS grant_row
                WHERE issuer=@issuer AND subject=@subject
                  AND capability IN ('CenterAccessGrantsManage','CenterRosterManage')
                  AND grant_state='active'
                  AND subject_mapping_current AND permission_version_current
                  AND grant_not_revoked AND audit_ready
                  AND EXISTS (SELECT 1 FROM taranom_canonical_center c
                              WHERE c.center_ref=grant_row.scope_ref)
                ORDER BY capability,scope_ref
                """;
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection);
            BindIdentity(command, identity.Value);
            var scopes = new List<object>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                scopes.Add(new { capability = reader.GetString(0), scopeRef = reader.GetString(1) });
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { scopes });
        }).RequireAuthorization(identityPolicy);

        var center = app.MapGroup("/api/v1/centers/{centerRef}");

        center.MapGet("/directory-draft", async (string centerRef,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            await using var connection = await db.OpenConnectionAsync(ct);
            var profile = await ReadCenterAsync(connection, null, centerRef, ct);
            return Results.Ok(new { centerRef, profile = new {
                revision = profile.Revision, displayName = profile.DisplayName, city = profile.City },
                verificationState = "UNVERIFIED_DRAFT" });
        }).RequirePanelCapability(PanelCapability.CenterAccessGrantsManage);

        center.MapPut("/directory-draft", async (string centerRef,
            CenterDirectoryDraftMutation input, HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var name = input.DisplayName?.Trim();
            var city = input.City?.Trim();
            if (!ValidText(name, 140) || !ValidText(city, 100))
                return Results.BadRequest(new { code = "INVALID_CENTER_DRAFT" });
            return await WriteAsync(db, context, centerRef, input.RequestId,
                input.ExpectedRevision, "center-profile", centerRef, name!, city!,
                "CenterAccessGrantsManage", ct);
        }).RequirePanelCapability(PanelCapability.CenterAccessGrantsManage);

        center.MapGet("/doctor-drafts", async (string centerRef, string? after,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            if (after is not null && !ValidRef(after))
                return Results.BadRequest(new { code = "INVALID_DOCTOR_CURSOR" });
            const string sql = """
                SELECT doctor_ref, revision, display_name, specialty
                FROM taranom_center_doctor_draft
                WHERE center_ref=@center AND doctor_ref>@after
                ORDER BY doctor_ref LIMIT 26
                """;
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("center", centerRef);
            command.Parameters.AddWithValue("after", after ?? "");
            var records = new List<object>();
            var refs = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                refs.Add(reader.GetString(0));
                records.Add(new { doctorRef = reader.GetString(0), revision = reader.GetInt64(1),
                    displayName = reader.GetString(2), specialty = reader.GetString(3),
                    verificationState = "UNVERIFIED_DRAFT" });
            }
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { centerRef,
                doctors = records.Take(25).ToArray(),
                nextAfter = records.Count > 25 ? refs[24] : null });
        }).RequirePanelCapability(PanelCapability.CenterRosterManage);

        center.MapPut("/doctor-drafts/{doctorRef}", async (
            string centerRef, string doctorRef, CenterDoctorDraftMutation input,
            HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var name = input.DisplayName?.Trim();
            var specialty = input.Specialty?.Trim();
            if (!ValidRef(doctorRef) || !ValidText(name, 140) || !ValidText(specialty, 120))
                return Results.BadRequest(new { code = "INVALID_DOCTOR_DRAFT" });
            return await WriteAsync(db, context, centerRef, input.RequestId,
                input.ExpectedRevision, "doctor-claim", doctorRef, name!, specialty!,
                "CenterRosterManage", ct);
        }).RequirePanelCapability(PanelCapability.CenterRosterManage);
    }

    private static async Task<IResult> WriteAsync(
        NpgsqlDataSource db, HttpContext context, string centerRef,
        Guid requestId, long expectedRevision, string action, string targetRef,
        string name, string detail, string capability, CancellationToken ct)
    {
        var identity = Identity(context);
        if (identity is null) return Results.Forbid();
        if (!ValidRef(centerRef) || requestId == Guid.Empty || expectedRevision < 0 ||
            expectedRevision == long.MaxValue)
            return Results.BadRequest(new { code = "INVALID_DIRECTORY_DRAFT_COMMAND" });

        context.Response.Headers.CacheControl = "no-store";
        var payload = JsonSerializer.Serialize(new { centerRef, action, targetRef, expectedRevision,
            displayName = name, detail });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var gate = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", connection, transaction))
        {
            gate.Parameters.AddWithValue("key", "directory-draft\n" + centerRef);
            await gate.ExecuteNonQueryAsync(ct);
        }

        // Revocation/permission update takes a row lock. The FOR SHARE lock
        // protects this write until commit and prevents stale grants from
        // crossing the mutation boundary after the endpoint policy check.
        await using (var canonical = new NpgsqlCommand("""
            SELECT 1 FROM taranom_canonical_center WHERE center_ref=@center FOR SHARE
            """, connection, transaction))
        {
            canonical.Parameters.AddWithValue("center", centerRef);
            if (await canonical.ExecuteScalarAsync(ct) is null)
                return Results.NotFound(new {code="CENTER_NOT_ADMIN_DEFINED"});
        }

        await using (var grant = new NpgsqlCommand("""
            SELECT 1 FROM taranom_panel_access_grant
            WHERE issuer=@issuer AND subject=@subject AND scope_ref=@center
              AND capability=@capability AND grant_state='active'
              AND subject_mapping_current
              AND permission_version_current AND grant_not_revoked AND audit_ready
            FOR SHARE
            """, connection, transaction))
        {
            BindIdentity(grant, identity.Value);
            grant.Parameters.AddWithValue("center", centerRef);
            grant.Parameters.AddWithValue("capability", capability);
            if (await grant.ExecuteScalarAsync(ct) is null) return Results.Forbid();
        }

        await using (var replay = new NpgsqlCommand("""
            SELECT action,target_ref,fingerprint,result_revision
            FROM taranom_center_directory_draft_audit
            WHERE center_ref=@center AND actor_issuer=@issuer
              AND actor_subject=@subject AND request_id=@request
            """, connection, transaction))
        {
            BindIdentity(replay, identity.Value);
            replay.Parameters.AddWithValue("center", centerRef);
            replay.Parameters.AddWithValue("request", requestId);
            await using var reader = await replay.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetString(0) != action || reader.GetString(1) != targetRef ||
                    reader.GetString(2) != fingerprint)
                    return Results.Conflict(new { code = "DIRECTORY_DRAFT_REPLAY_CONFLICT" });
                return Results.Ok(new { outcome = "Replayed", revision = reader.GetInt64(3),
                    verificationState = "UNVERIFIED_DRAFT" });
            }
        }

        long currentRevision;
        if (action == "center-profile")
            currentRevision = (await ReadCenterAsync(connection, transaction, centerRef, ct)).Revision;
        else
        {
            await using var read = new NpgsqlCommand("""
                SELECT revision FROM taranom_center_doctor_draft
                WHERE center_ref=@center AND doctor_ref=@doctor
                """, connection, transaction);
            read.Parameters.AddWithValue("center", centerRef);
            read.Parameters.AddWithValue("doctor", targetRef);
            currentRevision = (long?)await read.ExecuteScalarAsync(ct) ?? 0;
        }

        if (currentRevision != expectedRevision)
            return Results.Conflict(new { code = "DIRECTORY_DRAFT_STALE_REVISION",
                revision = currentRevision });

        var revision = checked(currentRevision + 1);
        if (action == "center-profile")
        {
            await using var save = new NpgsqlCommand("""
                INSERT INTO taranom_center_directory_draft
                    (center_ref,revision,display_name,city)
                VALUES (@center,@revision,@name,@detail)
                ON CONFLICT (center_ref) DO UPDATE SET
                    revision=EXCLUDED.revision,display_name=EXCLUDED.display_name,
                    city=EXCLUDED.city,updated_at=now()
                """, connection, transaction);
            BindDraft(save, centerRef, targetRef, revision, name, detail);
            await save.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var save = new NpgsqlCommand("""
                INSERT INTO taranom_center_doctor_draft
                    (center_ref,doctor_ref,revision,display_name,specialty)
                VALUES (@center,@target,@revision,@name,@detail)
                ON CONFLICT (center_ref,doctor_ref) DO UPDATE SET
                    revision=EXCLUDED.revision,display_name=EXCLUDED.display_name,
                    specialty=EXCLUDED.specialty,updated_at=now()
                """, connection, transaction);
            BindDraft(save, centerRef, targetRef, revision, name, detail);
            await save.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO taranom_center_directory_draft_audit
                (center_ref,actor_issuer,actor_subject,request_id,action,target_ref,
                 fingerprint,result_revision)
            VALUES (@center,@issuer,@subject,@request,@action,@target,
                    @fingerprint,@revision)
            """, connection, transaction))
        {
            BindIdentity(audit, identity.Value);
            audit.Parameters.AddWithValue("center", centerRef);
            audit.Parameters.AddWithValue("request", requestId);
            audit.Parameters.AddWithValue("action", action);
            audit.Parameters.AddWithValue("target", targetRef);
            audit.Parameters.AddWithValue("fingerprint", fingerprint);
            audit.Parameters.AddWithValue("revision", revision);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return Results.Ok(new { outcome = "Applied", revision,
            verificationState = "UNVERIFIED_DRAFT" });
    }

    private static void BindDraft(NpgsqlCommand command, string centerRef,
        string targetRef, long revision, string name, string detail)
    {
        command.Parameters.AddWithValue("center", centerRef);
        command.Parameters.AddWithValue("target", targetRef);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("detail", detail);
    }

    private static async Task<(long Revision, string DisplayName, string City)> ReadCenterAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string centerRef, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT revision,display_name,city FROM taranom_center_directory_draft
            WHERE center_ref=@center
            """, connection, transaction);
        command.Parameters.AddWithValue("center", centerRef);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (0, "", "");
        return (reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    private static (string Issuer, string Subject)? Identity(HttpContext context)
    {
        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ||
            issuer.Length > 512 || subject.Length > 256 ||
            issuer.Any(char.IsControl) || subject.Any(char.IsControl))
            return null;
        return (issuer, subject);
    }

    private static void BindIdentity(NpgsqlCommand command, (string Issuer, string Subject) actor)
    {
        command.Parameters.AddWithValue("issuer", actor.Issuer);
        command.Parameters.AddWithValue("subject", actor.Subject);
    }

    private static bool ValidRef(string? text) => ValidText(text, 256);
    private static bool ValidText(string? text, int max) =>
        text is { Length: > 0 } && text.Length <= max &&
        !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl);
}
