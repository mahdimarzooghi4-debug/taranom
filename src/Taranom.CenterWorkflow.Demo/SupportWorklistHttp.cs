using System.Globalization;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-129: local demo only. Exposes test: reference/revision counters,
// never member IDs, notes, evidence, actor, insurer or clinical information.
// This endpoint must NOT be copied into an unauthenticated production API.
internal static class SupportWorklistHttp
{
    internal static async Task<IResult> ListAsync(HttpContext context)
    {
        var q = context.Request.Query;
        if (q.Count is not (2 or 3) || !q.ContainsKey("afterCaseRef") ||
            !q.ContainsKey("limit") || q["afterCaseRef"].Count != 1 ||
            q["limit"].Count != 1 ||
            (q.Count == 3 && (!q.ContainsKey("topic") || q["topic"].Count != 1)))
            return Denied(400, "INVALID_TEST_WORKLIST_CURSOR");
        var after = q["afterCaseRef"].ToString();
        var count = q["limit"].ToString();
        var selected = q.ContainsKey("topic") ? q["topic"].ToString() : "ALL";
        var topic = selected switch
        {
            "ALL" => (SyntheticSupportTopic?)null,
            "MedicalServiceComplaint" => SyntheticSupportTopic.MedicalServiceComplaint,
            "InsuranceCoverageDispute" => SyntheticSupportTopic.InsuranceCoverageDispute,
            _ => (SyntheticSupportTopic?)null
        };
        if (selected is not ("ALL" or "MedicalServiceComplaint" or "InsuranceCoverageDispute"))
            return Denied(400, "INVALID_TEST_WORKLIST_TOPIC");
        if (after.Length > 0 && !PgSyntheticSupportJournal.IsSynthetic(after) ||
            count.Length is < 1 or > 2 ||
            count[0] == '0' || !count.All(char.IsAsciiDigit) ||
            !int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture,
                out var limit) || limit is < 1 or > 10)
            return Denied(400, "INVALID_TEST_WORKLIST_CURSOR");
        try
        {
            var result = await SupportHttp.ReadStore.ReadWorklistAsync(
                after.Length == 0 ? null : after, limit, topic);
            var rows = result.Rows.Select(x => new
            {
                caseRef = x.CaseRef,
                revision = x.Revision,
                followUpCount = x.FollowUps
            }).ToArray();
            return Results.Json(new
            {
                contract = "TRN-TECH-129",
                syntheticOnly = true,
                memberAuthenticated = false,
                complaintLegallySubmitted = false,
                insurerContacted = false,
                clinicalReviewCompleted = false,
                nationalReleaseAllowed = false,
                source = "VERIFIED_ISOLATED_POSTGRES_SUPPORT_JOURNAL",
                afterCaseRef = after,
                topic = selected,
                limit,
                hasMore = result.HasMore,
                nextAfterCaseRef = result.HasMore ? rows[^1].caseRef : null,
                cases = rows
            });
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or
            IOException or OverflowException)
        {
            return Denied(503, "TEST_WORKLIST_UNVERIFIABLE");
        }
    }

    private static IResult Denied(int status, string outcome) => Results.Json(new
    {
        contract = "TRN-TECH-129",
        outcome,
        syntheticOnly = true,
        memberAuthenticated = false,
        complaintLegallySubmitted = false,
        insurerContacted = false,
        clinicalReviewCompleted = false,
        nationalReleaseAllowed = false,
        cases = Array.Empty<object>()
    }, statusCode: status);
}
