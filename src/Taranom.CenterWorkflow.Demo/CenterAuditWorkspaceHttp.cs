using System.Globalization;
using Npgsql;
using Taranom.ClinicalCenterGovernance;
using CenterReducer = Taranom.ClinicalCenterGovernance.ClinicalCenterJournal;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-104: one source-attested snapshot produces BOTH the historical
// center state and its bounded chronological events, unlike two independent
// HTTP reads. Synthetic, strictly read-only and never a clinical audit.
internal static class CenterAuditWorkspaceHttp
{
    internal static async Task<IResult> ReadAsync(HttpContext ctx)
    {
        var center = ctx.Request.RouteValues["centerRef"]?.ToString();
        var q = ctx.Request.Query;
        if (!CenterReducer.IsSyntheticRef(center) ||
            q.Count != 4 ||
            !q.ContainsKey("expectedCurrentVersion") ||
            !q.ContainsKey("asOfVersion") ||
            !q.ContainsKey("afterVersion") ||
            !q.ContainsKey("limit") ||
            q["expectedCurrentVersion"].Count != 1 ||
            q["asOfVersion"].Count != 1 ||
            q["afterVersion"].Count != 1 ||
            q["limit"].Count != 1 ||
            !Unsigned(q["expectedCurrentVersion"].ToString(), out var expected) ||
            !Unsigned(q["asOfVersion"].ToString(), out var asOf) ||
            !Unsigned(q["afterVersion"].ToString(), out var after) ||
            !Unsigned(q["limit"].ToString(), out var limit) ||
            limit is < 1 or > 20 || after > asOf)
            return Denied(400, "INVALID_TEST_CENTER_AUDIT_QUERY");

        try
        {
            // The existing PostgreSQL owner attests every event and paired
            // audit plus the current relational projection at this read.
            var current = await CenterHttp.Store.ReadAsync(center!);
            if (current.Version != expected)
                return Denied(409, "TEST_CENTER_AUDIT_VERSION_CHANGED");
            if (asOf > expected)
                return Denied(400, "INVALID_TEST_CENTER_AUDIT_VERSION");

            // Historical state is reconstructed ONLY through the original
            // center domain reducer and exact original persisted commands.
            var past = CenterSnapshot.NewSynthetic(center!);
            foreach (var evt in current.Events)
            {
                if (evt.Version > asOf) break;
                var reduced = CenterReducer.Apply(past, evt.Command);
                if (reduced.Outcome != CenterOutcome.Applied ||
                    reduced.Snapshot.Version != evt.Version)
                    return Denied(503, "TEST_CENTER_AUDIT_UNVERIFIABLE");
                past = reduced.Snapshot;
            }
            if (past.Version != asOf)
                return Denied(503, "TEST_CENTER_AUDIT_UNVERIFIABLE");

            // Events and as-of snapshot are from ONE already reattested
            // PostgreSQL read; a second read only checks observed drift.
            var slice = past.Events.Where(x => x.Version > after)
                .Take((int)limit + 1).ToArray();
            var more = slice.Length > limit;
            var events = slice.Take((int)limit).Select(x => new
            {
                version = x.Version,
                kind = x.Command.Kind.ToString(),
                departmentRef = x.Command.DepartmentRef,
                doctorRef = x.Command.DoctorRef,
                hypotheticalOnly = true,
                professionalQualificationVerified = false,
                clinicalPrivilegesGranted = false
            }).ToArray();

            var checkedCurrent = await CenterHttp.Store.ReadAsync(center!);
            if (checkedCurrent.Version != expected)
                return Denied(409, "TEST_CENTER_AUDIT_VERSION_CHANGED");

            return Results.Json(new
            {
                contract = "TRN-TECH-104",
                syntheticOnly = true,
                outcome = "VERSION_PINNED_CENTER_AUDIT_WORKSPACE",
                centerRef = center,
                currentVersion = expected,
                asOfVersion = asOf,
                afterVersion = after,
                snapshot = SnapshotDto.From(past, "HISTORICAL_RECONSTRUCTION_TEST_ONLY"),
                events,
                hasMore = more,
                nextAfterVersion = more ? events[^1].version : (long?)null,
                sourceReplayed = true,
                sameSourceSnapshot = true,
                durableCrossRequestSnapshot = false,
                managerAuthenticated = false,
                professionalQualificationVerified = false,
                clinicalAccessGranted = false,
                providerContractVerified = false,
                billingAllowed = false,
                nationalReleaseAllowed = false
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException
                                  or IOException or OverflowException)
        {
            return Denied(503, "TEST_CENTER_AUDIT_UNVERIFIABLE");
        }
    }

    private static bool Unsigned(string? text, out long value)
    {
        value = 0;
        return text is { Length: > 0 and <= 19 } &&
            (text == "0" || text[0] != '0') &&
            text.All(char.IsAsciiDigit) &&
            long.TryParse(text, NumberStyles.None,
                CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static IResult Denied(int status, string outcome) =>
        Results.Json(new
        {
            contract = "TRN-TECH-104",
            syntheticOnly = true,
            outcome,
            sourceReplayed = false,
            sameSourceSnapshot = false,
            durableCrossRequestSnapshot = false,
            managerAuthenticated = false,
            professionalQualificationVerified = false,
            clinicalAccessGranted = false,
            providerContractVerified = false,
            billingAllowed = false,
            nationalReleaseAllowed = false
        }, statusCode: status);
}
