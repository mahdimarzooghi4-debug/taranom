using System.Globalization;
using Npgsql;
using Taranom.ClinicalCenterGovernance;
using CenterReducer = Taranom.ClinicalCenterGovernance.ClinicalCenterJournal;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-102 — reattested, version-pinned synthetic department roster.
// No authenticated manager, clinician qualification or patient access.
internal static class CenterDepartmentRosterHttp
{
    internal static async Task<IResult> ReadAsync(HttpContext context)
    {
        var center = context.Request.RouteValues["centerRef"]?.ToString();
        var department = context.Request.RouteValues["departmentRef"]?.ToString();
        var q = context.Request.Query;
        if (!CenterReducer.IsSyntheticRef(center) ||
            !CenterReducer.IsSyntheticRef(department) ||
            q.Count != 2 || !q.ContainsKey("expectedCurrentVersion") ||
            !q.ContainsKey("asOfVersion") ||
            q["expectedCurrentVersion"].Count != 1 ||
            q["asOfVersion"].Count != 1 ||
            !Unsigned(q["expectedCurrentVersion"].ToString(), out var expected) ||
            !Unsigned(q["asOfVersion"].ToString(), out var at))
            return Denied(400, "INVALID_TEST_ROSTER_QUERY");

        try
        {
            // Rehydrate and check original immutable events, SQL projection,
            // per-event digest and paired audit in existing PostgreSQL journal.
            var current = await CenterHttp.Store.ReadAsync(center!);
            if (current.Version != expected)
                return Denied(409, "TEST_CENTER_ROSTER_VERSION_CHANGED");
            if (at > expected)
                return Denied(400, "INVALID_TEST_ROSTER_VERSION");

            var past = CenterSnapshot.NewSynthetic(center!);
            foreach (var evt in current.Events)
            {
                if (evt.Version > at) break;
                var applied = CenterReducer.Apply(past, evt.Command);
                if (applied.Outcome != CenterOutcome.Applied ||
                    applied.Snapshot.Version != evt.Version)
                    return Denied(503, "TEST_CENTER_ROSTER_UNVERIFIABLE");
                past = applied.Snapshot;
            }
            if (past.Version != at)
                return Denied(503, "TEST_CENTER_ROSTER_UNVERIFIABLE");
            if (!past.Departments.Contains(department!))
                return Denied(404, "TEST_DEPARTMENT_NOT_PRESENT_AT_VERSION");

            // Only affiliated, currently assigned synthetic test doctors at
            // this exact historical prefix. Ended affiliations cannot linger.
            var doctors = past.Doctors
                .Where(k => k.Value == DoctorAffiliation.PendingIndependentVerification &&
                    past.DoctorDepartments.TryGetValue(k.Key, out var deps) &&
                    deps.Contains(department!))
                .OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => new
                {
                    doctorRef = k.Key,
                    affiliation = "PendingIndependentVerification",
                    assignmentUnverified = true,
                    clinicalPrivilegesGranted = false,
                    licensureVerified = false
                }).ToArray();

            // A second independent read detects observed source advancement;
            // no false claim of durable snapshot pinning across requests.
            var latest = await CenterHttp.Store.ReadAsync(center!);
            if (latest.Version != expected)
                return Denied(409, "TEST_CENTER_ROSTER_VERSION_CHANGED");
            return Results.Json(new
            {
                contract = "TRN-TECH-102",
                syntheticOnly = true,
                outcome = "UNVERIFIED_DEPARTMENT_ROSTER",
                centerRef = center,
                departmentRef = department,
                currentVersion = expected,
                asOfVersion = at,
                doctorCount = doctors.Length,
                doctors,
                historyReplayed = true,
                historicalOnly = at < expected,
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
            return Denied(503, "TEST_CENTER_ROSTER_UNVERIFIABLE");
        }
    }

    private static bool Unsigned(string? value, out long parsed)
    {
        parsed = 0;
        return value is { Length: > 0 and <= 19 } &&
            (value == "0" || value[0] != '0') &&
            value.All(char.IsAsciiDigit) &&
            long.TryParse(value, NumberStyles.None,
                CultureInfo.InvariantCulture, out parsed) && parsed >= 0;
    }

    private static IResult Denied(int status, string outcome) =>
        Results.Json(new
        {
            contract = "TRN-TECH-102", syntheticOnly = true, outcome,
            historyReplayed = false, managerAuthenticated = false,
            professionalQualificationVerified = false,
            clinicalAccessGranted = false, providerContractVerified = false,
            billingAllowed = false, nationalReleaseAllowed = false
        }, statusCode: status);
}
