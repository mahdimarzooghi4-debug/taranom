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

    [Fact]
    public async Task AnsweredInvitationHistoryIsPrivateAndSurvivesDraftArchive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("history-owner-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("history-recipient-" + Guid.NewGuid().ToString("N"));
        using var other = setup.AuthenticatedIdentityClient("history-other-" + Guid.NewGuid().ToString("N"));
        using var anonymous = setup.Factory.CreateClient();
        const string history = "/api/v1/me/group-invitations";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(history, ct)).StatusCode);
        var draft = await Draft(owner, ct); var token = Token(); var requestId = Guid.NewGuid();
        Assert.Equal("Pending", await State(await owner.PostAsJsonAsync($"{Drafts}/{draft:D}/invitations", new { requestId, token }, ct), ct));
        using (var before = await recipient.GetFromJsonAsync<JsonDocument>(history, ct))
            Assert.Empty(before!.RootElement.GetProperty("invitations").EnumerateArray());
        Assert.Equal("Accepted", await State(await recipient.PostAsJsonAsync(Respond, new { token, decision = "Accepted", consentVersion = "group-invitation-v1" }, ct), ct));
        using var response = await recipient.GetAsync(history, ct);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(token, body); Assert.DoesNotContain(requestId.ToString(), body);
        Assert.DoesNotContain("Synthetic private label", body); Assert.DoesNotContain("recipientSubject", body);
        using var page = JsonDocument.Parse(body);
        var item = Assert.Single(page.RootElement.GetProperty("invitations").EnumerateArray());
        var reference = item.GetProperty("invitationRef").GetGuid();
        Assert.Equal("Accepted", item.GetProperty("state").GetString());
        Assert.False(item.TryGetProperty("requestId", out _));
        foreach (var client in new[] { owner, other })
        {
            using var cross = await client.GetFromJsonAsync<JsonDocument>($"{history}?invitationRef={reference:D}", ct);
            Assert.Empty(cross!.RootElement.GetProperty("invitations").EnumerateArray());
        }
        await owner.PostAsync($"{Drafts}/{draft:D}/withdraw", null, ct);
        using var retained = await recipient.GetFromJsonAsync<JsonDocument>($"{history}?invitationRef={reference:D}", ct);
        var archived = Assert.Single(retained!.RootElement.GetProperty("invitations").EnumerateArray());
        Assert.Equal(item.GetProperty("respondedAt").GetString(), archived.GetProperty("respondedAt").GetString());
        Assert.Equal("Accepted", archived.GetProperty("state").GetString());
    }
    [Fact]
    public async Task RecipientHistoryUsesBoundedNonOverlappingPagesAndExactLookup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("history-pages-owner-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("history-pages-reader-" + Guid.NewGuid().ToString("N"));
        const string history = "/api/v1/me/group-invitations";
        Guid oldest = Guid.Empty;
        for (var i = 0; i < 22; i++)
        {
            var draft = await Draft(owner, ct); var token = Token();
            Assert.Equal("Pending", await State(await owner.PostAsJsonAsync($"{Drafts}/{draft:D}/invitations", new { requestId = Guid.NewGuid(), token }, ct), ct));
            using var response = await recipient.PostAsJsonAsync(Respond, new { token, decision = i % 2 == 0 ? "Accepted" : "Declined", consentVersion = "group-invitation-v1" }, ct);
            response.EnsureSuccessStatusCode();
            using var receipt = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
            if (i == 0) oldest = receipt!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
        }
        using var page1 = await recipient.GetFromJsonAsync<JsonDocument>(history, ct);
        var first = page1!.RootElement.GetProperty("invitations").EnumerateArray().Select(v => v.GetProperty("invitationRef").GetGuid()).ToArray();
        Assert.Equal(20, first.Length);
        using var page2 = await recipient.GetFromJsonAsync<JsonDocument>(history + "?before=" + page1.RootElement.GetProperty("nextBefore").GetString(), ct);
        var second = page2!.RootElement.GetProperty("invitations").EnumerateArray().Select(v => v.GetProperty("invitationRef").GetGuid()).ToArray();
        Assert.Equal(2, second.Length); Assert.Empty(first.Intersect(second));
        Assert.Equal(JsonValueKind.Null, page2.RootElement.GetProperty("nextBefore").ValueKind);
        using var exact = await recipient.GetFromJsonAsync<JsonDocument>($"{history}?invitationRef={oldest:D}", ct);
        Assert.Equal(oldest, Assert.Single(exact!.RootElement.GetProperty("invitations").EnumerateArray()).GetProperty("invitationRef").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await recipient.GetAsync(history + "?before=0", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await recipient.GetAsync(history + "?invitationRef=" + Guid.Empty, ct)).StatusCode);
    }


    [Fact]
    public async Task OwnerCanInspectPrivateInvitationActivityWithoutGrantingMembership()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("activity-owner-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("activity-recipient-" + Guid.NewGuid().ToString("N"));
        using var stranger = setup.AuthenticatedIdentityClient("activity-stranger-" + Guid.NewGuid().ToString("N"));
        using var anonymous = setup.Factory.CreateClient();
        const string activity = "/api/v1/me/group-invitation-activity";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(activity, ct)).StatusCode);
        var acceptedDraft = await Draft(owner, ct);
        var acceptedToken = Token(); var acceptedRequest = Guid.NewGuid();
        using var acceptedIssue = await owner.PostAsJsonAsync($"{Drafts}/{acceptedDraft:D}/invitations",
            new { requestId = acceptedRequest, token = acceptedToken }, ct);
        Assert.Equal("Pending", await State(acceptedIssue, ct));
        Assert.Equal("Accepted", await State(await recipient.PostAsJsonAsync(Respond,
            new { token = acceptedToken, decision = "Accepted", consentVersion = "group-invitation-v1" }, ct), ct));
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsync($"{Drafts}/{acceptedDraft:D}/withdraw", null, ct)).StatusCode);

        var replacedDraft = await Draft(owner, ct);
        var oldToken = Token(); var currentToken = Token();
        Assert.Equal("Pending", await State(await owner.PostAsJsonAsync(
            $"{Drafts}/{replacedDraft:D}/invitations", new { requestId = Guid.NewGuid(), token = oldToken }, ct), ct));
        Assert.Equal("Pending", await State(await owner.PostAsJsonAsync(
            $"{Drafts}/{replacedDraft:D}/invitations", new { requestId = Guid.NewGuid(), token = currentToken }, ct), ct));
        // Server-clock expiry, not a forged browser-state transition.
        await using (var db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!))
        await using (var expire = db.CreateCommand("""
            UPDATE taranom_member_group_invitation
            SET created_at=now()-interval '8 days', expires_at=now()-interval '1 day'
            WHERE draft_ref=$1 AND token_hash=$2
            """))
        {
            expire.Parameters.AddWithValue(replacedDraft);
            expire.Parameters.AddWithValue(Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(currentToken))));
            Assert.Equal(1, await expire.ExecuteNonQueryAsync(ct));
        }

        var declinedDraft = await Draft(owner, ct); var declinedToken = Token();
        Assert.Equal("Pending", await State(await owner.PostAsJsonAsync(
            $"{Drafts}/{declinedDraft:D}/invitations", new { requestId = Guid.NewGuid(), token = declinedToken }, ct), ct));
        Assert.Equal("Declined", await State(await recipient.PostAsJsonAsync(Respond,
            new { token = declinedToken, decision = "Declined", consentVersion = "group-invitation-v1" }, ct), ct));

        using var response = await owner.GetAsync(activity, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync(ct);
        foreach (var secret in new[] { acceptedToken, oldToken, currentToken, declinedToken,
                      acceptedRequest.ToString(), "activity-recipient-", "tokenHash", "recipientIssuer",
                      "recipientSubject", "consentVersion" })
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("invitations").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Equal(new[] { "Accepted", "Revoked", "Expired", "Declined" }.OrderBy(x => x),
            items.Select(x => x.GetProperty("state").GetString()!).OrderBy(x => x));
        Assert.All(items, x => {
            Assert.Equal("Synthetic private label", x.GetProperty("recipientLabel").GetString());
            Assert.Equal(JsonValueKind.String, x.GetProperty("draftRef").ValueKind);
            Assert.False(x.TryGetProperty("requestId", out _));
            Assert.False(x.TryGetProperty("recipient_issuer", out _));
        });
        var accepted = Assert.Single(items, x => x.GetProperty("state").GetString() == "Accepted");
        Assert.Equal("Withdrawn", accepted.GetProperty("draftState").GetString());
        Assert.NotEqual(JsonValueKind.Null, accepted.GetProperty("respondedAt").ValueKind);
        var archivedRef = accepted.GetProperty("invitationRef").GetGuid();
        using var exact = await owner.GetFromJsonAsync<JsonDocument>(
            $"{activity}?invitationRef={archivedRef:D}", ct);
        Assert.Equal("Accepted", Assert.Single(exact!.RootElement.GetProperty("invitations").EnumerateArray())
            .GetProperty("state").GetString());
        using var foreign = await stranger.GetFromJsonAsync<JsonDocument>(
            $"{activity}?invitationRef={archivedRef:D}", ct);
        Assert.Empty(foreign!.RootElement.GetProperty("invitations").EnumerateArray());
        using var foreignList = await stranger.GetFromJsonAsync<JsonDocument>(activity, ct);
        Assert.Empty(foreignList!.RootElement.GetProperty("invitations").EnumerateArray());
        using var ownScopes = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/appointment-scopes", ct);
        Assert.Empty(ownScopes!.RootElement.GetProperty("appointmentScopes").EnumerateArray());
    }

    [Fact]
    public async Task OwnerInvitationActivityHasBoundedNonOverlappingPagesAndRejectsMalformedQueries()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("activity-pages-" + Guid.NewGuid().ToString("N"));
        using var other = setup.AuthenticatedIdentityClient("activity-pages-other-" + Guid.NewGuid().ToString("N"));
        const string activity = "/api/v1/me/group-invitation-activity";
        var draft = await Draft(owner, ct);
        Guid oldest = Guid.Empty;
        for (var index = 0; index < 22; index++)
        {
            using var issue = await owner.PostAsJsonAsync(
                $"{Drafts}/{draft:D}/invitations", new { requestId = Guid.NewGuid(), token = Token() }, ct);
            Assert.Equal("Pending", await State(issue, ct));
            using var json = await issue.Content.ReadFromJsonAsync<JsonDocument>(ct);
            if (index == 0) oldest = json!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
        }
        using var firstPage = await owner.GetFromJsonAsync<JsonDocument>(activity, ct);
        var first = firstPage!.RootElement.GetProperty("invitations").EnumerateArray()
            .Select(x => x.GetProperty("invitationRef").GetGuid()).ToArray();
        Assert.Equal(20, first.Length);
        var cursor = firstPage.RootElement.GetProperty("nextBefore").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));
        using var secondPage = await owner.GetFromJsonAsync<JsonDocument>(activity + "?before=" + cursor, ct);
        var second = secondPage!.RootElement.GetProperty("invitations").EnumerateArray()
            .Select(x => x.GetProperty("invitationRef").GetGuid()).ToArray();
        Assert.Equal(2, second.Length);
        Assert.Empty(first.Intersect(second));
        Assert.Equal(JsonValueKind.Null, secondPage.RootElement.GetProperty("nextBefore").ValueKind);
        Assert.Contains(oldest, second);
        // Filtering is done by PostgreSQL before LIMIT, not by the first page in the browser.
        using var draftPage1 = await owner.GetFromJsonAsync<JsonDocument>($"{activity}?draftRef={draft:D}", ct);
        Assert.Equal(20, draftPage1!.RootElement.GetProperty("invitations").GetArrayLength());
        var draftCursor = draftPage1.RootElement.GetProperty("nextBefore").GetString();
        using var draftPage2 = await owner.GetFromJsonAsync<JsonDocument>(
            $"{activity}?draftRef={draft:D}&before={draftCursor}", ct);
        var draftSecond = draftPage2!.RootElement.GetProperty("invitations").EnumerateArray()
            .Select(x => x.GetProperty("invitationRef").GetGuid()).ToArray();
        Assert.Equal(2, draftSecond.Length);
        Assert.Empty(first.Intersect(draftSecond));
        using var pending = await owner.GetFromJsonAsync<JsonDocument>(
            $"{activity}?draftRef={draft:D}&state=Pending", ct);
        var pendingOne = Assert.Single(pending!.RootElement.GetProperty("invitations").EnumerateArray());
        Assert.Equal("Pending", pendingOne.GetProperty("state").GetString());
        Assert.Equal("Draft", pendingOne.GetProperty("draftState").GetString());
        Assert.Equal(JsonValueKind.Null, pending.RootElement.GetProperty("nextBefore").ValueKind);
        using var exact = await owner.GetFromJsonAsync<JsonDocument>($"{activity}?invitationRef={oldest:D}", ct);
        Assert.Equal(oldest, Assert.Single(exact!.RootElement.GetProperty("invitations").EnumerateArray())
            .GetProperty("invitationRef").GetGuid());
        using var otherExact = await other.GetFromJsonAsync<JsonDocument>($"{activity}?invitationRef={oldest:D}", ct);
        Assert.Empty(otherExact!.RootElement.GetProperty("invitations").EnumerateArray());
        foreach (var query in new[] { "?before=0", "?before=-1", "?invitationRef=" + Guid.Empty,
                "?unexpected=1", "?before=1&before=2",
                "?invitationRef=" + oldest.ToString("D") + "&before=2" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(activity + query, ct)).StatusCode);
    }


    [Fact]
    public async Task PendingWorklistFindsOlderUnansweredLinkBehindMoreThanTwentyTerminalLinks()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("worklist-owner-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("worklist-recipient-" + Guid.NewGuid().ToString("N"));
        using var other = setup.AuthenticatedIdentityClient("worklist-foreign-" + Guid.NewGuid().ToString("N"));
        using var anonymous = setup.Factory.CreateClient();
        const string activity = "/api/v1/me/group-invitation-activity";
        var olderDraft = await Draft(owner, ct);
        var olderPath = $"{Drafts}/{olderDraft:D}/invitations";
        var olderToken = Token();
        using var olderIssue = await owner.PostAsJsonAsync(olderPath,
            new { requestId = Guid.NewGuid(), token = olderToken }, ct);
        Assert.Equal("Pending", await State(olderIssue, ct));
        using var olderJson = await olderIssue.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var olderRef = olderJson!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
        var terminalRefs = new List<Guid>();

        for (var i = 0; i < 22; i++)
        {
            var draft = await Draft(owner, ct);
            var path = $"{Drafts}/{draft:D}/invitations";
            var token = Token();
            using var created = await owner.PostAsJsonAsync(path,
                new { requestId = Guid.NewGuid(), token }, ct);
            Assert.Equal("Pending", await State(created, ct));
            using var response = await created.Content.ReadFromJsonAsync<JsonDocument>(ct);
            var invitation = response!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
            terminalRefs.Add(invitation);
            if (i % 2 == 0)
                Assert.Equal("Revoked", await State(await owner.PostAsync(
                    $"{path}/{invitation:D}/revoke", null, ct), ct));
            else
                Assert.Equal("Declined", await State(await recipient.PostAsJsonAsync(Respond,
                    new { token, decision = "Declined", consentVersion = "group-invitation-v1" }, ct), ct));
        }

        using var first = await owner.GetFromJsonAsync<JsonDocument>(activity, ct);
        var first20 = first!.RootElement.GetProperty("invitations").EnumerateArray()
            .Select(x => x.GetProperty("invitationRef").GetGuid()).ToArray();
        Assert.Equal(20, first20.Length);
        Assert.DoesNotContain(olderRef, first20);

        using var worklistResponse = await owner.GetAsync(activity + "?state=Pending", ct);
        Assert.Equal("no-store", worklistResponse.Headers.CacheControl?.ToString());
        using var worklist = await worklistResponse.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var item = Assert.Single(worklist!.RootElement.GetProperty("invitations").EnumerateArray());
        Assert.Equal(olderRef, item.GetProperty("invitationRef").GetGuid());
        Assert.Equal(olderDraft, item.GetProperty("draftRef").GetGuid());
        Assert.Equal("Pending", item.GetProperty("state").GetString());
        Assert.Equal("Draft", item.GetProperty("draftState").GetString());
        Assert.Equal(JsonValueKind.Null, worklist.RootElement.GetProperty("nextBefore").ValueKind);
        var body = worklist.RootElement.GetRawText();
        Assert.DoesNotContain(olderToken, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recipientSubject", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requestId", body, StringComparison.OrdinalIgnoreCase);

        using var scoped = await owner.GetFromJsonAsync<JsonDocument>(
            $"{activity}?draftRef={olderDraft:D}&state=Pending", ct);
        Assert.Equal(olderRef, Assert.Single(scoped!.RootElement.GetProperty("invitations").EnumerateArray())
            .GetProperty("invitationRef").GetGuid());
        using var otherScoped = await other.GetFromJsonAsync<JsonDocument>(
            $"{activity}?draftRef={olderDraft:D}&state=Pending", ct);
        Assert.Empty(otherScoped!.RootElement.GetProperty("invitations").EnumerateArray());
        using var strangerExact = await other.GetFromJsonAsync<JsonDocument>(
            $"{activity}?invitationRef={olderRef:D}", ct);
        Assert.Empty(strangerExact!.RootElement.GetProperty("invitations").EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(activity + "?state=Pending", ct)).StatusCode);

        foreach (var bad in new[] {
            "?state=Accepted", "?state=", "?state=Pending&state=Pending",
            "?draftRef=" + Guid.Empty, "?draftRef=", "?before=0&state=Pending",
            "?invitationRef=" + olderRef.ToString("D") + "&state=Pending",
            "?invitationRef=" + olderRef.ToString("D") + "&draftRef=" + olderDraft.ToString("D"),
            "?draftRef=" + olderDraft.ToString("D") + "&draftRef=" + olderDraft.ToString("D"),
            "?unexpected=1&state=Pending"
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(activity + bad, ct)).StatusCode);

        Assert.Equal("Revoked", await State(await owner.PostAsync(
            $"{olderPath}/{olderRef:D}/revoke", null, ct), ct));
        using var pendingAfter = await owner.GetFromJsonAsync<JsonDocument>(activity + "?state=Pending", ct);
        Assert.Empty(pendingAfter!.RootElement.GetProperty("invitations").EnumerateArray());
        using var exact = await owner.GetFromJsonAsync<JsonDocument>(
            $"{activity}?invitationRef={olderRef:D}", ct);
        Assert.Equal("Revoked", Assert.Single(exact!.RootElement.GetProperty("invitations").EnumerateArray())
            .GetProperty("state").GetString());
        Assert.Equal(22, terminalRefs.Count);
        using var scopes = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/appointment-scopes", ct);
        Assert.Empty(scopes!.RootElement.GetProperty("appointmentScopes").EnumerateArray());
    }

    [Fact]
    public async Task OwnerCanRevokePendingTokenWithoutArchivingDraftOrExposingRecipients()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("revoke-owner-" + Guid.NewGuid().ToString("N"));
        using var outsider = setup.AuthenticatedIdentityClient("revoke-foreign-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("revoke-recipient-" + Guid.NewGuid().ToString("N"));
        using var anonymous = setup.Factory.CreateClient();
        var draft = await Draft(owner, ct); var token = Token(); var requestId = Guid.NewGuid();
        var invitationPath = $"{Drafts}/{draft:D}/invitations";
        using var issue = await owner.PostAsJsonAsync(invitationPath, new { requestId, token }, ct);
        Assert.Equal("Pending", await State(issue, ct));
        using var issued = await issue.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var invitationRef = issued!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
        var revokePath = $"{invitationPath}/{invitationRef:D}/revoke";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(revokePath, null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsync(revokePath, null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync(
            $"{invitationPath}/{Guid.NewGuid():D}/revoke", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(revokePath,
            new { recipientSubject = "forged", state = "Accepted" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(
            $"{invitationPath}/{Guid.Empty:D}/revoke", null, ct)).StatusCode);

        using var revoke = await owner.PostAsync(revokePath, null, ct);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal("no-store", revoke.Headers.CacheControl?.ToString());
        var result = await revoke.Content.ReadAsStringAsync(ct);
        using var parsed = JsonDocument.Parse(result);
        Assert.Equal("Applied", parsed.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(invitationRef, parsed.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid());
        Assert.Equal("Revoked", parsed.RootElement.GetProperty("invitation").GetProperty("state").GetString());
        Assert.DoesNotContain(token, result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recipientIssuer", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", result, StringComparison.OrdinalIgnoreCase);
        using var replay = await owner.PostAsync(revokePath, null, ct);
        Assert.Equal("Revoked", await State(replay, ct));
        using var replayBody = await replay.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Replayed", replayBody!.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("Revoked", await State(await recipient.PostAsJsonAsync(
            Preview, new { token }, ct), ct));
        Assert.Equal(HttpStatusCode.Conflict, (await recipient.PostAsJsonAsync(Respond,
            new { token, decision = "Accepted", consentVersion = "group-invitation-v1" }, ct)).StatusCode);
        Assert.Equal("Revoked", await State(await owner.PostAsJsonAsync(
            invitationPath, new { requestId, token }, ct), ct));

        using var draftReceipt = await owner.GetFromJsonAsync<JsonDocument>(
            $"{Drafts}?draftRef={draft:D}", ct);
        Assert.Equal("Draft", Assert.Single(draftReceipt!.RootElement.GetProperty("drafts").EnumerateArray())
            .GetProperty("state").GetString());
        using var activity = await owner.GetFromJsonAsync<JsonDocument>(
            $"/api/v1/me/group-invitation-activity?invitationRef={invitationRef:D}", ct);
        Assert.Equal("Revoked", Assert.Single(activity!.RootElement.GetProperty("invitations").EnumerateArray())
            .GetProperty("state").GetString());
        using var scopes = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/appointment-scopes", ct);
        Assert.Empty(scopes!.RootElement.GetProperty("appointmentScopes").EnumerateArray());
    }

    [Fact]
    public async Task RevocationSerializesWithConsentAndDoesNotAlterFinalOrExpiredStates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("revoke-race-owner-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("revoke-race-recipient-" + Guid.NewGuid().ToString("N"));
        var draft = await Draft(owner, ct);
        var path = $"{Drafts}/{draft:D}/invitations";
        var token = Token();
        using var issue = await owner.PostAsJsonAsync(path,
            new { requestId = Guid.NewGuid(), token }, ct);
        Assert.Equal("Pending", await State(issue, ct));
        using var issued = await issue.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var id = issued!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
        var revokePath = $"{path}/{id:D}/revoke";
        var two = await Task.WhenAll(
            owner.PostAsync(revokePath, null, ct),
            recipient.PostAsJsonAsync(Respond,
                new { token, decision = "Accepted", consentVersion = "group-invitation-v1" }, ct));
        Assert.Equal(1, two.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, two.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        using var latest = await owner.GetFromJsonAsync<JsonDocument>(path, ct);
        var last = latest!.RootElement.GetProperty("invitation").GetProperty("state").GetString();
        Assert.Contains(last, new[] { "Accepted", "Revoked" });
        if (last == "Accepted")
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync(revokePath, null, ct)).StatusCode);
        else
            Assert.Equal("Revoked", await State(await owner.PostAsync(revokePath, null, ct), ct));
        foreach (var response in two) response.Dispose();

        var declinedDraft = await Draft(owner, ct); var declinedToken = Token();
        var declinedPath = $"{Drafts}/{declinedDraft:D}/invitations";
        using var declinedIssue = await owner.PostAsJsonAsync(declinedPath,
            new { requestId = Guid.NewGuid(), token = declinedToken }, ct);
        using var declinedData = await declinedIssue.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var declinedId = declinedData!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
        Assert.Equal("Declined", await State(await recipient.PostAsJsonAsync(Respond,
            new { token = declinedToken, decision = "Declined", consentVersion = "group-invitation-v1" }, ct), ct));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync(
            $"{declinedPath}/{declinedId:D}/revoke", null, ct)).StatusCode);

        var expiredDraft = await Draft(owner, ct); var expiredToken = Token();
        var expiredPath = $"{Drafts}/{expiredDraft:D}/invitations";
        using var expiredIssue = await owner.PostAsJsonAsync(expiredPath,
            new { requestId = Guid.NewGuid(), token = expiredToken }, ct);
        using var expiredData = await expiredIssue.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var expiredId = expiredData!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
        await using (var db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!))
        await using (var expire = db.CreateCommand("""
            UPDATE taranom_member_group_invitation
            SET created_at=now()-interval '8 days', expires_at=now()-interval '1 day'
            WHERE invitation_ref=$1
            """))
        {
            expire.Parameters.AddWithValue(expiredId);
            Assert.Equal(1, await expire.ExecuteNonQueryAsync(ct));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync(
            $"{expiredPath}/{expiredId:D}/revoke", null, ct)).StatusCode);
        Assert.Equal("Expired", await State(await recipient.PostAsJsonAsync(
            Preview, new { token = expiredToken }, ct), ct));
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsync($"{Drafts}/{expiredDraft:D}/withdraw", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync(
            $"{expiredPath}/{expiredId:D}/revoke", null, ct)).StatusCode);
    }

}
