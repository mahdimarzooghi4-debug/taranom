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
    [Fact]
    public async Task StaffReplyRequiresCurrentCentralGrantAndIsVisibleOnlyToOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        var staffId = "support-staff-" + Guid.NewGuid().ToString("N");
        using var owner = setup.AuthenticatedIdentityClient("support-patient-" + Guid.NewGuid().ToString("N"));
        using var staff = setup.AuthenticatedIdentityClient(staffId);
        using var other = setup.AuthenticatedIdentityClient("support-stranger-" + Guid.NewGuid().ToString("N"));
        var createId = Guid.NewGuid();
        var created = await owner.PostAsJsonAsync("/api/v1/me/support-requests",
            new { requestId = createId, topic = "account", message = "Synthetic request awaiting staff assistance" }, ct);
        using var receipt = await created.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var ticket = receipt!.RootElement.GetProperty("request").GetProperty("requestRef").GetGuid();
        var path = $"/api/v1/operations/support-requests/{ticket:D}/reply";
        var command = new { requestId = Guid.NewGuid(), expectedRevision = 0, reply = "Synthetic staff answer for the member" };
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/operations/support-requests", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync(path, command, ct)).StatusCode);
        await setup.GrantAsync(staffId, "SupportRequestsManage", "wrong-scope", ct);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(path, command, ct)).StatusCode);
        await setup.GrantAsync(staffId, "SupportRequestsManage", "taranom-support", ct);
        using var queue = await staff.GetFromJsonAsync<JsonDocument>($"/api/v1/operations/support-requests?requestRef={ticket:D}", ct);
        Assert.Single(queue!.RootElement.GetProperty("requests").EnumerateArray());
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => staff.PostAsJsonAsync(path, command, ct)));
        var outcomes = new List<string>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
            outcomes.Add(json!.RootElement.GetProperty("outcome").GetString()!);
            response.Dispose();
        }
        Assert.Contains("Applied", outcomes);
        Assert.Contains("Replayed", outcomes);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(path,
            new { command.requestId, expectedRevision = 0, reply = "A different synthetic answer" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(path,
            new { requestId = Guid.NewGuid(), expectedRevision = 0, command.reply }, ct)).StatusCode);
        using var own = await owner.GetFromJsonAsync<JsonDocument>($"/api/v1/me/support-requests?requestId={createId:D}", ct);
        var answered = Assert.Single(own!.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal("Answered", answered.GetProperty("state").GetString());
        Assert.Equal(1, answered.GetProperty("revision").GetInt64());
        Assert.Equal(command.reply, answered.GetProperty("reply").GetString());
        Assert.NotEqual(JsonValueKind.Null, answered.GetProperty("repliedAt").ValueKind);
        using var cross = await other.GetFromJsonAsync<JsonDocument>($"/api/v1/me/support-requests?requestId={createId:D}", ct);
        Assert.Empty(cross!.RootElement.GetProperty("requests").EnumerateArray());
        // Replaying intake after the answer returns the same ticket with its current answer.
        var replay = await owner.PostAsJsonAsync("/api/v1/me/support-requests",
            new { requestId = createId, topic = "account", message = "Synthetic request awaiting staff assistance" }, ct);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    [Fact]
    public async Task CompetingStaffCannotOverwriteAnswerAndInvalidEvidenceCannotReadQueue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        var staffId = "support-race-" + Guid.NewGuid().ToString("N");
        var staleId = "support-stale-" + Guid.NewGuid().ToString("N");
        using var staff = setup.AuthenticatedIdentityClient(staffId);
        using var stale = setup.AuthenticatedIdentityClient(staleId);
        using var owner = setup.AuthenticatedIdentityClient("support-race-owner-" + Guid.NewGuid().ToString("N"));
        await setup.GrantAsync(staffId, "SupportRequestsManage", "taranom-support", ct);
        await setup.GrantAsync(staleId, "SupportRequestsManage", "taranom-support", ct, currentEvidence: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await stale.GetAsync("/api/v1/operations/support-requests", ct)).StatusCode);
        var created = await owner.PostAsJsonAsync("/api/v1/me/support-requests",
            new { requestId = Guid.NewGuid(), topic = "privacy", message = "Synthetic race support request" }, ct);
        using var receipt = await created.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var ticket = receipt!.RootElement.GetProperty("request").GetProperty("requestRef").GetGuid();
        var path = $"/api/v1/operations/support-requests/{ticket:D}/reply";
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(path,
            new { requestId = Guid.NewGuid(), expectedRevision = 0, reply = "short" }, ct)).StatusCode);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => staff.PostAsJsonAsync(path,
            new { requestId = Guid.NewGuid(), expectedRevision = 0, reply = "Synthetic competing reply " + i }, ct)));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var result in results) result.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostAsJsonAsync(
            $"/api/v1/operations/support-requests/{Guid.NewGuid():D}/reply",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, reply = "Synthetic missing ticket reply" }, ct)).StatusCode);
    }
}
