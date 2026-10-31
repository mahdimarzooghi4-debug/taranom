using System.Globalization;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// Local CI-only read boundary. It returns no schedule until an authorized
// adapter is explicitly wired; source assertions never become bookings here.
internal static class AppointmentScheduleHttp
{
    internal static AppointmentScheduleReadService Reader { get; set; } = new(null);

    internal static async Task<IResult> ReadAsync(HttpContext context)
    {
        var centerRef = context.Request.RouteValues["centerRef"]?.ToString();
        if (!TestRef(centerRef) ||
            context.Request.Query.Keys.Any(key =>
                key is not ("departmentRef" or "requestedDoctorRef" or "from" or "until")))
            return Denied();

        var departmentValues = context.Request.Query["departmentRef"];
        var doctorValues = context.Request.Query["requestedDoctorRef"];
        var fromValues = context.Request.Query["from"];
        var untilValues = context.Request.Query["until"];
        if (departmentValues.Count > 1 || doctorValues.Count > 1 ||
            fromValues.Count != 1 || untilValues.Count != 1 ||
            !AppointmentTimestampSyntax.TryParseTimestamp(fromValues[0], out var startsAt) ||
            !AppointmentTimestampSyntax.TryParseTimestamp(untilValues[0], out var endsAt) ||
            endsAt <= startsAt)
            return Denied();

        var departmentRef = departmentValues.Count == 0 ||
            string.IsNullOrEmpty(departmentValues[0]) ? null : departmentValues[0];
        var requestedDoctorRef = doctorValues.Count == 0 ||
            string.IsNullOrEmpty(doctorValues[0]) ? null : doctorValues[0];
        if (departmentRef is not null && !TestRef(departmentRef) ||
            requestedDoctorRef is not null && !TestRef(requestedDoctorRef))
            return Denied();

        var query = new AppointmentScheduleQuery(
            centerRef!, departmentRef, requestedDoctorRef, startsAt, endsAt);
        var result = await Reader.ReadAsync(query, context.RequestAborted);
        return Results.Json(new
        {
            contract = "TRN-TECH-229",
            syntheticOnly = true,
            outcome = result.Outcome.ToString(),
            centerRef,
            departmentRefFilter = departmentRef ?? string.Empty,
            requestedDoctorRefFilter = requestedDoctorRef ?? string.Empty,
            from = startsAt.ToString("O", CultureInfo.InvariantCulture),
            until = endsAt.ToString("O", CultureInfo.InvariantCulture),
            sourceClaims = result.SourceClaimedSlots,
            unverifiedSourceMetadata = result.UnverifiedSourceMetadata,
            sourceAuthenticated = result.SourceAuthenticated,
            slotAvailabilityVerified = result.SlotAvailabilityVerified,
            bookingConfirmed = result.BookingConfirmed,
            clinicalActionAllowed = result.ClinicalActionAllowed,
            nationalReleaseAllowed = false
        });
    }

    private static bool TestRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');

    private static IResult Denied() =>
        Results.Json(new
        {
            contract = "TRN-TECH-229",
            outcome = "INVALID_TEST_QUERY",
            syntheticOnly = true,
            sourceClaims = Array.Empty<AppointmentScheduleSlotClaim>(),
            unverifiedSourceMetadata = (AppointmentScheduleSourceMetadata?)null,
            sourceAuthenticated = false,
            slotAvailabilityVerified = false,
            bookingConfirmed = false,
            clinicalActionAllowed = false,
            nationalReleaseAllowed = false
        }, statusCode: StatusCodes.Status400BadRequest);
}
