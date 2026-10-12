using System.Globalization;

namespace Taranom.MemberGovernance;

// TRN-TECH-093: intentionally narrow, TEST-ONLY, side-effect-free adapter.
// The actual five-lane rule remains in the INTERNAL SyntheticCostPreviewBuilder.
// No friend assembly, financial provider, member authentication or live API access.
public sealed record SyntheticCostWorkbenchLine(
    string? Component, string? State, string? Amount, string? Currency,
    string? SourceRef, Guid? EvidenceRef, long SourceRevision);

public sealed record SyntheticCostWorkbenchLineRead(
    string Component, string State, string? Amount, string? Currency,
    string? SourceRef, Guid? EvidenceRef, long SourceRevision);

public sealed record SyntheticCostWorkbenchRead(
    string State, string? Fingerprint,
    IReadOnlyList<SyntheticCostWorkbenchLineRead> Lines);

public static class SyntheticCostWorkbench
{
    public static SyntheticCostWorkbenchRead Inspect(
        string? memberRef, Guid previewId, long previewRevision,
        IReadOnlyList<SyntheticCostWorkbenchLine>? supplied)
    {
        static SyntheticCostWorkbenchRead Invalid() =>
            new("Invalid", null, Array.Empty<SyntheticCostWorkbenchLineRead>());
        if (supplied is null || supplied.Count != 5) return Invalid();
        var lines = new List<SyntheticCostLine>(5);
        foreach (var input in supplied)
        {
            // Enum.TryParse alone accepts numeric strings: reject them.
            if (!Enum.TryParse<SyntheticCostComponent>(input.Component, false, out var component) ||
                !Enum.IsDefined(component) || input.Component != component.ToString() ||
                !Enum.TryParse<SyntheticCostEvidenceState>(input.State, false, out var state) ||
                !Enum.IsDefined(state) || input.State != state.ToString())
                return Invalid();
            decimal? amount = null;
            if (input.Amount is not null)
            {
                if (!decimal.TryParse(input.Amount, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                    return Invalid();
                amount = parsed;
            }
            lines.Add(new(component, state, amount, input.Currency, input.SourceRef,
                input.EvidenceRef ?? Guid.Empty, input.SourceRevision));
        }
        var preview = SyntheticCostPreviewBuilder.Build(
            memberRef, previewId, previewRevision, lines);
        if (preview.State == SyntheticCostPreviewState.Invalid)
            return Invalid();
        return new(preview.State.ToString(), preview.Fingerprint,
            preview.Lines.Select(line => new SyntheticCostWorkbenchLineRead(
                line.Component.ToString(), line.State.ToString(),
                line.ClaimedAmount?.ToString("G29", CultureInfo.InvariantCulture),
                line.CurrencyCode, line.UnverifiedSourceRef,
                line.EvidenceRef == Guid.Empty ? null : line.EvidenceRef,
                line.SourceRevision)).ToArray());
    }
}
