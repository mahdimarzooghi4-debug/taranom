using System.Globalization;
using System.Text.RegularExpressions;

namespace Taranom.CenterWorkflow.Demo;

// Shared syntax gate for provider schedule reads and captured schedule claims.
internal static class AppointmentTimestampSyntax
{
    private static readonly Regex OffsetTimestampPattern = new(
        @"\A[0-9]{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12][0-9]|3[01])T(?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z",
        RegexOptions.CultureInvariant);

    internal static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value is null || value.Length > 64 ||
            !OffsetTimestampPattern.IsMatch(value))
            return false;

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out timestamp);
    }
}
