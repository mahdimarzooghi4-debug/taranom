using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class MemberGroupDraftApiHttpTests
{
    private const string Path = "/api/v1/me/group-invitation-drafts";
    [Fact]
    public async Task DraftIsPrivateOwnerDataAndCreatesNoMembershipOrAppointmentAuthority()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("group-owner-" + Guid.NewGuid().ToString("N"));
        using var other = setup.AuthenticatedIdentityClient("group-other-" + Guid.NewGuid().ToString("N"));
        using var anonymous = setup.Factory.CreateClient();
        var command = new { requestId = Guid.NewGuid(), method = "NewRegistration", recipientLabel = "Synthetic private invitation draft" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Path, command, ct)).StatusCode);
        using var response = await owner.PostAsJsonAsync(Path, command, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var receipt = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var reference = receipt!.RootElement.GetProperty("draft").GetProperty("draftRef").GetGuid();
        Assert.Equal("Draft", receipt.RootElement.GetProperty("draft").GetProperty("state").GetString());
        using var own = await owner.GetFromJsonAsync<JsonDocument>(Path, ct);
        Assert.Equal(reference, Assert.Single(own!.RootElement.GetProperty("drafts").EnumerateArray()).GetProperty("draftRef").GetGuid());
        using var cross = await other.GetFromJsonAsync<JsonDocument>($"{Path}?draftRef={reference:D}&subject=someone-else", ct);
        Assert.Empty(cross!.RootElement.GetProperty("drafts").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"{Path}/{reference:D}/withdraw", null, ct)).StatusCode);
        using var scopes = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/appointment-scopes", ct);
        Assert.Empty(scopes!.RootElement.GetProperty("appointmentScopes").EnumerateArray());
        using var notifications = await other.GetFromJsonAsync<JsonDocument>("/api/v1/me/notifications", ct);
        Assert.Empty(notifications!.RootElement.GetProperty("notifications").EnumerateArray());
        using var profile = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/profile", ct);
        Assert.Equal(0L, profile!.RootElement.GetProperty("profile").GetProperty("revision").GetInt64());
    }
    [Fact]
    public async Task ConcurrentCreateReplaysAndWithdrawalCannotBeUndoneByCreationReplay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("group-retry-" + Guid.NewGuid().ToString("N"));
        var command = new { requestId = Guid.NewGuid(), method = "ExistingMember", recipientLabel = "Synthetic group label" };
        var results = await Task.WhenAll(owner.PostAsJsonAsync(Path, command, ct), owner.PostAsJsonAsync(Path, command, ct));
        var outcomes = new List<string>();
        var references = new List<Guid>();
        foreach (var result in results)
        {
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            using var receipt = await result.Content.ReadFromJsonAsync<JsonDocument>(ct);
            outcomes.Add(receipt!.RootElement.GetProperty("outcome").GetString()!);
            references.Add(receipt.RootElement.GetProperty("draft").GetProperty("draftRef").GetGuid());
            result.Dispose();
        }
        Assert.Contains("Applied", outcomes);
        Assert.Contains("Replayed", outcomes);
        Assert.Equal(references[0], references[1]);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(Path,
            new { command.requestId, method = "NewRegistration", command.recipientLabel }, ct)).StatusCode);
        var withdraw = $"{Path}/{references[0]:D}/withdraw";
        var withdrawals = await Task.WhenAll(owner.PostAsync(withdraw, null, ct), owner.PostAsync(withdraw, null, ct));
        var timestamps = new List<DateTimeOffset>();
        foreach (var response in withdrawals)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var receipt = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
            timestamps.Add(receipt!.RootElement.GetProperty("draft").GetProperty("withdrawnAt").GetDateTimeOffset());
            response.Dispose();
        }
        Assert.Equal(timestamps[0], timestamps[1]);
        using var replay = await owner.PostAsJsonAsync(Path, command, ct);
        using var json = await replay.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Replayed", json!.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("Withdrawn", json.RootElement.GetProperty("draft").GetProperty("state").GetString());
        using var page = await owner.GetFromJsonAsync<JsonDocument>($"{Path}?requestId={command.requestId:D}", ct);
        Assert.Single(page!.RootElement.GetProperty("drafts").EnumerateArray());
    }
    [Fact]
    public async Task ValidationRejectsRecipientAuthorityAndPagesDoNotOverlap()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("group-pages-" + Guid.NewGuid().ToString("N"));
        foreach (var pair in new[] { ("Unknown", "Synthetic label"), ("NewRegistration", "x"), ("ExistingMember", new string('x',101)), ("ExistingMember", "Control\u0001label") })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Path,
                new { requestId = Guid.NewGuid(), method = pair.Item1, recipientLabel = pair.Item2 }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Path,
            new { requestId = Guid.NewGuid(), method = "ExistingMember", recipientLabel = "Synthetic label", recipientSubject = "someone-else", state = "Accepted" }, ct)).StatusCode);
        var firstId = Guid.NewGuid();
        for (var i = 0; i < 22; i++)
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(Path,
                new { requestId = i == 0 ? firstId : Guid.NewGuid(), method = "NewRegistration", recipientLabel = "Synthetic pagination label " + i }, ct)).StatusCode);
        using var page1 = await owner.GetFromJsonAsync<JsonDocument>(Path, ct);
        var first = page1!.RootElement.GetProperty("drafts").EnumerateArray().Select(d => d.GetProperty("draftRef").GetGuid()).ToArray();
        Assert.Equal(20, first.Length);
        using var page2 = await owner.GetFromJsonAsync<JsonDocument>(Path + "?before=" + page1.RootElement.GetProperty("nextBefore").GetString(), ct);
        var second = page2!.RootElement.GetProperty("drafts").EnumerateArray().Select(d => d.GetProperty("draftRef").GetGuid()).ToArray();
        Assert.Equal(2, second.Length);
        Assert.Empty(first.Intersect(second));
        using var exact = await owner.GetFromJsonAsync<JsonDocument>($"{Path}?requestId={firstId:D}", ct);
        Assert.Single(exact!.RootElement.GetProperty("drafts").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(Path+"?before=0", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"{Path}/{Guid.Empty:D}/withdraw", null, ct)).StatusCode);
    }
}
