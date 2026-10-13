using System.Globalization;
using Npgsql;
using Taranom.ClinicalCenterGovernance;
using CenterReducer = Taranom.ClinicalCenterGovernance.ClinicalCenterJournal;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-103: read-only, replayed single-doctor center affiliation and
// department transitions. Only synthetic event/audit-backed claims exist.
internal static class CenterDoctorAffiliationHttp
{
    internal static async Task<IResult> ReadAsync(HttpContext context)
    {
        var center = context.Request.RouteValues["centerRef"]?.ToString();
        var doctor = context.Request.RouteValues["doctorRef"]?.ToString();
        var query = context.Request.Query;
        if (!CenterReducer.IsSyntheticRef(center) ||
            !CenterReducer.IsSyntheticRef(doctor) ||
            query.Count != 4 ||
            !query.ContainsKey("expectedCurrentVersion") ||
            !query.ContainsKey("asOfVersion") ||
            !query.ContainsKey("afterVersion") ||
            !query.ContainsKey("limit") ||
            query["expectedCurrentVersion"].Count != 1 ||
            query["asOfVersion"].Count != 1 ||
            query["afterVersion"].Count != 1 ||
            query["limit"].Count != 1 ||
            !Unsigned(query["expectedCurrentVersion"].ToString(), out var expected) ||
            !Unsigned(query["asOfVersion"].ToString(), out var at) ||
            !Unsigned(query["afterVersion"].ToString(), out var after) ||
            !Unsigned(query["limit"].ToString(), out var limit) ||
            limit is < 1 or > 20 || after > at)
            return Denied(400, "INVALID_TEST_DOCTOR_HISTORY_QUERY");

        try
        {
            // The unchanged PostgreSQL journal verifies every accepted event,
            // SHA digest, paired audit and current relational projection.
            var current = await CenterHttp.Store.ReadAsync(center!);
            if (current.Version != expected)
                return Denied(409, "TEST_DOCTOR_HISTORY_VERSION_CHANGED");
            if (at > current.Version)
                return Denied(400, "INVALID_TEST_DOCTOR_HISTORY_VERSION");

            var past = CenterSnapshot.NewSynthetic(center!);
            foreach (var evt in current.Events)
            {
                if (evt.Version > at) break;
                var replayed = CenterReducer.Apply(past, evt.Command);
                if (replayed.Outcome != CenterOutcome.Applied ||
                    replayed.Snapshot.Version != evt.Version)
                    return Denied(503, "TEST_DOCTOR_HISTORY_UNVERIFIABLE");
                past = replayed.Snapshot;
            }
            if (past.Version != at)
                return Denied(503, "TEST_DOCTOR_HISTORY_UNVERIFIABLE");

            if (!past.Doctors.TryGetValue(doctor!, out var status))
                return Denied(404, "TEST_DOCTOR_NOT_AFFILIATED_AT_VERSION");

            // Do not expose historical command authorization assertions,
            // request IDs or actor IDs. Those are not verified credentials.
            var relevant = past.Events.Where(x =>
                x.Version > after &&
                x.Command.DoctorRef == doctor &&
                x.Command.Kind is (CenterCommandKind.RecordDoctorAffiliation or
                                   CenterCommandKind.AssignDoctorToDepartment or
                                   CenterCommandKind.UnassignDoctorFromDepartment or
                                   CenterCommandKind.EndDoctorAffiliation))
                .Take((int)limit + 1).ToArray();
            var more = relevant.Length > limit;
            var events = relevant.Take((int)limit).Select(x => new
            {
                version = x.Version,
                kind = x.Command.Kind.ToString(),
                departmentRef = x.Command.Kind is (CenterCommandKind.AssignDoctorToDepartment or
                    CenterCommandKind.UnassignDoctorFromDepartment)
                    ? x.Command.DepartmentRef : null,
                hypotheticalOnly = true,
                licenseVerified = false,
                clinicalPrivilegesGranted = false
            }).ToArray();
            var departments = past.DoctorDepartments.TryGetValue(doctor!, out var assigned)
                ? assigned.OrderBy(x => x, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

            // An observed intervening write invalidates the entire
            // source-pinned read. No transaction survives across HTTP reads.
            var latest = await CenterHttp.Store.ReadAsync(center!);
            if (latest.Version != expected)
                return Denied(409, "TEST_DOCTOR_HISTORY_VERSION_CHANGED");

            return Results.Json(new
            {
                contract = "TRN-TECH-103",
                syntheticOnly = true,
                outcome = "UNVERIFIED_DOCTOR_AFFILIATION_HISTORY",
                centerRef = center, doctorRef = doctor,
                currentVersion = expected, asOfVersion = at,
                afterVersion = after,
                affiliation = status.ToString(),
                departmentRefs = departments,
                historyReplayed = true,
                historicalOnly = at < expected,
                events,
                hasMore = more,
                nextAfterVersion = more ? events[^1].version : (long?)null,
                managerAuthenticated = false,
                professionalQualificationVerified = false,
                medicalLicenseVerified = false,
                clinicalAccessGranted = false,
                providerContractVerified = false,
                billingAllowed = false,
                nationalReleaseAllowed = false
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or
                                  IOException or OverflowException)
        {
            return Denied(503, "TEST_DOCTOR_HISTORY_UNVERIFIABLE");
        }
    }

    private static bool Unsigned(string? text, out long number)
    {
        number = 0;
        return text is { Length: > 0 and <= 19 } &&
            (text == "0" || text[0] != '0') &&
            text.All(char.IsAsciiDigit) &&
            long.TryParse(text, NumberStyles.None,
                CultureInfo.InvariantCulture, out number) && number >= 0;
    }

    private static IResult Denied(int code, string outcome) =>
        Results.Json(new
        {
            contract = "TRN-TECH-103",
            syntheticOnly = true, outcome,
            historyReplayed = false,
            managerAuthenticated = false,
            professionalQualificationVerified = false,
            medicalLicenseVerified = false,
            clinicalAccessGranted = false,
            providerContractVerified = false,
            billingAllowed = false,
            nationalReleaseAllowed = false
        }, statusCode: code);
}
