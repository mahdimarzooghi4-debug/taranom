using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// Opaque transport boundary for insurer-owned calculation APIs. The relay does
// not parse request/response bodies or calculate coverage, shares, or payment.
internal readonly record struct InsuranceProviderApiResponse(
    int HttpStatusCode,
    ImmutableArray<byte> Body);

internal interface IInsuranceProviderApi
{
    Task<InsuranceProviderApiResponse> ForwardAsync(
        string providerRef,
        ImmutableArray<byte> providerDefinedRequest,
        CancellationToken cancellationToken);
}

internal enum InsuranceProviderRelayOutcome
{
    Denied = 1,
    ResponseReceived = 2
}

internal readonly record struct InsuranceProviderRelayResult(
    InsuranceProviderRelayOutcome Outcome,
    Guid ExchangeId,
    int? ProviderHttpStatusCode,
    ImmutableArray<byte> ProviderResponse);

internal sealed class SyntheticInsuranceProviderApiRelay(IInsuranceProviderApi providerApi)
{
    internal const int MaximumMessageBytes = 1024 * 1024;

    internal async Task<InsuranceProviderRelayResult> ForwardAsync(
        Guid exchangeId,
        string providerRef,
        ImmutableArray<byte> providerDefinedRequest,
        CancellationToken cancellationToken = default)
    {
        if (exchangeId == Guid.Empty ||
            !TestReference(providerRef) ||
            providerDefinedRequest.IsDefaultOrEmpty ||
            providerDefinedRequest.Length > MaximumMessageBytes)
            return Denied(exchangeId);

        var response = await providerApi.ForwardAsync(
            providerRef,
            ImmutableArray.CreateRange(providerDefinedRequest),
            cancellationToken);

        if (response.HttpStatusCode is < 100 or > 599 ||
            response.Body.IsDefault ||
            response.Body.Length > MaximumMessageBytes)
            return Denied(exchangeId);

        // A response means only that the provider returned bytes. The status
        // and body are not converted into eligibility or financial decisions.
        return new(
            InsuranceProviderRelayOutcome.ResponseReceived,
            exchangeId,
            response.HttpStatusCode,
            ImmutableArray.CreateRange(response.Body));
    }

    private static InsuranceProviderRelayResult Denied(Guid exchangeId) =>
        new(InsuranceProviderRelayOutcome.Denied, exchangeId, null,
            ImmutableArray<byte>.Empty);

    private static bool TestReference(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
