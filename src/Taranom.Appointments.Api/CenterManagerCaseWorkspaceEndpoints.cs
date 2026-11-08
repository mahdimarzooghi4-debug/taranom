using Npgsql;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;

namespace Taranom.Appointments.Api;

/// <summary>
/// Admin-only case workspace: aggregates submitted claims and canonical center
/// data. This view has no transition or grant side effects, and is NOT a
/// registry of verified legal/organizational authority.
/// </summary>
internal static class CenterManagerCaseWorkspaceEndpoints
{
    internal static void MapCenterManagerCaseWorkspaceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/operations/center-manager-requests/{requestId:guid}/workspace",
            ReadWorkspaceAsync).RequirePanelCapability(PanelCapability.AdminCenterRegistryManage);
    }

    private static async Task<IResult> ReadWorkspaceAsync(
        Guid requestId, HttpContext context, NpgsqlDataSource db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (requestId == Guid.Empty)
            return Results.BadRequest(new { code = "INVALID_MANAGER_CASE_REF" });

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT r.center_ref, c.display_name, c.city,
                c.defined_by_issuer, c.defined_by_subject,
                r.issuer, r.subject, r.reason, r.created_at,
                review.decision, review.reason, review.reviewer_issuer,
                review.reviewer_subject, review.reviewed_at,
                (SELECT count(*) FROM taranom_center_manager_request competing
                 WHERE competing.center_ref=r.center_ref AND
                       competing.request_id<>r.request_id) AS other_applicant_claims
            FROM taranom_center_manager_request AS r
            INNER JOIN taranom_canonical_center AS c ON c.center_ref=r.center_ref
            LEFT JOIN taranom_center_manager_request_review AS review
                ON review.request_id=r.request_id
            WHERE r.request_id=@request
            """, conn);
        cmd.Parameters.AddWithValue("request", requestId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return Results.NotFound(new { code = "MANAGER_CASE_NOT_FOUND" });

        return Results.Ok(new {
            requestId,
            center = new {
                centerRef = reader.GetString(0),
                displayName = reader.GetString(1),
                city = reader.GetString(2),
                definedByIssuer = reader.GetString(3),
                definedBySubject = reader.GetString(4),
                recordState = "ADMIN_DEFINED_UNVERIFIED",
                licensed = false,
                published = false
            },
            applicant = new {
                issuer = reader.GetString(5),
                subject = reader.GetString(6),
                submittedReason = reader.GetString(7),
                submittedAt = reader.GetFieldValue<DateTimeOffset>(8)
            },
            review = new {
                state = reader.IsDBNull(9) ? "PENDING_REVIEW" : reader.GetString(9),
                reason = reader.IsDBNull(10) ? null : reader.GetString(10),
                reviewerIssuer = reader.IsDBNull(11) ? null : reader.GetString(11),
                reviewerSubject = reader.IsDBNull(12) ? null : reader.GetString(12),
                reviewedAt = reader.IsDBNull(13)
                    ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(13)
            },
            otherApplicantClaims = reader.GetInt64(14),
            representationEvidence = new {
                sourceStatus = "NOT_CONNECTED",
                authorityVerified = false,
                criteriaDefined = false
            },
            associationGateRequired = true,
            managerAssignedByThisWorkflow = false
        });
    }
}
