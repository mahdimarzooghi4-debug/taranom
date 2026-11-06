using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class MemberSupportApiHttpTests
{
    [Fact]
    public async Task RequestsArePersistedAndVisibleOnlyToTheirAuthenticatedOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var anonymous = setup.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/me/support-requests", ct)).StatusCode);
        var ownerSubject = "support-owner-" + Guid.NewGuid().ToString("N");
        using var owner = setup.AuthenticatedIdentityClient(ownerSubject);
        using var other = setup.AuthenticatedIdentityClient("support-other-" + Guid.NewGuid().ToString("N"));
        var requestId = Guid.NewGuid();
        var created = await owner.PostAsJsonAsync("/api/v1/me/support-requests",
            new { requestId, topic = "account", message = "Synthetic account assistance request" }, ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl?.ToString());
        using var receipt = await created.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Applied", receipt!.RootElement.GetProperty("outcome").GetString());
        var requestRef = receipt.RootElement.GetProperty("request").GetProperty("requestRef").GetGuid();
        using var read = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/support-requests", ct);
        var ownRequests = read!.RootElement.GetProperty("requests").EnumerateArray().ToArray();
        Assert.Single(ownRequests);
        Assert.Equal(requestRef, ownRequests[0].GetProperty("requestRef").GetGuid());
        Assert.Equal("Registered", ownRequests[0].GetProperty("state").GetString());
        using var cross = await other.GetFromJsonAsync<JsonDocument>(
            $"/api/v1/me/support-requests?requestId={requestId:D}&subject={ownerSubject}", ct);
        Assert.Empty(cross!.RootElement.GetProperty("requests").EnumerateArray());
    }

    [Fact]
    public async Task ExactRetriesReplayAndChangedPayloadCannotReuseTheRequestId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("support-replay-" + Guid.NewGuid().ToString("N"));
        var requestId = Guid.NewGuid();
        var payload = new { requestId, topic = "privacy", message = "Synthetic privacy assistance request" };
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            owner.PostAsJsonAsync("/api/v1/me/support-requests", payload, ct)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var outcomes = new List<string>();
        foreach (var response in responses)
        {
            using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
            outcomes.Add(json!.RootElement.GetProperty("outcome").GetString()!);
            response.Dispose();
        }
        Assert.Contains("Applied", outcomes);
        Assert.Contains("Replayed", outcomes);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/v1/me/support-requests",
            new { requestId, topic = "privacy", message = "A different synthetic request body" }, ct)).StatusCode);
        using var read = await owner.GetFromJsonAsync<JsonDocument>($"/api/v1/me/support-requests?requestId={requestId:D}", ct);
        Assert.Single(read!.RootElement.GetProperty("requests").EnumerateArray());
    }

    [Fact]
    public async Task InvalidTopicsContentAndOwnerSelectorsAreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("support-invalid-" + Guid.NewGuid().ToString("N"));
        foreach (var pair in new[] { ("unknown", "A synthetic test message"), ("account", "short"),
            ("account", new string('x', 4001)), ("account", "Invalid control\u0001message") })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/me/support-requests",
                new { requestId = Guid.NewGuid(), topic = pair.Item1, message = pair.Item2 }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/me/support-requests",
            new { requestId = Guid.NewGuid(), topic = "account", message = "Synthetic request body", subject = "another-user" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/me/support-requests?before=-1", ct)).StatusCode);
    }

    [Fact]
    public async Task HistoryPagesDoNotOverlapAndExactReceiptCanBeReconciled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("support-pages-" + Guid.NewGuid().ToString("N"));
        var firstRequestId = Guid.NewGuid();
        for (var i = 0; i < 22; i++)
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/me/support-requests",
                new { requestId = i == 0 ? firstRequestId : Guid.NewGuid(), topic = "appointments", message = "Synthetic support request " + i }, ct)).StatusCode);
        using var page1 = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/support-requests", ct);
        var first = page1!.RootElement.GetProperty("requests").EnumerateArray().Select(r => r.GetProperty("requestRef").GetGuid()).ToArray();
        Assert.Equal(20, first.Length);
        var cursor = page1.RootElement.GetProperty("nextBefore").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));
        using var page2 = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/support-requests?before=" + cursor, ct);
        var second = page2!.RootElement.GetProperty("requests").EnumerateArray().Select(r => r.GetProperty("requestRef").GetGuid()).ToArray();
        Assert.Equal(2, second.Length);
        Assert.Empty(first.Intersect(second));
        Assert.Equal(JsonValueKind.Null, page2.RootElement.GetProperty("nextBefore").ValueKind);
        using var exact = await owner.GetFromJsonAsync<JsonDocument>($"/api/v1/me/support-requests?requestId={firstRequestId:D}", ct);
        Assert.Single(exact!.RootElement.GetProperty("requests").EnumerateArray());
    }
}
