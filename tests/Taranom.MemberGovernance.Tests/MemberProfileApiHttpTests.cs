using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class MemberProfileApiHttpTests
{
    [Fact]
    public async Task ProfileIsOwnedByAuthenticatedIdentityAndDoesNotActivateMembership()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var anonymous = setup.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/me/profile", ct)).StatusCode);
        var subject = "profile-owner-" + Guid.NewGuid().ToString("N");
        using var owner = setup.AuthenticatedIdentityClient(subject);
        using var other = setup.AuthenticatedIdentityClient("profile-other-" + Guid.NewGuid().ToString("N"));
        var saved = await owner.PutAsJsonAsync("/api/v1/me/profile", new
        {
            requestId = Guid.NewGuid(), expectedRevision = 0, fullName = "نام آزمایشی",
            birthDate = "1990-03-21", city = "تهران"
        }, ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var read = await owner.GetAsync("/api/v1/me/profile", ct);
        Assert.Equal("no-store", read.Headers.CacheControl?.ToString());
        using var json = await read.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("نام آزمایشی", json!.RootElement.GetProperty("profile").GetProperty("fullName").GetString());
        var otherRead = await other.GetAsync("/api/v1/me/profile?subject=" + subject, ct);
        using var otherJson = await otherRead.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal(0, otherJson!.RootElement.GetProperty("profile").GetProperty("revision").GetInt64());
        var scopes = await owner.GetAsync("/api/v1/me/appointment-scopes", ct);
        using var scopesJson = await scopes.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Empty(scopesJson!.RootElement.GetProperty("appointmentScopes").EnumerateArray());
    }

    [Fact]
    public async Task RetriesReplayButChangedPayloadOrStaleRevisionCannotOverwrite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("profile-replay-" + Guid.NewGuid().ToString("N"));
        var requestId = Guid.NewGuid();
        var payload = new { requestId, expectedRevision = 0, fullName = "Profile test", birthDate = (string?)null, city = "" };
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/v1/me/profile", payload, ct)).StatusCode);
        var retry = await owner.PutAsJsonAsync("/api/v1/me/profile", payload, ct);
        using var json = await retry.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Replayed", json!.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/v1/me/profile",
            new { requestId, expectedRevision = 0, fullName = "Changed", birthDate = (string?)null, city = "" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/v1/me/profile",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, fullName = "Stale", birthDate = (string?)null, city = "" }, ct)).StatusCode);
        using var current = await owner.GetFromJsonAsync<JsonDocument>("/api/v1/me/profile", ct);
        Assert.Equal(1, current!.RootElement.GetProperty("profile").GetProperty("revision").GetInt64());
        Assert.Equal("Profile test", current.RootElement.GetProperty("profile").GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task InvalidBirthDatesAndLockedIdentityFieldsAreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("profile-invalid-" + Guid.NewGuid().ToString("N"));
        foreach (var birth in new[] { "1899-01-01", "2999-01-01" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/v1/me/profile",
                new { requestId = Guid.NewGuid(), expectedRevision = 0, fullName = "Test", birthDate = birth, city = "" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/v1/me/profile",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, fullName = "Test", birthDate = (string?)null, city = "", nationalId = "unverified" }, ct)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentEditsApplyOnlyOneRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        using var owner = setup.AuthenticatedIdentityClient("profile-race-" + Guid.NewGuid().ToString("N"));
        var tasks = Enumerable.Range(1, 2).Select(i => owner.PutAsJsonAsync("/api/v1/me/profile",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, fullName = "Writer " + i, birthDate = (string?)null, city = "" }, ct));
        var responses = await Task.WhenAll(tasks);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
    }
}
