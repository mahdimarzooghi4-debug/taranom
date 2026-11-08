using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class CenterManagerCaseWorkspaceApiHttpTests
{
    [Fact]
    public async Task AdminCaseWorkspacePinsRealCenterAndClaimsWithoutVerifyingAuthority()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fx = new AppointmentApiFixture(ct);
        var adminSubject = "admin-case-" + Guid.NewGuid().ToString("N");
        await fx.GrantAsync(adminSubject, "AdminCenterRegistryManage",
            "taranom-center-registry", ct);
        using var admin = fx.AuthenticatedIdentityClient(adminSubject);
        using var create = await admin.PostAsJsonAsync("/api/v1/operations/centers",
            new { requestId=Guid.NewGuid(), displayName="مرکز ثبت‌شده ادمین",
                city="اراک" }, ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var created = await create.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var centerRef = created!.RootElement.GetProperty("centerRef").GetString()!;

        var applicantSubject = "manager-case-" + Guid.NewGuid().ToString("N");
        using var applicant = fx.AuthenticatedIdentityClient(applicantSubject);
        var id = Guid.NewGuid();
        var reason = "Claiming to represent a center pending independent proof";
        using var applied = await applicant.PostAsJsonAsync(
            "/api/v1/me/center-manager-requests",
            new { requestId=id, centerRef, reason }, ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        var path = "/api/v1/operations/center-manager-requests/" + id + "/workspace";
        using var anon = fx.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync(path, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await applicant.GetAsync(path, ct)).StatusCode);
        using var response = await admin.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var root = json!.RootElement;
        Assert.Equal(id, root.GetProperty("requestId").GetGuid());
        var center = root.GetProperty("center");
        Assert.Equal(centerRef, center.GetProperty("centerRef").GetString());
        Assert.Equal("مرکز ثبت‌شده ادمین",center.GetProperty("displayName").GetString());
        Assert.Equal("اراک",center.GetProperty("city").GetString());
        Assert.Equal("ADMIN_DEFINED_UNVERIFIED",
            center.GetProperty("recordState").GetString());
        Assert.False(center.GetProperty("licensed").GetBoolean());
        Assert.False(center.GetProperty("published").GetBoolean());
        var subject = root.GetProperty("applicant");
        Assert.Equal(applicantSubject,subject.GetProperty("subject").GetString());
        Assert.Equal(reason,subject.GetProperty("submittedReason").GetString());
        Assert.Equal("PENDING_REVIEW",root.GetProperty("review").GetProperty("state").GetString());
        Assert.Equal(0L,root.GetProperty("otherApplicantClaims").GetInt64());
        Assert.Equal("NOT_CONNECTED",
            root.GetProperty("representationEvidence").GetProperty("sourceStatus").GetString());
        Assert.False(root.GetProperty("representationEvidence").GetProperty("authorityVerified").GetBoolean());
        Assert.False(root.GetProperty("representationEvidence").GetProperty("criteriaDefined").GetBoolean());
        Assert.True(root.GetProperty("associationGateRequired").GetBoolean());
        Assert.False(root.GetProperty("managerAssignedByThisWorkflow").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync(
                "/api/v1/operations/center-manager-requests/"+Guid.NewGuid()+"/workspace",ct))
            .StatusCode);

        var competitor = "other-claimant-" + Guid.NewGuid().ToString("N");
        using var other = fx.AuthenticatedIdentityClient(competitor);
        Assert.Equal(HttpStatusCode.OK,
            (await other.PostAsJsonAsync("/api/v1/me/center-manager-requests",
                new { requestId=Guid.NewGuid(), centerRef,
                    reason="A competing unverified representation claim" },ct)).StatusCode);
        using var competingResponse = await admin.GetAsync(path,ct);
        using var competing = await competingResponse.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal(1L,competing!.RootElement.GetProperty("otherApplicantClaims").GetInt64());

        var review = new {commandId=Guid.NewGuid(),decision="REVIEWED_PENDING_VERIFICATION",
            reason="No independent source can validate this organizational claim yet"};
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync(
                "/api/v1/operations/center-manager-requests/"+id+"/review",review,ct))
            .StatusCode);
        using var afterReview = await admin.GetAsync(path,ct);
        using var reviewed = await afterReview.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var reviewState = reviewed!.RootElement.GetProperty("review");
        Assert.Equal("REVIEWED_PENDING_VERIFICATION",reviewState.GetProperty("state").GetString());
        Assert.Equal(adminSubject,reviewState.GetProperty("reviewerSubject").GetString());
        Assert.False(reviewed.RootElement.GetProperty("representationEvidence")
            .GetProperty("authorityVerified").GetBoolean());

        await using var db=NpgsqlDataSource.Create(
            Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn=await db.OpenConnectionAsync(ct);
        await using var count=new NpgsqlCommand("""
            SELECT count(*) FROM taranom_panel_access_grant
            WHERE subject=@subject AND scope_ref=@center
              AND capability='CenterAccessGrantsManage' AND grant_state='active'
            """,conn);
        count.Parameters.AddWithValue("subject",applicantSubject);
        count.Parameters.AddWithValue("center",centerRef);
        Assert.Equal(0L,(long)(await count.ExecuteScalarAsync(ct))!);
    }

    [Fact]
    public async Task RevokedAdminCannotReadManagerCaseWorkspace()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var fx=new AppointmentApiFixture(ct);
        var adminSubject="admin-revoke-case-"+Guid.NewGuid().ToString("N");
        await fx.GrantAsync(adminSubject,"AdminCenterRegistryManage",
            "taranom-center-registry",ct);
        using var admin=fx.AuthenticatedIdentityClient(adminSubject);
        await using var db=NpgsqlDataSource.Create(
            Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn=await db.OpenConnectionAsync(ct);
        await using var revoke=new NpgsqlCommand("""
            UPDATE taranom_panel_access_grant SET grant_state='revoked',
                grant_not_revoked=false WHERE subject=@subject
                AND capability='AdminCenterRegistryManage'
            """,conn);
        revoke.Parameters.AddWithValue("subject",adminSubject);
        Assert.Equal(1,await revoke.ExecuteNonQueryAsync(ct));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync(
                "/api/v1/operations/center-manager-requests/"+
                Guid.NewGuid()+"/workspace",ct)).StatusCode);
    }
}
