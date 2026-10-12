using System.Globalization;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// Read-only history derived solely from the same verified append-only test journal.
// Synthetic refs are not member identity, professional or insurer authority.
internal static class SupportHistoryHttp
{
    internal static async Task<IResult> ListAsync(HttpContext context)
    {
        var caseRef = context.Request.RouteValues["caseRef"]?.ToString();
        var query = context.Request.Query;
        if (!PgSyntheticSupportJournal.IsSynthetic(caseRef) || query.Count != 2 ||
            !query.ContainsKey("afterRevision") || !query.ContainsKey("limit") ||
            query["afterRevision"].Count != 1 || query["limit"].Count != 1 ||
            !ParseUnsigned(query["afterRevision"].ToString(), out var cursor) ||
            !ParseUnsigned(query["limit"].ToString(), out var count) ||
            count is < 1 or > 20)
            return Denied(400, "INVALID_TEST_HISTORY_CURSOR");
        try
        {
            var verified = await SupportHttp.Store.ReadAsync(caseRef!);
            if (cursor > verified.Revision)
                return Denied(400, "INVALID_TEST_HISTORY_CURSOR");
            var page = verified.History.Where(x => x.Revision > cursor)
                .Take((int)count + 1).ToArray();
            var hasMore = page.Length > count;
            var events = page.Take((int)count).Select(x => new
            {
                revision = x.Revision,
                action = x.Command.Action == SyntheticSupportAction.SubmitUnverified
                    ? "SUBMIT_UNVERIFIED" : "FOLLOW_UP_UNVERIFIED",
                noteRef = x.Command.UnverifiedFollowUpNoteRef,
                hypotheticalOnly = true
            }).ToArray();
            return Results.Json(new
            {
                contract = "TRN-TECH-084",
                syntheticOnly = true,
                memberAuthenticated = false,
                complaintLegallySubmitted = false,
                insurerContacted = false,
                clinicalReviewCompleted = false,
                nationalReleaseAllowed = false,
                caseRef,
                currentRevision = verified.Revision,
                afterRevision = cursor,
                hasMore,
                nextAfterRevision = hasMore ? events[^1].revision : (long?)null,
                events
            });
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or
            IOException or OverflowException)
        {
            return Denied(503, "TEST_SUPPORT_HISTORY_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> AtVersionAsync(HttpContext context)
    {
        var caseRef = context.Request.RouteValues["caseRef"]?.ToString();
        var requested = context.Request.RouteValues["revision"]?.ToString();
        if (!PgSyntheticSupportJournal.IsSynthetic(caseRef) ||
            context.Request.Query.Count != 0 ||
            !ParseUnsigned(requested, out var at))
            return Denied(400, "INVALID_TEST_HISTORY_REVISION");
        try
        {
            var current = await SupportHttp.Store.ReadAsync(caseRef!);
            if (at > current.Revision)
                return Denied(400, "INVALID_TEST_HISTORY_REVISION");
            var past = SyntheticSupportSnapshot.Empty;
            foreach (var entry in current.History)
            {
                if (entry.Revision > at) break;
                var applied = SyntheticSupportIntake.Apply(past, entry.Command);
                if (applied.Outcome != SyntheticSupportOutcome.RecordedUnverified ||
                    applied.Snapshot.Revision != entry.Revision)
                    return Denied(503, "TEST_SUPPORT_HISTORY_UNVERIFIABLE");
                past = applied.Snapshot;
            }
            if (past.Revision != at)
                return Denied(503, "TEST_SUPPORT_HISTORY_UNVERIFIABLE");
            return Results.Json(new
            {
                contract = "TRN-TECH-084",
                syntheticOnly = true,
                memberAuthenticated = false,
                complaintLegallySubmitted = false,
                insurerContacted = false,
                clinicalReviewCompleted = false,
                nationalReleaseAllowed = false,
                caseRef,
                currentRevision = current.Revision,
                asOfRevision = at,
                snapshot = SupportHttp.ToResponse(past, caseRef!, "HISTORICAL_READ")
            });
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or
            IOException or OverflowException)
        {
            return Denied(503, "TEST_SUPPORT_HISTORY_UNVERIFIABLE");
        }
    }

    private static bool ParseUnsigned(string? value, out long result)
    {
        result = 0;
        return value is { Length: > 0 and <= 19 } &&
            (value == "0" || value[0] != '0') && value.All(char.IsAsciiDigit) &&
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                out result) && result >= 0;
    }

    private static IResult Denied(int status, string outcome) => Results.Json(new
    {
        contract = "TRN-TECH-084",
        outcome,
        syntheticOnly = true,
        memberAuthenticated = false,
        complaintLegallySubmitted = false,
        insurerContacted = false,
        clinicalReviewCompleted = false,
        nationalReleaseAllowed = false
    }, statusCode: status);
}
