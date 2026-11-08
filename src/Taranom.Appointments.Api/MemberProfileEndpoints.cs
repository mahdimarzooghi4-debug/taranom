using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Npgsql;

namespace Taranom.Appointments.Api;

public sealed record MemberProfileView(long Revision, string FullName, DateOnly? BirthDate, string City);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemberProfileMutation(Guid RequestId, long ExpectedRevision, string FullName, DateOnly? BirthDate, string City);

internal static class MemberProfileEndpoints
{
    internal static void MapMemberProfileEndpoints(this WebApplication app, string policy)
    {
        app.MapGet("/api/v1/me/profile", async (HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var identity = Identity(context);
            if (identity is null) return Results.Forbid();
            context.Response.Headers.CacheControl = "no-store";
            await using var connection = await db.OpenConnectionAsync(ct);
            var profile = await ReadAsync(connection, null, identity.Value, ct);
            return Results.Ok(new { profile });
        }).RequireAuthorization(policy);

        app.MapPut("/api/v1/me/profile", async (MemberProfileMutation input, HttpContext context, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var identity = Identity(context);
            if (identity is null) return Results.Forbid();
            context.Response.Headers.CacheControl = "no-store";
            var fullName = input.FullName?.Trim();
            var city = input.City?.Trim();
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(210));
            if (input.RequestId == Guid.Empty || input.ExpectedRevision < 0 ||
                string.IsNullOrWhiteSpace(fullName) || fullName.Length > 140 || fullName.Any(char.IsControl) ||
                city is null || city.Length > 100 || city.Any(char.IsControl) ||
                input.BirthDate is { } birth && (birth < new DateOnly(1900, 1, 1) || birth > today))
                return Results.BadRequest(new { code = "INVALID_PROFILE" });
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                input.ExpectedRevision, FullName = fullName, input.BirthDate, City = city
            });
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@identity, 0))", connection, transaction))
            {
                gate.Parameters.AddWithValue("identity", $"member-profile\n{identity.Value.Issuer}\n{identity.Value.Subject}");
                await gate.ExecuteNonQueryAsync(ct);
            }
            await using (var replay = new NpgsqlCommand("""
                SELECT fingerprint, result_revision FROM taranom_member_profile_audit
                WHERE issuer=@issuer AND subject=@subject AND request_id=@request
                """, connection, transaction))
            {
                BindIdentity(replay, identity.Value);
                replay.Parameters.AddWithValue("request", input.RequestId);
                await using var reader = await replay.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    if (!string.Equals(reader.GetString(0), fingerprint, StringComparison.Ordinal))
                        return Results.Conflict(new { code = "PROFILE_REQUEST_CONFLICT" });
                    var revision = reader.GetInt64(1);
                    return Results.Ok(new { outcome = "Replayed", revision });
                }
            }
            var current = await ReadAsync(connection, transaction, identity.Value, ct);
            if (current.Revision != input.ExpectedRevision)
                return Results.Conflict(new { code = "PROFILE_STALE_REVISION", revision = current.Revision });
            var nextRevision = checked(current.Revision + 1);
            await using (var save = new NpgsqlCommand("""
                INSERT INTO taranom_member_profile (issuer,subject,revision,full_name,birth_date,city)
                VALUES (@issuer,@subject,@revision,@name,@birth,@city)
                ON CONFLICT (issuer,subject) DO UPDATE SET revision=EXCLUDED.revision,
                    full_name=EXCLUDED.full_name,birth_date=EXCLUDED.birth_date,city=EXCLUDED.city
                """, connection, transaction))
            {
                BindIdentity(save, identity.Value);
                save.Parameters.AddWithValue("revision", nextRevision);
                save.Parameters.AddWithValue("name", fullName);
                save.Parameters.AddWithValue("birth", NpgsqlTypes.NpgsqlDbType.Date, (object?)input.BirthDate ?? DBNull.Value);
                save.Parameters.AddWithValue("city", city);
                await save.ExecuteNonQueryAsync(ct);
            }
            await using (var audit = new NpgsqlCommand("""
                INSERT INTO taranom_member_profile_audit (issuer,subject,request_id,fingerprint,result_revision)
                VALUES (@issuer,@subject,@request,@fingerprint,@revision)
                """, connection, transaction))
            {
                BindIdentity(audit, identity.Value);
                audit.Parameters.AddWithValue("request", input.RequestId);
                audit.Parameters.AddWithValue("fingerprint", fingerprint);
                audit.Parameters.AddWithValue("revision", nextRevision);
                await audit.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return Results.Ok(new { outcome = "Applied", revision = nextRevision });
        }).RequireAuthorization(policy);
    }

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

    private static async Task<MemberProfileView> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        (string Issuer, string Subject) identity, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT revision,full_name,birth_date,city FROM taranom_member_profile
            WHERE issuer=@issuer AND subject=@subject
            """, connection, transaction);
        BindIdentity(command, identity);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new MemberProfileView(0, "", null, "");
        return new MemberProfileView(reader.GetInt64(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2), reader.GetString(3));
    }
}
