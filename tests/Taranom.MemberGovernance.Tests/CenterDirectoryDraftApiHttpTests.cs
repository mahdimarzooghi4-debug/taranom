using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class CenterDirectoryDraftApiHttpTests
{
    [Fact]
    public async Task OnlyScopedCenterManagerMayWriteUnverifiedProfileWithAuditedReplay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        var center = "center-draft-" + Guid.NewGuid().ToString("N");
        var other = "center-other-" + Guid.NewGuid().ToString("N");
        var subject = "center-manager-" + Guid.NewGuid().ToString("N");
        var unrelated = "center-unrelated-" + Guid.NewGuid().ToString("N");
        var url = $"/api/v1/centers/{center}/directory-draft";

        using var anonymous = setup.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(url, ct)).StatusCode);

        using var manager = setup.AuthenticatedIdentityClient(subject);
        using var unrelatedClient = setup.AuthenticatedIdentityClient(unrelated);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync(url, ct)).StatusCode);
        await setup.GrantAsync(subject, "CenterAccessGrantsManage", center, ct);
        await setup.GrantAsync(unrelated, "CenterAccessGrantsManage", other, ct);

        var initial = await manager.GetAsync(url, ct);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal("no-store", initial.Headers.CacheControl?.ToString());
        using var initialDoc = await initial.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal(0, initialDoc!.RootElement.GetProperty("profile").GetProperty("revision").GetInt64());
        Assert.Equal("UNVERIFIED_DRAFT", initialDoc.RootElement.GetProperty("verificationState").GetString());

        var id = Guid.NewGuid();
        var payload = new { requestId = id, expectedRevision = 0L,
            displayName = "مرکز آزمایشی نام‌گذاری‌شده", city = "تهران" };
        var first = await manager.PutAsJsonAsync(url, payload, ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var created = await first.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Applied", created!.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(1, created.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("UNVERIFIED_DRAFT", created.RootElement.GetProperty("verificationState").GetString());

        var same = await manager.PutAsJsonAsync(url, payload, ct);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        using var replayed = await same.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Replayed", replayed!.RootElement.GetProperty("outcome").GetString());

        Assert.Equal(HttpStatusCode.Conflict,
            (await manager.PutAsJsonAsync(url, new {
                requestId = id, expectedRevision = 0L,
                displayName = "نام متفاوت", city = "تهران" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await manager.PutAsJsonAsync(url, new {
                requestId = Guid.NewGuid(), expectedRevision = 0L,
                displayName = "نام متفاوت", city = "تهران" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await unrelatedClient.GetAsync(url, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"/api/v1/centers/{other}/directory-draft", ct)).StatusCode);

        var saved = await manager.GetAsync(url, ct);
        using var savedDoc = await saved.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var current = savedDoc!.RootElement.GetProperty("profile");
        Assert.Equal(1, current.GetProperty("revision").GetInt64());
        Assert.Equal("تهران", current.GetProperty("city").GetString());
        Assert.Equal("مرکز آزمایشی نام‌گذاری‌شده", current.GetProperty("displayName").GetString());

        // The only durable interpretation is a center-claimed draft.
        using var member = setup.AuthenticatedIdentityClient("member-no-directory-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.GetAsync(url, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.GetAsync($"/api/v1/centers/{center}/doctor-drafts", ct)).StatusCode);

        await using var db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var query = new NpgsqlCommand("""
            SELECT count(*) FROM taranom_center_directory_draft_audit
            WHERE center_ref=@center AND actor_subject=@subject
            """, conn);
        query.Parameters.AddWithValue("center", center);
        query.Parameters.AddWithValue("subject", subject);
        Assert.Equal(1L, (long)(await query.ExecuteScalarAsync(ct))!);
    }


    [Fact]
    public async Task StaleSessionFlagDeniesDirectoryAndMemberScopeEvenWithOtherwiseCurrentGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        var center = "center-session-" + Guid.NewGuid().ToString("N");
        var managerSubject = "center-session-manager-" + Guid.NewGuid().ToString("N");
        var memberSubject = "center-session-member-" + Guid.NewGuid().ToString("N");
        await setup.GrantAsync(managerSubject, "CenterAccessGrantsManage", center, ct);
        await setup.GrantAsync(memberSubject, "MemberAppointmentsManage",
            "member-session-" + Guid.NewGuid().ToString("N"), ct);

        using var manager = setup.AuthenticatedIdentityClient(managerSubject);
        using var member = setup.AuthenticatedIdentityClient(memberSubject);
        Assert.Equal(HttpStatusCode.OK,
            (await manager.GetAsync($"/api/v1/centers/{center}/directory-draft", ct)).StatusCode);

        await using var db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using (var stale = new NpgsqlCommand("""
            UPDATE taranom_panel_access_grant
               SET session_current=false
             WHERE issuer=@issuer AND subject IN (@manager,@member)
            """, conn))
        {
            stale.Parameters.AddWithValue("issuer", "https://appointments.test.invalid/realms/taranom");
            stale.Parameters.AddWithValue("manager", managerSubject);
            stale.Parameters.AddWithValue("member", memberSubject);
            Assert.Equal(2, await stale.ExecuteNonQueryAsync(ct));
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"/api/v1/centers/{center}/directory-draft", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PutAsJsonAsync($"/api/v1/centers/{center}/directory-draft",
                new { requestId = Guid.NewGuid(), expectedRevision = 0,
                    displayName = "مرکز غیرمجاز", city = "تهران" }, ct)).StatusCode);

        using var memberScopes = await member.GetAsync("/api/v1/me/appointment-scopes", ct);
        Assert.Equal(HttpStatusCode.OK, memberScopes.StatusCode);
        using var scopesDoc = await memberScopes.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Empty(scopesDoc!.RootElement.GetProperty("appointmentScopes").EnumerateArray());

        using var directoryScopes = await manager.GetAsync("/api/v1/me/center-directory-draft-scopes", ct);
        Assert.Equal(HttpStatusCode.OK, directoryScopes.StatusCode);
        using var directoryDoc = await directoryScopes.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Empty(directoryDoc!.RootElement.GetProperty("scopes").EnumerateArray());

        using var appointments = await member.GetAsync("/api/v1/me/appointments", ct);
        Assert.Equal(HttpStatusCode.OK, appointments.StatusCode);
        using var own = await appointments.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Empty(own!.RootElement.GetProperty("appointments").EnumerateArray());
    }

    [Fact]
    public async Task DoctorDraftsAreCenterScopedUnverifiedAndRequireCurrentRosterGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(ct);
        var center = "center-roster-" + Guid.NewGuid().ToString("N");
        var otherCenter = "center-roster-other-" + Guid.NewGuid().ToString("N");
        var subject = "roster-writer-" + Guid.NewGuid().ToString("N");
        var managerSubject = "roster-manager-" + Guid.NewGuid().ToString("N");
        using var staff = setup.AuthenticatedIdentityClient(subject);
        using var manager = setup.AuthenticatedIdentityClient(managerSubject);

        await setup.GrantAsync(subject, "CenterRosterManage", center, ct);
        await setup.GrantAsync(managerSubject, "CenterAccessGrantsManage", center, ct);
        var scopes = await staff.GetAsync("/api/v1/me/center-directory-draft-scopes", ct);
        Assert.Equal(HttpStatusCode.OK, scopes.StatusCode);
        using var scopeDoc = await scopes.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Contains(scopeDoc!.RootElement.GetProperty("scopes").EnumerateArray(),
            item => item.GetProperty("capability").GetString() == "CenterRosterManage" &&
                item.GetProperty("scopeRef").GetString() == center);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"/api/v1/centers/{center}/doctor-drafts", ct)).StatusCode);

        var url = $"/api/v1/centers/{center}/doctor-drafts/doctor-draft-1";
        var id = Guid.NewGuid();
        var payload = new { requestId = id, expectedRevision = 0L,
            displayName = "پزشک معرفی‌شده", specialty = "تخصص ادعایی مرکز" };
        var first = await staff.PutAsJsonAsync(url, payload, ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var created = await first.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("UNVERIFIED_DRAFT", created!.RootElement.GetProperty("verificationState").GetString());
        Assert.Equal(1L, created.RootElement.GetProperty("revision").GetInt64());

        var replay = await staff.PutAsJsonAsync(url, payload, ct);
        using var replayDoc = await replay.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Replayed", replayDoc!.RootElement.GetProperty("outcome").GetString());

        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PutAsJsonAsync(url, new { requestId = id,
                expectedRevision = 0L, displayName = "متفاوت", specialty = "تخصص" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PutAsJsonAsync(url, new { requestId = Guid.NewGuid(),
                expectedRevision = 0L, displayName = "متفاوت", specialty = "تخصص" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PutAsJsonAsync($"/api/v1/centers/{otherCenter}/doctor-drafts/doctor-draft-1",
                payload, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await staff.PutAsJsonAsync(url, new { requestId = Guid.NewGuid(), expectedRevision = 1L,
                displayName = "", specialty = "تخصص" }, ct)).StatusCode);

        var listed = await staff.GetAsync($"/api/v1/centers/{center}/doctor-drafts", ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var listedDoc = await listed.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var doctor = Assert.Single(listedDoc!.RootElement.GetProperty("doctors").EnumerateArray());
        Assert.Equal("doctor-draft-1", doctor.GetProperty("doctorRef").GetString());
        Assert.Equal("UNVERIFIED_DRAFT", doctor.GetProperty("verificationState").GetString());
        Assert.Equal("پزشک معرفی‌شده", doctor.GetProperty("displayName").GetString());

        await using var db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using (var revoke = new NpgsqlCommand("""
            UPDATE taranom_panel_access_grant SET grant_state='revoked',grant_not_revoked=false
            WHERE issuer=@issuer AND subject=@subject
              AND capability='CenterRosterManage' AND scope_ref=@center
            """, conn))
        {
            revoke.Parameters.AddWithValue("issuer", "https://appointments.test.invalid/realms/taranom");
            revoke.Parameters.AddWithValue("subject", subject);
            revoke.Parameters.AddWithValue("center", center);
            Assert.Equal(1, await revoke.ExecuteNonQueryAsync(ct));
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.GetAsync($"/api/v1/centers/{center}/doctor-drafts", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PutAsJsonAsync(url, new { requestId = Guid.NewGuid(),
                expectedRevision = 1L, displayName = "another", specialty = "another" }, ct)).StatusCode);
    }
}
