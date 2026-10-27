using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Http.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// Local CI-only synthetic endpoint. It never authenticates actors, reserves a
// slot, confirms a visit, or performs a clinical action.
internal static class SyntheticAppointmentHttp
{
    internal static PgSyntheticAppointmentRequestJournal Store { get; set; } = null!;
    private static readonly string[] Fields =
    [
        "requestId", "expectedRevision", "memberRef", "centerRef",
        "departmentRef", "requestedDoctorRef", "slotRef"
    ];
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    internal static async Task<IResult> WriteAsync(HttpContext context)
    {
        if (!ValidRef(context.Request.RouteValues["requestRef"]?.ToString()))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_REFERENCE");
        if (!AllowedOrigin(context.Request.Headers.Origin.ToString()))
            return Denied(StatusCodes.Status403Forbidden, "ORIGIN_NOT_ALLOWED");
        if (context.Request.ContentType?.Split(';', 2)[0].Trim() != "application/json" ||
            context.Request.ContentLength is null or < 1 or > 8192)
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");

        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Count() != Fields.Length ||
                document.RootElement.EnumerateObject().Any(p => !Fields.Contains(p.Name)))
                return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");
            foreach (var field in Fields)
                if (!document.RootElement.TryGetProperty(field, out _))
                    return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");

            var command = document.RootElement.Deserialize<SyntheticAppointmentRequestCommand>(JsonOptions);
            var requestRef = context.Request.RouteValues["requestRef"]!.ToString();
            var change = await Store.ApplyAsync(requestRef!, command);
            var result = SyntheticAppointmentRequestProjection.Project(change.Snapshot);
            var status = change.Outcome switch
            {
                SyntheticAppointmentRequestOutcome.RecordedRequest or
                SyntheticAppointmentRequestOutcome.Replayed => StatusCodes.Status200OK,
                SyntheticAppointmentRequestOutcome.Invalid => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status409Conflict
            };
            return Results.Json(new
            {
                contract = "TRN-TECH-216",
                outcome = change.Outcome.ToString(),
                result.Revision,
                result.RequestCount,
                result.Requests,
                result.MemberAuthenticated,
                result.CenterAuthenticated,
                result.RequestedDoctorLicensed,
                result.SlotAvailabilityVerified,
                result.BookingConfirmed,
                result.ClinicalActionAllowed,
                nationalReleaseAllowed = false
            }, statusCode: status);
        }
        catch (JsonException)
        {
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            return Denied(StatusCodes.Status503ServiceUnavailable,
                "TEST_JOURNAL_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> ListForCenterAsync(HttpContext context)
    {
        var centerRef = context.Request.RouteValues["centerRef"]?.ToString();
        if (!ValidRef(centerRef))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_REFERENCE");
        if (context.Request.Query.Keys.Any(k => k is not ("afterRequestRef" or "limit")))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_QUERY");

        var afterValues = context.Request.Query["afterRequestRef"];
        if (afterValues.Count > 1)
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_QUERY");
        string? after = afterValues.Count == 0 ? null : afterValues[0];
        if (string.IsNullOrEmpty(after)) after = null;
        var limitValues = context.Request.Query["limit"];
        var limit = 10;
        if (limitValues.Count > 1 || limitValues.Count == 1 &&
            (!int.TryParse(limitValues[0], NumberStyles.None,
                CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 10))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_QUERY");

        try
        {
            var page = await Store.ReadForCenterAsync(centerRef!, after, limit);
            return Results.Json(new
            {
                contract = "TRN-TECH-217",
                syntheticOnly = true,
                source = "VERIFIED_ISOLATED_POSTGRES_APPOINTMENT_JOURNAL",
                centerRef,
                afterRequestRef = after ?? string.Empty,
                limit,
                hasMore = page.HasMore,
                nextAfterRequestRef = page.NextAfterRequestRef,
                requests = page.Requests,
                memberAuthenticated = false,
                centerAuthenticated = false,
                requestedDoctorLicensed = false,
                slotAvailabilityVerified = false,
                bookingConfirmed = false,
                clinicalActionAllowed = false,
                nationalReleaseAllowed = false
            });
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            return Denied(StatusCodes.Status503ServiceUnavailable,
                "TEST_JOURNAL_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> ReadAsync(HttpContext context)
    {
        var requestRef = context.Request.RouteValues["requestRef"]?.ToString();
        if (!ValidRef(requestRef))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_REFERENCE");
        try
        {
            var snapshot = await Store.ReadAsync(requestRef!);
            var result = SyntheticAppointmentRequestProjection.Project(snapshot);
            return Results.Json(new
            {
                contract = "TRN-TECH-216",
                outcome = "Read",
                result.Revision,
                result.RequestCount,
                result.Requests,
                result.MemberAuthenticated,
                result.CenterAuthenticated,
                result.RequestedDoctorLicensed,
                result.SlotAvailabilityVerified,
                result.BookingConfirmed,
                result.ClinicalActionAllowed,
                nationalReleaseAllowed = false
            });
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            return Denied(StatusCodes.Status503ServiceUnavailable,
                "TEST_JOURNAL_UNVERIFIABLE");
        }
    }

    private static bool AllowedOrigin(string origin) =>
        origin.Length == 0 || origin is "http://127.0.0.1:4320" or "http://localhost:4320";

    private static bool ValidRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');

    private static IResult Denied(int status, string code) =>
        Results.Json(new { contract = "TRN-TECH-216", outcome = code },
            statusCode: status);
}
