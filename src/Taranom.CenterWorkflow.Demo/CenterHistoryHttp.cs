using System.Globalization;
using Npgsql;
using Taranom.ClinicalCenterGovernance;
using CenterReducer = global::Taranom.ClinicalCenterGovernance.ClinicalCenterJournal;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-074: VERIFIED REPLAY of the existing test-only PostgreSQL journal.
// Never an actual clinical, staff access, professional license or service history.
internal static class CenterHistoryHttp
{
    private const string Contract = "TRN-TECH-074";

    internal static async Task<IResult> ListAsync(HttpContext context)
    {
        var center = context.Request.RouteValues["centerRef"]?.ToString();
        if (!CenterReducer.IsSyntheticRef(center))
            return Denied(400, "INVALID_TEST_REFERENCE");

        var query = context.Request.Query;
        if (query.Count != 2 ||
            !query.TryGetValue("afterVersion", out var afterRaw) ||
            !query.TryGetValue("limit", out var limitRaw) ||
            afterRaw.Count != 1 || limitRaw.Count != 1 ||
            !ParseNonnegative(afterRaw.ToString(), out var after) ||
            !ParseNonnegative(limitRaw.ToString(), out var requestedLimit) ||
            requestedLimit is < 1 or > 20)
            return Denied(400, "INVALID_HISTORY_QUERY");

        try
        {
            // ReadAsync replays the original append-only journal and checks
            // audit digest + relational materialized projections atomically.
            var current = await CenterHttp.Store.ReadAsync(center!);
            if (after > current.Version)
                return Denied(400, "FUTURE_HISTORY_CURSOR");

            var slice = current.Events.Where(x => x.Version > after)
                .Take((int)requestedLimit + 1).ToArray();
            var hasMore = slice.Length > requestedLimit;
            var page = slice.Take((int)requestedLimit).Select(x => new
            {
                version = x.Version,
                kind = x.Command.Kind.ToString(),
                departmentRef = x.Command.DepartmentRef,
                doctorRef = x.Command.DoctorRef,
                // Explicitly do not expose caller-claimed "Confirmed" evidence
                // as if it were an authenticated manager or licensure approval.
                hypotheticalOnly = true
            }).ToArray();
            long? nextAfterVersion = hasMore ? page[^1].version : null;

            return Results.Json(new
            {
                contract = Contract,
                syntheticOnly = true,
                managerAuthenticated = false,
                professionalQualificationVerified = false,
                nationalReleaseAllowed = false,
                centerRef = center,
                currentVersion = current.Version,
                afterVersion = after,
                hasMore,
                nextAfterVersion,
                events = page
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException
            or IOException or OverflowException)
        {
            return Denied(503, "TEST_HISTORY_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> AtVersionAsync(HttpContext context)
    {
        var center = context.Request.RouteValues["centerRef"]?.ToString();
        var versionText = context.Request.RouteValues["version"]?.ToString();
        if (!CenterReducer.IsSyntheticRef(center))
            return Denied(400, "INVALID_TEST_REFERENCE");
        if (context.Request.Query.Count != 0 ||
            !ParseNonnegative(versionText, out var version))
            return Denied(400, "INVALID_HISTORICAL_VERSION");

        try
        {
            var latest = await CenterHttp.Store.ReadAsync(center!);
            if (version > latest.Version)
                return Denied(400, "FUTURE_HISTORICAL_VERSION");
            var past = CenterSnapshot.NewSynthetic(center!);
            foreach (var evt in latest.Events)
            {
                if (evt.Version > version) break;
                var result = CenterReducer.Apply(past, evt.Command);
                if (result.Outcome != CenterOutcome.Applied ||
                    result.Snapshot.Version != evt.Version)
                    return Denied(503, "TEST_HISTORY_UNVERIFIABLE");
                past = result.Snapshot;
            }
            if (past.Version != version)
                return Denied(503, "TEST_HISTORY_UNVERIFIABLE");

            return Results.Json(new
            {
                contract = Contract,
                syntheticOnly = true,
                managerAuthenticated = false,
                professionalQualificationVerified = false,
                nationalReleaseAllowed = false,
                currentVersion = latest.Version,
                asOfVersion = version,
                snapshot = SnapshotDto.From(past, "HISTORICAL_RECONSTRUCTION_TEST_ONLY")
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException
            or IOException or OverflowException)
        {
            return Denied(503, "TEST_HISTORY_UNVERIFIABLE");
        }
    }

    private static bool ParseNonnegative(string? value, out long result)
    {
        result = default;
        if (string.IsNullOrEmpty(value) || value.Length > 19 ||
            !value.All(char.IsAsciiDigit))
            return false;
        return long.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out result);
    }

    private static IResult Denied(int status, string outcome) =>
        Results.Json(new
        {
            contract = Contract,
            outcome,
            syntheticOnly = true,
            managerAuthenticated = false,
            professionalQualificationVerified = false,
            nationalReleaseAllowed = false
        }, statusCode: status);
}
