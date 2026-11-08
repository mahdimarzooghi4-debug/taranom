using Npgsql;

namespace Taranom.Appointments.Api;

internal static class CanonicalCenterGuard
{
    // Existence only. Clinical qualification/publication is independently
    // governed and must not be inferred from this check.
    internal static async Task<bool> ExistsAsync(
        NpgsqlDataSource dataSource,string centerRef,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(centerRef)||centerRef.Length>256||
           centerRef.Any(char.IsControl))return false;
        await using var connection=await dataSource.OpenConnectionAsync(ct);
        await using var command=new NpgsqlCommand("""
            SELECT 1 FROM taranom_canonical_center WHERE center_ref=@center
            """,connection);
        command.Parameters.AddWithValue("center",centerRef);
        return await command.ExecuteScalarAsync(ct) is not null;
    }
}
