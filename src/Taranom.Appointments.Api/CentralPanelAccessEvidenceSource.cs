using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;
using Npgsql;

namespace Taranom.Appointments.Api;

internal sealed class CentralPanelAccessEvidenceSource(NpgsqlDataSource dataSource)
    : IPanelAccessEvidenceSource
{
    public async ValueTask<PanelAccessEvidence?> ResolveAsync(
        ClaimsPrincipal principal,
        PanelCapability capability,
        object? authorizationResource)
    {
        if (authorizationResource is not HttpContext httpContext)
            return null;
        var issuer = principal.FindFirst("iss")?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        var capabilityToken = PanelAccessPolicy.GetCapabilityToken(capability);
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ||
            capabilityToken is null)
            return null;

        string? scopeHint = null;
        string? scopeRef;
        if (capability is PanelCapability.CenterAppointmentsManage or
            PanelCapability.CenterAccessGrantsManage or PanelCapability.CenterRosterManage)
        {
            scopeHint = httpContext.Request.RouteValues["centerRef"]?.ToString();
            scopeRef = scopeHint;
            if (string.IsNullOrWhiteSpace(scopeRef))
                return null;
        }
        else if (capability == PanelCapability.MemberAppointmentsManage)
        {
            scopeHint = httpContext.Request.RouteValues["memberRef"]?.ToString();
            scopeRef = scopeHint;
            if (string.IsNullOrWhiteSpace(scopeRef))
                return null;
        }
        else if (capability == PanelCapability.SupportRequestsManage)
        {
            scopeRef = "taranom-support";
        }
        else if (capability == PanelCapability.AdminCenterRegistryManage)
        {
            // Fixed server-owned singleton scope; never taken from request JSON,
            // route, role claims or a customer-supplied center reference.
            scopeRef = "taranom-center-registry";
        }
        else
        {
            return null;
        }

        const string sql = """
            SELECT scope_ref, subject_mapping_current,
                   permission_version_current, grant_not_revoked, audit_ready
            FROM taranom_panel_access_grant
            WHERE issuer = @issuer AND subject = @subject
              AND capability = @capability AND grant_state = 'active'
              AND (@scope_ref IS NULL OR scope_ref = @scope_ref)
            ORDER BY scope_ref
            LIMIT 2
            """;
        await using var connection = await dataSource.OpenConnectionAsync(httpContext.RequestAborted);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("capability", capabilityToken);
        command.Parameters.AddWithValue("scope_ref", (object?)scopeRef ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(httpContext.RequestAborted);
        if (!await reader.ReadAsync(httpContext.RequestAborted))
            return null;

        var resolvedScope = reader.GetString(0);
        var freshness = new PanelAccessFreshness(
            PanelEvidenceState.Confirmed,
            ReadEvidence(reader, 1), ReadEvidence(reader, 2),
            ReadEvidence(reader, 3), ReadEvidence(reader, 4));
        if (await reader.ReadAsync(httpContext.RequestAborted))
            return null;

        return new PanelAccessEvidence(freshness,
            new PanelScopeContext(resolvedScope, scopeHint, resolvedScope));
    }

    private static PanelEvidenceState ReadEvidence(NpgsqlDataReader reader, int ordinal) =>
        reader.GetBoolean(ordinal) ? PanelEvidenceState.Confirmed : PanelEvidenceState.Missing;
}

internal sealed class AppointmentMemberScopeResolver(NpgsqlDataSource dataSource)
{
    internal async Task<string?> ResolveAsync(
        ClaimsPrincipal principal,
        string memberRef,
        CancellationToken cancellationToken)
    {
        var issuer = principal.FindFirst("iss")?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            return null;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT scope_ref
            FROM taranom_panel_access_grant
            WHERE issuer = @issuer AND subject = @subject
              AND capability = 'MemberAppointmentsManage'
              AND scope_ref = @member_ref
              AND grant_state = 'active'
              AND subject_mapping_current
              AND permission_version_current AND grant_not_revoked AND audit_ready
            ORDER BY scope_ref
            LIMIT 2
            """, connection);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("member_ref", memberRef);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return reader.GetString(0);
    }
}
