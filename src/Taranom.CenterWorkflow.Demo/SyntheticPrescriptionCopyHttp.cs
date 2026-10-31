using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// Local CI-only HTTP adapter. It never authenticates a member or validates a prescription.
internal static class SyntheticPrescriptionCopyHttp
{
    internal static PgSyntheticPrescriptionCopyJournal Store { get; set; } = null!;
    private const int MaximumBodyBytes = 6 * 1024 * 1024;
    private static readonly string[] Fields =
    [
        "requestId", "expectedRevision", "memberRef", "prescriptionRef",
        "issuerRef", "sourceRevision", "issuedAt", "documentBase64", "documentSha256"
    ];
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private sealed record WriteDto(
        Guid RequestId,
        long ExpectedRevision,
        string MemberRef,
        string PrescriptionRef,
        string IssuerRef,
        string SourceRevision,
        string IssuedAt,
        string DocumentBase64,
        string DocumentSha256);

    internal static async Task<IResult> WriteAsync(HttpContext context)
    {
        var memberRef = context.Request.RouteValues["memberRef"]?.ToString();
        var prescriptionRef = context.Request.RouteValues["prescriptionRef"]?.ToString();
        if (!ValidRef(memberRef) || !ValidRef(prescriptionRef))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_REFERENCE");
        if (!AllowedOrigin(context.Request.Headers.Origin.ToString()))
            return Denied(StatusCodes.Status403Forbidden, "ORIGIN_NOT_ALLOWED");
        if (context.Request.ContentType?.Split(';', 2)[0].Trim() != "application/json" ||
            context.Request.ContentLength is null or < 1 or > MaximumBodyBytes)
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");

        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != Fields.Length ||
                root.EnumerateObject().Any(p => !Fields.Contains(p.Name)) ||
                Fields.Any(field => !root.TryGetProperty(field, out _)))
                return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");

            var dto = root.Deserialize<WriteDto>(JsonOptions);
            if (dto is null || dto.MemberRef != memberRef ||
                dto.PrescriptionRef != prescriptionRef ||
                !DateTimeOffset.TryParse(dto.IssuedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var issuedAt))
                return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");
            if (dto.DocumentBase64 is null)
                return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");
            var bytes = Convert.FromBase64String(dto.DocumentBase64);
            if (bytes.Length is < 1 or > 4 * 1024 * 1024)
                return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");

            var command = new SyntheticPrescriptionCopyCommand(
                dto.RequestId, dto.ExpectedRevision, dto.MemberRef, dto.PrescriptionRef,
                dto.IssuerRef, dto.SourceRevision, issuedAt,
                System.Collections.Immutable.ImmutableArray.CreateRange(bytes),
                dto.DocumentSha256);
            var change = await Store.ApplyAsync(prescriptionRef!, command);
            var status = change.Outcome switch
            {
                SyntheticPrescriptionCopyOutcome.RecordedCopy or
                SyntheticPrescriptionCopyOutcome.Replayed => StatusCodes.Status200OK,
                SyntheticPrescriptionCopyOutcome.Invalid => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status409Conflict
            };
            return Results.Json(new
            {
                contract = "TRN-TECH-241",
                outcome = change.Outcome.ToString(),
                revision = change.Snapshot.Revision,
                versionCount = change.Snapshot.History.Length,
                documentSha256 = change.Snapshot.History.Length == 0
                    ? string.Empty : change.Snapshot.History[^1].DocumentSha256,
                memberAuthenticated = false,
                issuerAuthenticated = false,
                sourceAuthenticityVerified = false,
                prescriptionValidityVerified = false,
                clinicalUseAllowed = false,
                dispensingAllowed = false,
                paymentAllowed = false,
                nationalReleaseAllowed = false
            }, statusCode: status);
        }
        catch (JsonException)
        {
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");
        }
        catch (FormatException)
        {
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            return Denied(StatusCodes.Status503ServiceUnavailable,
                "PRESCRIPTION_COPY_JOURNAL_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> ReadAsync(HttpContext context)
    {
        var memberRef = context.Request.RouteValues["memberRef"]?.ToString();
        var prescriptionRef = context.Request.RouteValues["prescriptionRef"]?.ToString();
        if (!ValidRef(memberRef) || !ValidRef(prescriptionRef))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_REFERENCE");
        try
        {
            var snapshot = await Store.ReadAsync(memberRef!, prescriptionRef!);
            if (snapshot is null)
                return Denied(StatusCodes.Status404NotFound, "PRESCRIPTION_COPY_NOT_FOUND");
            return Results.Json(new
            {
                contract = "TRN-TECH-241",
                syntheticOnly = true,
                revision = snapshot.Revision,
                versionCount = snapshot.History.Length,
                versions = snapshot.History.Select(version => new
                {
                    version.Revision,
                    version.MemberRef,
                    version.PrescriptionRef,
                    version.IssuerRef,
                    version.SourceRevision,
                    issuedAt = version.IssuedAt.ToString("O", CultureInfo.InvariantCulture),
                    documentBase64 = Convert.ToBase64String(version.DocumentBytes.AsSpan()),
                    version.DocumentSha256
                }),
                memberAuthenticated = false,
                issuerAuthenticated = false,
                sourceAuthenticityVerified = false,
                prescriptionValidityVerified = false,
                clinicalUseAllowed = false,
                dispensingAllowed = false,
                paymentAllowed = false,
                nationalReleaseAllowed = false
            });
        }
        catch (InvalidOperationException ex) when (ex.Message == "PRESCRIPTION_COPY_SCOPE_CONFLICT")
        {
            return Denied(StatusCodes.Status403Forbidden, "PRESCRIPTION_COPY_SCOPE_CONFLICT");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            return Denied(StatusCodes.Status503ServiceUnavailable,
                "PRESCRIPTION_COPY_JOURNAL_UNVERIFIABLE");
        }
    }

    private static bool ValidRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');

    private static bool AllowedOrigin(string origin) =>
        origin.Length == 0 || origin is "http://127.0.0.1:4320" or "http://localhost:4320";

    private static IResult Denied(int status, string code) =>
        Results.Json(new { contract = "TRN-TECH-241", outcome = "Denied", error = code },
            statusCode: status);
}
