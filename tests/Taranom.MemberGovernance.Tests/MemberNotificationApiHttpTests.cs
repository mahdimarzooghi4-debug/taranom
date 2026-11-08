using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Npgsql;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class MemberNotificationApiHttpTests
{
    [Fact]
    public async Task ReplyEmitsExactlyOnePrivateNotificationAndReadIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("notify-owner-" + Guid.NewGuid().ToString("N"));
        using var other = setup.AuthenticatedIdentityClient("notify-other-" + Guid.NewGuid().ToString("N"));
        using var anonymous = setup.Factory.CreateClient();
        var staffId = "notify-staff-" + Guid.NewGuid().ToString("N");
        using var staff = setup.AuthenticatedIdentityClient(staffId);
        await setup.GrantAsync(staffId, "SupportRequestsManage", "taranom-support", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/me/notifications", ct)).StatusCode);
        using var initial = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/notifications", ct);
        Assert.Empty(initial!.RootElement.GetProperty("notifications").EnumerateArray());
        var requestId = Guid.NewGuid();
        var created = await owner.PostAsJsonAsync("/api/v1/me/support-requests",
            new { requestId, topic = "account", message = "Synthetic request for notification test" }, ct);
        using var createJson = await created.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var reference = createJson!.RootElement.GetProperty("request").GetProperty("requestRef").GetGuid();
        var reply = new { requestId = Guid.NewGuid(), expectedRevision = 0, reply = "Synthetic private answer not included in notification" };
        var replyPath = $"/api/v1/operations/support-requests/{reference:D}/reply";
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync(replyPath, reply, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync(replyPath, reply, ct)).StatusCode);
        using var response = await owner.GetAsync("/api/v1/me/notifications", ct);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var text = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(reply.reply, text);
        using var document = JsonDocument.Parse(text);
        var item = Assert.Single(document.RootElement.GetProperty("notifications").EnumerateArray());
        Assert.Equal(reference, item.GetProperty("notificationRef").GetGuid());
        Assert.Equal(requestId, item.GetProperty("sourceRequestId").GetGuid());
        Assert.Equal("SupportAnswered", item.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("readAt").ValueKind);
        Assert.Equal(1L, document.RootElement.GetProperty("unreadCount").GetInt64());
        var readPath = $"/api/v1/me/notifications/{reference:D}/read";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(readPath, null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync(readPath, null, ct)).StatusCode);
        using var cross = await other.GetFromJsonAsync<JsonDocument>($"/api/v1/me/notifications?notificationRef={reference:D}&subject=another-user", ct);
        Assert.Empty(cross!.RootElement.GetProperty("notifications").EnumerateArray());
        var reads = await Task.WhenAll(owner.PostAsync(readPath, null, ct), owner.PostAsync(readPath, null, ct));
        var stamps = new List<DateTimeOffset>();
        foreach (var read in reads)
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var receipt = await read.Content.ReadFromJsonAsync<JsonDocument>(ct);
            stamps.Add(receipt!.RootElement.GetProperty("readAt").GetDateTimeOffset());
            read.Dispose();
        }
        Assert.Equal(stamps[0], stamps[1]);
        using var final = await owner.GetFromJsonAsync<JsonDocument>($"/api/v1/me/notifications?notificationRef={reference:D}", ct);
        Assert.Equal(0L, final!.RootElement.GetProperty("unreadCount").GetInt64());
        Assert.Equal(stamps[0], Assert.Single(final.RootElement.GetProperty("notifications").EnumerateArray()).GetProperty("readAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task PagesAreBoundedAndGlobalUnreadCountDoesNotDependOnCursor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("notify-pages-" + Guid.NewGuid().ToString("N"));
        var staffId = "notify-pages-staff-" + Guid.NewGuid().ToString("N");
        using var staff = setup.AuthenticatedIdentityClient(staffId);
        await setup.GrantAsync(staffId, "SupportRequestsManage", "taranom-support", ct);
        for (var i = 0; i < 22; i++)
        {
            var created = await owner.PostAsJsonAsync("/api/v1/me/support-requests",
                new { requestId = Guid.NewGuid(), topic = "privacy", message = "Synthetic pagination support request " + i }, ct);
            using var receipt = await created.Content.ReadFromJsonAsync<JsonDocument>(ct);
            var reference = receipt!.RootElement.GetProperty("request").GetProperty("requestRef").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/v1/operations/support-requests/{reference:D}/reply",
                new { requestId = Guid.NewGuid(), expectedRevision = 0, reply = "Synthetic pagination answer " + i }, ct)).StatusCode);
        }
        using var first = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/notifications", ct);
        var page1 = first!.RootElement.GetProperty("notifications").EnumerateArray().Select(n => n.GetProperty("notificationRef").GetGuid()).ToArray();
        Assert.Equal(20, page1.Length);
        var cursor = first.RootElement.GetProperty("nextBefore").GetString();
        using var second = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/notifications?before=" + cursor, ct);
        var page2 = second!.RootElement.GetProperty("notifications").EnumerateArray().Select(n => n.GetProperty("notificationRef").GetGuid()).ToArray();
        Assert.Equal(2, page2.Length);
        Assert.Empty(page1.Intersect(page2));
        Assert.Equal(22L, second.RootElement.GetProperty("unreadCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextBefore").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/me/notifications?before=0", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/v1/me/notifications/{Guid.NewGuid():D}/read", null, ct)).StatusCode);
    }

    [Fact]
    public async Task ExistingAnsweredTicketIsBackfilledWhenApiRestarts()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = "notify-migration-" + Guid.NewGuid().ToString("N");
        Guid reference;
        await using (var setup = new AppointmentApiFixture(ct))
        {
            using var owner = setup.AuthenticatedIdentityClient(ownerId);
            var staffId = "notify-migration-staff-" + Guid.NewGuid().ToString("N");
            using var staff = setup.AuthenticatedIdentityClient(staffId);
            await setup.GrantAsync(staffId, "SupportRequestsManage", "taranom-support", ct);
            var created = await owner.PostAsJsonAsync("/api/v1/me/support-requests",
                new { requestId = Guid.NewGuid(), topic = "account", message = "Synthetic legacy answered request" }, ct);
            using var receipt = await created.Content.ReadFromJsonAsync<JsonDocument>(ct);
            reference = receipt!.RootElement.GetProperty("request").GetProperty("requestRef").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/v1/operations/support-requests/{reference:D}/reply",
                new { requestId = Guid.NewGuid(), expectedRevision = 0, reply = "Synthetic legacy answer for migration" }, ct)).StatusCode);
            await using var db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
            await using var command = db.CreateCommand("DELETE FROM taranom_member_support_notification WHERE request_ref=@ref");
            command.Parameters.AddWithValue("ref", reference);
            await command.ExecuteNonQueryAsync(ct);
        }
        await using var restarted = new AppointmentApiFixture(ct);
        using var client = restarted.AuthenticatedIdentityClient(ownerId);
        using var page = await client.GetFromJsonAsync<JsonDocument>("/api/v1/me/notifications", ct);
        Assert.Equal(reference, Assert.Single(page!.RootElement.GetProperty("notifications").EnumerateArray()).GetProperty("notificationRef").GetGuid());
    }

    [Fact]
    public async Task FinalInvitationResponseEmitsOnePrivateOwnerNotificationAndKeepsSupportEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("response-notify-owner-" + Guid.NewGuid().ToString("N"));
        using var recipient = setup.AuthenticatedIdentityClient("response-notify-recipient-" + Guid.NewGuid().ToString("N"));
        using var other = setup.AuthenticatedIdentityClient("response-notify-foreign-" + Guid.NewGuid().ToString("N"));
        using var anonymous = setup.Factory.CreateClient();

        const string notifications = "/api/v1/me/notifications";
        using var created = await owner.PostAsJsonAsync("/api/v1/me/group-invitation-drafts",
            new { requestId = Guid.NewGuid(), method = "ExistingMember", recipientLabel = "Private synthetic planning label" }, ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var draftJson = await created.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var draft = draftJson!.RootElement.GetProperty("draft").GetProperty("draftRef").GetGuid();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var issuePath = $"/api/v1/me/group-invitation-drafts/{draft:D}/invitations";
        using var issued = await owner.PostAsJsonAsync(issuePath, new { requestId = Guid.NewGuid(), token }, ct);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var issueJson = await issued.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var invitation = issueJson!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsJsonAsync(
            "/api/v1/me/group-invitations/preview", new { token }, ct)).StatusCode);
        using (var before = await owner.GetFromJsonAsync<JsonDocument>(notifications, ct))
            Assert.Empty(before!.RootElement.GetProperty("notifications").EnumerateArray());

        var answer = new { token, decision = "Accepted", consentVersion = "group-invitation-v1" };
        var responses = await Task.WhenAll(
            recipient.PostAsJsonAsync("/api/v1/me/group-invitations/respond", answer, ct),
            recipient.PostAsJsonAsync("/api/v1/me/group-invitations/respond", answer, ct));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }
        using var feed = await owner.GetAsync(notifications, ct);
        Assert.Equal("no-store", feed.Headers.CacheControl?.ToString());
        var content = await feed.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(token, content);
        Assert.DoesNotContain("Private synthetic planning label", content);
        Assert.DoesNotContain("recipientSubject", content);
        Assert.DoesNotContain("recipientIssuer", content);
        using var json = JsonDocument.Parse(content);
        var item = Assert.Single(json.RootElement.GetProperty("notifications").EnumerateArray());
        Assert.Equal(invitation, item.GetProperty("notificationRef").GetGuid());
        Assert.Equal(invitation, item.GetProperty("sourceInvitationRef").GetGuid());
        Assert.Equal("GroupInvitationResponded", item.GetProperty("kind").GetString());
        Assert.Equal("Accepted", item.GetProperty("responseState").GetString());
        Assert.False(item.TryGetProperty("sourceRequestId", out _));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("readAt").ValueKind);
        Assert.Equal(1L, json.RootElement.GetProperty("unreadCount").GetInt64());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(notifications, ct)).StatusCode);
        using var foreign = await other.GetFromJsonAsync<JsonDocument>(
            $"{notifications}?notificationRef={invitation:D}", ct);
        Assert.Empty(foreign!.RootElement.GetProperty("notifications").EnumerateArray());
        using var recipientFeed = await recipient.GetFromJsonAsync<JsonDocument>(notifications, ct);
        Assert.Empty(recipientFeed!.RootElement.GetProperty("notifications").EnumerateArray());

        var ack = $"{notifications}/{invitation:D}/read";
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync(ack, null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(ack, null, ct)).StatusCode);
        var acks = await Task.WhenAll(owner.PostAsync(ack, null, ct), owner.PostAsync(ack, null, ct));
        var timestamps = new List<DateTimeOffset>();
        foreach (var result in acks)
        {
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            using var receipt = await result.Content.ReadFromJsonAsync<JsonDocument>(ct);
            timestamps.Add(receipt!.RootElement.GetProperty("readAt").GetDateTimeOffset());
            result.Dispose();
        }
        Assert.Equal(timestamps[0], timestamps[1]);

        var staffId = "response-notify-staff-" + Guid.NewGuid().ToString("N");
        using var staff = setup.AuthenticatedIdentityClient(staffId);
        await setup.GrantAsync(staffId, "SupportRequestsManage", "taranom-support", ct);
        var supportRequestId = Guid.NewGuid();
        using var support = await owner.PostAsJsonAsync("/api/v1/me/support-requests",
            new { requestId = supportRequestId, topic = "group", message = "Synthetic owner support case" }, ct);
        Assert.Equal(HttpStatusCode.OK, support.StatusCode);
        using var supportJson = await support.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var requestRef = supportJson!.RootElement.GetProperty("request").GetProperty("requestRef").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync(
            $"/api/v1/operations/support-requests/{requestRef:D}/reply",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, reply = "Synthetic owner answer retained privately" }, ct)).StatusCode);

        using var mixed = await owner.GetFromJsonAsync<JsonDocument>(notifications, ct);
        var events = mixed!.RootElement.GetProperty("notifications").EnumerateArray().ToArray();
        Assert.Equal(2, events.Length);
        Assert.Equal("SupportAnswered", events[0].GetProperty("kind").GetString());
        Assert.Equal(supportRequestId, events[0].GetProperty("sourceRequestId").GetGuid());
        Assert.Equal("GroupInvitationResponded", events[1].GetProperty("kind").GetString());
        Assert.Equal(1L, mixed.RootElement.GetProperty("unreadCount").GetInt64());

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync(
            $"/api/v1/me/group-invitation-drafts/{draft:D}/withdraw", null, ct)).StatusCode);
        using var retained = await owner.GetFromJsonAsync<JsonDocument>(
            $"{notifications}?notificationRef={invitation:D}", ct);
        var old = Assert.Single(retained!.RootElement.GetProperty("notifications").EnumerateArray());
        Assert.Equal("Accepted", old.GetProperty("responseState").GetString());
        Assert.Equal(timestamps[0], old.GetProperty("readAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task DeclinedResponseIsBackfilledWithoutInventingAdditionalNotificationOrOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = "response-backfill-owner-" + Guid.NewGuid().ToString("N");
        var recipientId = "response-backfill-recipient-" + Guid.NewGuid().ToString("N");
        Guid invitation;
        await using (var setup = new AppointmentApiFixture(ct))
        {
            using var owner = setup.AuthenticatedIdentityClient(ownerId);
            using var recipient = setup.AuthenticatedIdentityClient(recipientId);
            using var draftPost = await owner.PostAsJsonAsync("/api/v1/me/group-invitation-drafts",
                new { requestId = Guid.NewGuid(), method = "NewRegistration", recipientLabel = "Private unverified recipient name" }, ct);
            using var draftJson = await draftPost.Content.ReadFromJsonAsync<JsonDocument>(ct);
            var draft = draftJson!.RootElement.GetProperty("draft").GetProperty("draftRef").GetGuid();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            using var issue = await owner.PostAsJsonAsync(
                $"/api/v1/me/group-invitation-drafts/{draft:D}/invitations",
                new { requestId = Guid.NewGuid(), token }, ct);
            using var issueJson = await issue.Content.ReadFromJsonAsync<JsonDocument>(ct);
            invitation = issueJson!.RootElement.GetProperty("invitation").GetProperty("invitationRef").GetGuid();
            var command = new { token, decision = "Declined", consentVersion = "group-invitation-v1" };
            Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsJsonAsync(
                "/api/v1/me/group-invitations/respond", command, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsJsonAsync(
                "/api/v1/me/group-invitations/respond", command, ct)).StatusCode);
            using var first = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/notifications", ct);
            var record = Assert.Single(first!.RootElement.GetProperty("notifications").EnumerateArray());
            Assert.Equal("Declined", record.GetProperty("responseState").GetString());
            Assert.Equal(1L, first.RootElement.GetProperty("unreadCount").GetInt64());
            await using var db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
            await using var remove = db.CreateCommand(
                "DELETE FROM taranom_member_notification_journal WHERE kind='GroupInvitationResponded' AND source_ref=@ref");
            remove.Parameters.AddWithValue("ref", invitation);
            Assert.Equal(1, await remove.ExecuteNonQueryAsync(ct));
        }
        await using var restarted = new AppointmentApiFixture(ct);
        using var recoveredOwner = restarted.AuthenticatedIdentityClient(ownerId);
        using var recoveredRecipient = restarted.AuthenticatedIdentityClient(recipientId);
        using var restored = await recoveredOwner.GetFromJsonAsync<JsonDocument>("/api/v1/me/notifications", ct);
        var row = Assert.Single(restored!.RootElement.GetProperty("notifications").EnumerateArray());
        Assert.Equal(invitation, row.GetProperty("notificationRef").GetGuid());
        Assert.Equal("Declined", row.GetProperty("responseState").GetString());
        Assert.Equal(1L, restored.RootElement.GetProperty("unreadCount").GetInt64());
        using var wrongOwner = await recoveredRecipient.GetFromJsonAsync<JsonDocument>(
            $"/api/v1/me/notifications?notificationRef={invitation:D}", ct);
        Assert.Empty(wrongOwner!.RootElement.GetProperty("notifications").EnumerateArray());
    }
}
