using System.Collections.Immutable;
using System.Text;
using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticInsuranceProviderApiRelayTests
{
    private sealed class FakeProviderApi(
        InsuranceProviderApiResponse response) : IInsuranceProviderApi
    {
        internal int Calls { get; private set; }
        internal string? ProviderRef { get; private set; }
        internal ImmutableArray<byte> Request { get; private set; }

        public Task<InsuranceProviderApiResponse> ForwardAsync(
            string providerRef,
            ImmutableArray<byte> providerDefinedRequest,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            ProviderRef = providerRef;
            Request = ImmutableArray.CreateRange(providerDefinedRequest);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Returns_provider_status_and_body_unchanged_without_calculation()
    {
        var request = ImmutableArray.CreateRange(
            Encoding.UTF8.GetBytes("{\"synthetic\":true,\"opaque\":\"provider-defined\"}"));
        var responseBody = ImmutableArray.CreateRange(
            Encoding.UTF8.GetBytes("{\"syntheticResult\":\"computed-by-provider\"}"));
        var api = new FakeProviderApi(new(200, responseBody));
        var relay = new SyntheticInsuranceProviderApiRelay(api);
        var exchangeId = Guid.NewGuid();

        var result = await relay.ForwardAsync(
            exchangeId, "test:insurer-a", request);

        Assert.Equal(InsuranceProviderRelayOutcome.ResponseReceived, result.Outcome);
        Assert.Equal(exchangeId, result.ExchangeId);
        Assert.Equal(200, result.ProviderHttpStatusCode);
        Assert.Equal(responseBody, result.ProviderResponse);
        Assert.Equal(request, api.Request);
        Assert.Equal("test:insurer-a", api.ProviderRef);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task Preserves_provider_denial_status_as_data_not_a_taranom_decision()
    {
        var api = new FakeProviderApi(new(422,
            ImmutableArray.CreateRange(Encoding.UTF8.GetBytes("synthetic-provider-response"))));
        var relay = new SyntheticInsuranceProviderApiRelay(api);

        var result = await relay.ForwardAsync(Guid.NewGuid(), "test:insurer-a",
            ImmutableArray.CreateRange(new byte[] { 1, 2, 3 }));

        Assert.Equal(InsuranceProviderRelayOutcome.ResponseReceived, result.Outcome);
        Assert.Equal(422, result.ProviderHttpStatusCode);
        Assert.Equal(1, api.Calls);
    }

    [Theory]
    [InlineData("insurer-live")]
    [InlineData("test:")]
    public async Task Denies_live_or_malformed_provider_scope_without_calling_api(
        string providerRef)
    {
        var api = new FakeProviderApi(new(200, ImmutableArray<byte>.Empty));
        var relay = new SyntheticInsuranceProviderApiRelay(api);

        var result = await relay.ForwardAsync(Guid.NewGuid(), providerRef,
            ImmutableArray.CreateRange(new byte[] { 1 }));

        Assert.Equal(InsuranceProviderRelayOutcome.Denied, result.Outcome);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task Denies_empty_or_oversized_requests_before_calling_api()
    {
        var api = new FakeProviderApi(new(200, ImmutableArray<byte>.Empty));
        var relay = new SyntheticInsuranceProviderApiRelay(api);

        var empty = await relay.ForwardAsync(Guid.NewGuid(), "test:insurer-a",
            ImmutableArray<byte>.Empty);
        var oversized = await relay.ForwardAsync(Guid.NewGuid(), "test:insurer-a",
            ImmutableArray.CreateRange(new byte[SyntheticInsuranceProviderApiRelay.MaximumMessageBytes + 1]));

        Assert.Equal(InsuranceProviderRelayOutcome.Denied, empty.Outcome);
        Assert.Equal(InsuranceProviderRelayOutcome.Denied, oversized.Outcome);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task Rejects_malformed_provider_response_without_reinterpreting_it()
    {
        var api = new FakeProviderApi(new(700, ImmutableArray<byte>.Empty));
        var relay = new SyntheticInsuranceProviderApiRelay(api);

        var result = await relay.ForwardAsync(Guid.NewGuid(), "test:insurer-a",
            ImmutableArray.CreateRange(new byte[] { 1 }));

        Assert.Equal(InsuranceProviderRelayOutcome.Denied, result.Outcome);
        Assert.Null(result.ProviderHttpStatusCode);
    }

    [Fact]
    public async Task Denies_empty_exchange_id_without_calling_api()
    {
        var api = new FakeProviderApi(new(200, ImmutableArray<byte>.Empty));
        var relay = new SyntheticInsuranceProviderApiRelay(api);

        var result = await relay.ForwardAsync(Guid.Empty, "test:insurer-a",
            ImmutableArray.CreateRange(new byte[] { 1 }));

        Assert.Equal(InsuranceProviderRelayOutcome.Denied, result.Outcome);
        Assert.Equal(Guid.Empty, result.ExchangeId);
        Assert.Equal(0, api.Calls);
    }
}
