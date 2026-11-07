using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Npgsql;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class MemberGroupInvitationApiHttpTests
{
    private const string Drafts = "/api/v1/me/group-invitation-drafts";
    private const string Preview = "/api/v1/me/group-invitations/preview";
    private const string Respond = "/api/v1/me/group-invitations/respond";
    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private static async Task<Guid> Draft(HttpClient client, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(Drafts, new { requestId = Guid.NewGuid(), method = "ExistingMember", recipientLabel = "Synthetic private label" }, ct);
        response.EnsureSuccessStatusCode();
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        return json!.RootElement.GetProperty("draft").GetProperty("draftRef").GetGuid();
    }
    private static async Task<string> State(HttpResponseMessage response, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json!.RootElement.GetProperty("invitation").GetProperty("state").GetString()!;
    }
    [Fact]
    public async Task InvitationRequiresOwnConsentAndDoesNotGrantMembershipOrRevealDraftLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("invite-owner-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("invite-recipient-" + Guid.NewGuid().ToString("N"));
        using var other = setup.AuthenticatedIdentityClient("invite-other-" + Guid.NewGuid().ToString("N"));
        using var anonymous = setup.Factory.CreateClient();
        var draft = await Draft(owner, ct); var token = Token();
        var path = $"{Drafts}/{draft:D}/invitations";
        var issue = new { requestId = Guid.NewGuid(), token };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Preview, new { token }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(path, issue, ct)).StatusCode);
        using var issued = await owner.PostAsJsonAsync(path, issue, ct);
        Assert.Equal("Pending", await State(issued, ct));
        Assert.DoesNotContain(token, await issued.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync(Preview, new { token }, ct)).StatusCode);
        using var preview = await recipient.PostAsJsonAsync(Preview, new { token }, ct);
        Assert.Equal("no-store", preview.Headers.CacheControl?.ToString());
        Assert.Equal("Pending", await State(preview, ct));
        var body = await preview.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("Synthetic private label", body);
        Assert.DoesNotContain("recipientSubject", body);
        Assert.Equal(HttpStatusCode.BadRequest, (await recipient.PostAsJsonAsync(Respond, new { token, decision = "Accepted", consentVersion = "unknown" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await recipient.PostAsJsonAsync(Respond, new { token, decision = "Accepted", consentVersion = "group-invitation-v1", recipientSubject = "other" }, ct)).StatusCode);
        var accept = new { token, decision = "Accepted", consentVersion = "group-invitation-v1" };
        var responses = await Task.WhenAll(recipient.PostAsJsonAsync(Respond, accept, ct), recipient.PostAsJsonAsync(Respond, accept, ct));
        var times = new List<DateTimeOffset>();
        foreach (var response in responses)
        {
            Assert.Equal("Accepted", await State(response, ct));
            using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
            times.Add(json!.RootElement.GetProperty("invitation").GetProperty("respondedAt").GetDateTimeOffset());
            response.Dispose();
        }
        Assert.Equal(times[0], times[1]);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(Respond, accept, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await recipient.PostAsJsonAsync(Respond, new { token, decision = "Declined", consentVersion = "group-invitation-v1" }, ct)).StatusCode);
        using var latest = await owner.GetFromJsonAsync<JsonDocument>(path, ct);
        Assert.Equal("Accepted", latest!.RootElement.GetProperty("invitation").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path, new { requestId = Guid.NewGuid(), token = Token() }, ct)).StatusCode);
        using var scopes = await recipient.GetFromJsonAsync<JsonDocument>("/api/v1/me/appointment-scopes", ct);
        Assert.Empty(scopes!.RootElement.GetProperty("appointmentScopes").EnumerateArray());
        using var profile = await recipient.GetFromJsonAsync<JsonDocument>("/api/v1/me/profile", ct);
        Assert.Equal(0L, profile!.RootElement.GetProperty("profile").GetProperty("revision").GetInt64());
    }
    [Fact]
    public async Task ReplacementAndArchiveInvalidateOldLinksAndReplaysCannotResurrectThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("invite-replace-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("invite-reader-" + Guid.NewGuid().ToString("N"));
        var draft = await Draft(owner, ct); var path = $"{Drafts}/{draft:D}/invitations";
        var first = new { requestId = Guid.NewGuid(), token = Token() };
        var responses = await Task.WhenAll(owner.PostAsJsonAsync(path, first, ct), owner.PostAsJsonAsync(path, first, ct));
        foreach (var response in responses) { Assert.Equal("Pending", await State(response, ct)); response.Dispose(); }
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path, new { first.requestId, token = Token() }, ct)).StatusCode);
        var second = new { requestId = Guid.NewGuid(), token = Token() };
        Assert.Equal("Pending", await State(await owner.PostAsJsonAsync(path, second, ct), ct));
        Assert.Equal("Revoked", await State(await owner.PostAsJsonAsync(path, first, ct), ct));
        Assert.Equal("Revoked", await State(await recipient.PostAsJsonAsync(Preview, new { first.token }, ct), ct));
        Assert.Equal(HttpStatusCode.Conflict, (await recipient.PostAsJsonAsync(Respond, new { first.token, decision = "Accepted", consentVersion = "group-invitation-v1" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"{Drafts}/{draft:D}/withdraw", null, ct)).StatusCode);
        Assert.Equal("Revoked", await State(await recipient.PostAsJsonAsync(Preview, new { second.token }, ct), ct));
        Assert.Equal(HttpStatusCode.Conflict, (await recipient.PostAsJsonAsync(Respond, new { second.token, decision = "Accepted", consentVersion = "group-invitation-v1" }, ct)).StatusCode);
        Assert.Equal("Revoked", await State(await owner.PostAsJsonAsync(path, second, ct), ct));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path, new { requestId = Guid.NewGuid(), token = Token() }, ct)).StatusCode);
    }
    [Fact]
    public async Task DeclineIsFinalAndUnknownTokensAndMalformedCommandsAreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("invite-decline-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("invite-decliner-" + Guid.NewGuid().ToString("N"));
        var draft = await Draft(owner, ct); var path = $"{Drafts}/{draft:D}/invitations"; var token = Token();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new { requestId = Guid.Empty, token }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new { requestId = Guid.NewGuid(), token = "weak" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.PostAsJsonAsync(Preview, new { token }, ct)).StatusCode);
        Assert.Equal("Pending", await State(await owner.PostAsJsonAsync(path, new { requestId = Guid.NewGuid(), token }, ct), ct));
        var command = new { token, decision = "Declined", consentVersion = "group-invitation-v1" };
        Assert.Equal("Declined", await State(await recipient.PostAsJsonAsync(Respond, command, ct), ct));
        Assert.Equal("Declined", await State(await recipient.PostAsJsonAsync(Respond, command, ct), ct));
        Assert.Equal(HttpStatusCode.Conflict, (await recipient.PostAsJsonAsync(Respond, new { token, decision = "Accepted", consentVersion = "group-invitation-v1" }, ct)).StatusCode);
        var secondDraft = await Draft(owner, ct);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"{Drafts}/{secondDraft:D}/invitations", new { requestId = Guid.NewGuid(), token }, ct)).StatusCode);
    }
    [Fact]
    public async Task ExpiryBlocksResponseAndDifferentRecipientsCannotBothClaimTheSameInvitation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("invite-expiry-" + Guid.NewGuid().ToString("N"));
        using var first = setup.AuthenticatedIdentityClient("invite-racer-a-" + Guid.NewGuid().ToString("N"));
        using var second = setup.AuthenticatedIdentityClient("invite-racer-b-" + Guid.NewGuid().ToString("N"));
        var draft = await Draft(owner, ct); var path = $"{Drafts}/{draft:D}/invitations"; var token = Token();
        Assert.Equal("Pending", await State(await owner.PostAsJsonAsync(path, new { requestId = Guid.NewGuid(), token }, ct), ct));
        await using (var db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!))
        await using (var expire = db.CreateCommand("UPDATE taranom_member_group_invitation SET created_at=now()-interval '8 days',expires_at=now()-interval '1 day' WHERE draft_ref=$1"))
        { expire.Parameters.AddWithValue(draft); await expire.ExecuteNonQueryAsync(ct); }
        Assert.Equal("Expired", await State(await first.PostAsJsonAsync(Preview, new { token }, ct), ct));
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync(Respond, new { token, decision = "Accepted", consentVersion = "group-invitation-v1" }, ct)).StatusCode);
        token = Token();
        Assert.Equal("Pending", await State(await owner.PostAsJsonAsync(path, new { requestId = Guid.NewGuid(), token }, ct), ct));
        var accept = new { token, decision = "Accepted", consentVersion = "group-invitation-v1" };
        var race = await Task.WhenAll(first.PostAsJsonAsync(Respond, accept, ct), second.PostAsJsonAsync(Respond, accept, ct));
        Assert.Single(race, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(race, r => r.StatusCode == HttpStatusCode.NotFound);
        foreach (var response in race) response.Dispose();
    }

}
