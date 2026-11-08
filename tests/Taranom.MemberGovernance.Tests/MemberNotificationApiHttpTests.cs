using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
}
