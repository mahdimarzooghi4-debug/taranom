using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;

namespace Taranom.Appointments.Api;

// A canonical record is an Admin-defined directory identity only.
// It does not convey any license, approval, clinician authority or publication.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminCreateCenterCommand(Guid RequestId,string DisplayName,string City);

internal static class AdminCenterRegistryEndpoints
{
    private const string Marker = "ADMIN_DEFINED_UNVERIFIED";

    internal static void MapAdminCenterRegistryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/operations/centers", async (
            string? after, HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            if(after is not null && !ValidRef(after))
                return Results.BadRequest(new {code="INVALID_CENTER_CURSOR"});
            context.Response.Headers.CacheControl="no-store";
            await using var conn=await db.OpenConnectionAsync(ct);
            await using var cmd=new NpgsqlCommand("""
                SELECT center_ref,display_name,city FROM taranom_canonical_center
                WHERE center_ref>@after ORDER BY center_ref LIMIT 21
                """,conn);
            cmd.Parameters.AddWithValue("after",after??"");
            var rows=new List<object>();
            var refs=new List<string>();
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))
            {
                var centerRef=r.GetString(0);
                refs.Add(centerRef);
                rows.Add(new {centerRef,displayName=r.GetString(1),city=r.GetString(2),
                    recordState=Marker,licensed=false,published=false,managerAssigned=false});
            }
            return Results.Ok(new {centers=rows.Take(20).ToArray(),
                nextAfter=rows.Count>20?refs[19]:null});
        }).RequirePanelCapability(PanelCapability.AdminCenterRegistryManage);

        // Recover a lost create response by inspecting only this actor's
        // transaction receipt. It never creates a center on GET.
        app.MapGet("/api/v1/operations/centers/creation-requests/{requestId:guid}", async (
            Guid requestId,HttpContext context,NpgsqlDataSource db,CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl="no-store";
            if(requestId==Guid.Empty)return Results.BadRequest();
            var issuer=context.User.FindFirst("iss")?.Value;
            var subject=context.User.FindFirst("sub")?.Value;
            await using var conn=await db.OpenConnectionAsync(ct);
            await using var cmd=new NpgsqlCommand("""
                SELECT center_ref FROM taranom_canonical_center_creation_receipt
                WHERE issuer=@issuer AND subject=@subject AND request_id=@request
                """,conn);
            cmd.Parameters.AddWithValue("issuer",issuer!);
            cmd.Parameters.AddWithValue("subject",subject!);
            cmd.Parameters.AddWithValue("request",requestId);
            var found=await cmd.ExecuteScalarAsync(ct) as string;
            if(found is null)return Results.NotFound(new {code="CENTER_CREATION_NOT_OBSERVED"});
            return Results.Ok(new {outcome="Observed",centerRef=found,recordState=Marker});
        }).RequirePanelCapability(PanelCapability.AdminCenterRegistryManage);

        app.MapPost("/api/v1/operations/centers", async (
            AdminCreateCenterCommand input, HttpContext context,
            NpgsqlDataSource db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl="no-store";
            var name=input.DisplayName?.Trim();
            var city=input.City?.Trim();
            if(input.RequestId==Guid.Empty||!ValidText(name,140)||!ValidText(city,100))
                return Results.BadRequest(new {code="INVALID_CENTER_DEFINITION"});
            var issuer=context.User.FindFirst("iss")?.Value;
            var subject=context.User.FindFirst("sub")?.Value;
            if(!ValidText(issuer,512)||!ValidText(subject,256))
                return Results.Forbid();
            var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new {displayName=name,city}))));
            await using var conn=await db.OpenConnectionAsync(ct);
            await using var tx=await conn.BeginTransactionAsync(ct);
            await using(var serial=new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",conn,tx))
            {
                serial.Parameters.AddWithValue("key",JsonSerializer.Serialize(
                    new {issuer,subject,input.RequestId}));
                await serial.ExecuteNonQueryAsync(ct);
            }

            // Authorization middleware checked current central authority on
            // entry. A held row lock rechecks and orders revocation vs commit.
            await using(var grant=new NpgsqlCommand("""
                SELECT 1 FROM taranom_panel_access_grant
                WHERE issuer=@issuer AND subject=@subject AND
                    capability='AdminCenterRegistryManage' AND
                    scope_ref='taranom-center-registry' AND
                    grant_state='active' AND subject_mapping_current AND
                    permission_version_current AND grant_not_revoked AND audit_ready
                FOR SHARE
                """,conn,tx))
            {
                grant.Parameters.AddWithValue("issuer",issuer!);
                grant.Parameters.AddWithValue("subject",subject!);
                if(await grant.ExecuteScalarAsync(ct) is null)
                    return Results.Forbid();
            }

            await using(var prior=new NpgsqlCommand("""
                SELECT fingerprint,center_ref FROM taranom_canonical_center_creation_receipt
                WHERE issuer=@issuer AND subject=@subject AND request_id=@request
                """,conn,tx))
            {
                prior.Parameters.AddWithValue("issuer",issuer!);
                prior.Parameters.AddWithValue("subject",subject!);
                prior.Parameters.AddWithValue("request",input.RequestId);
                await using var reader=await prior.ExecuteReaderAsync(ct);
                if(await reader.ReadAsync(ct))
                {
                    if(reader.GetString(0)!=fingerprint)
                        return Results.Conflict(new {code="CENTER_CREATE_REPLAY_CONFLICT"});
                    return Results.Ok(new {outcome="Replayed",centerRef=reader.GetString(1),
                        displayName=name,city,recordState=Marker,licensed=false,
                        published=false,managerAssigned=false});
                }
            }
            // Server-only opaque center identity, independent of user input and
            // physician IDs; a center cannot self-generate or reserve it.
            var centerRef="center-"+Guid.NewGuid().ToString("N");
            await using(var create=new NpgsqlCommand("""
                INSERT INTO taranom_canonical_center
                    (center_ref,display_name,city,defined_by_issuer,defined_by_subject)
                VALUES (@center,@name,@city,@issuer,@subject)
                """,conn,tx))
            {
                create.Parameters.AddWithValue("center",centerRef);
                create.Parameters.AddWithValue("name",name!);
                create.Parameters.AddWithValue("city",city!);
                create.Parameters.AddWithValue("issuer",issuer!);
                create.Parameters.AddWithValue("subject",subject!);
                await create.ExecuteNonQueryAsync(ct);
            }
            await using(var receipt=new NpgsqlCommand("""
                INSERT INTO taranom_canonical_center_creation_receipt
                    (issuer,subject,request_id,fingerprint,center_ref)
                VALUES (@issuer,@subject,@request,@fingerprint,@center)
                """,conn,tx))
            {
                receipt.Parameters.AddWithValue("issuer",issuer!);
                receipt.Parameters.AddWithValue("subject",subject!);
                receipt.Parameters.AddWithValue("request",input.RequestId);
                receipt.Parameters.AddWithValue("fingerprint",fingerprint);
                receipt.Parameters.AddWithValue("center",centerRef);
                await receipt.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new {outcome="Applied",centerRef,displayName=name,city,
                recordState=Marker,licensed=false,published=false,managerAssigned=false});
        }).RequirePanelCapability(PanelCapability.AdminCenterRegistryManage);
    }

    private static bool ValidText(string? value,int maximum)=> value is {Length:>0} &&
        value.Length<=maximum && !string.IsNullOrWhiteSpace(value) &&
        !value.Any(char.IsControl);
    private static bool ValidRef(string? value)=>ValidText(value,256);
}
