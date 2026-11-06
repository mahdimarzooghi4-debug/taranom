using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class CenterManagerRequestApiHttpTests
{
    private static readonly string Base = "/api/v1/me/center-manager-requests";
    private static readonly string AdminBase = "/api/v1/operations/center-manager-requests";

    private static async Task<string> DefineCenterAsync(
        AppointmentApiFixture fixture, string adminSubject, CancellationToken ct)
    {
        await fixture.GrantAsync(adminSubject, "AdminCenterRegistryManage",
            "taranom-center-registry", ct);
        using var admin = fixture.AuthenticatedIdentityClient(adminSubject);
        using var create = await admin.PostAsJsonAsync("/api/v1/operations/centers",
            new { requestId=Guid.NewGuid(), displayName="مرکز مرجع آزمایشی",
                city="شیراز" }, ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var payload = await create.Content.ReadFromJsonAsync<JsonDocument>(ct);
        return payload!.RootElement.GetProperty("centerRef").GetString()!;
    }

    [Fact]
    public async Task AuthenticatedApplicantMaySubmitOnlyForAdminDefinedCenterAndCannotGainAuthority()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fx = new AppointmentApiFixture(ct);
        var adminSubject = "review-admin-" + Guid.NewGuid().ToString("N");
        var center = await DefineCenterAsync(fx, adminSubject, ct);
        var claimant = "manager-claimant-" + Guid.NewGuid().ToString("N");
        using var anon = fx.Factory.CreateClient();
        using var applicant = fx.AuthenticatedIdentityClient(claimant);

        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(Base,ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync(Base,
                new { requestId=Guid.NewGuid(), centerRef=center,
                    reason="Unverified applicant claim" },ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await applicant.GetAsync(AdminBase,ct)).StatusCode);

        var invalidCenter = "center-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.NotFound,
            (await applicant.PostAsJsonAsync(Base,new {
                requestId=Guid.NewGuid(), centerRef=invalidCenter,
                reason="A genuine submitted claim not proof" },ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await applicant.PostAsJsonAsync(Base,new {
                requestId=Guid.NewGuid(), centerRef=center, reason="short" },ct)).StatusCode);

        var requestId = Guid.NewGuid();
        var command = new { requestId, centerRef=center,
            reason="I request human review for this center" };
        var submitted = await applicant.PostAsJsonAsync(Base,command,ct);
        Assert.Equal(HttpStatusCode.OK,submitted.StatusCode);
        Assert.Equal("no-store",submitted.Headers.CacheControl?.ToString());
        using var body = await submitted.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Applied",body!.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("PENDING_REVIEW",body.RootElement.GetProperty("state").GetString());
        Assert.False(body.RootElement.GetProperty("managerAssigned").GetBoolean());

        var replayed = await applicant.PostAsJsonAsync(Base,command,ct);
        Assert.Equal(HttpStatusCode.OK,replayed.StatusCode);
        using var replay = await replayed.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Replayed",replay!.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await applicant.PostAsJsonAsync(Base,new {
                requestId, centerRef=center, reason="Changed untrusted claim" },ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await applicant.PostAsJsonAsync(Base,new {
                requestId=Guid.NewGuid(),centerRef=center,reason=command.reason },ct)).StatusCode);

        using var list = await applicant.GetAsync(Base,ct);
        Assert.Equal(HttpStatusCode.OK,list.StatusCode);
        using var own = await list.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.True(own!.RootElement.GetProperty("associationGateRequired").GetBoolean());
        var row=Assert.Single(own.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal(requestId.ToString("D"),row.GetProperty("requestId").GetString());
        Assert.Equal("PENDING_REVIEW",row.GetProperty("state").GetString());
        Assert.False(row.GetProperty("managerAssigned").GetBoolean());

        using var unrelated = fx.AuthenticatedIdentityClient(
            "unrelated-" + Guid.NewGuid().ToString("N"));
        using var privateView = await unrelated.GetAsync(Base,ct);
        using var privateJson = await privateView.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Empty(privateJson!.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await applicant.GetAsync($"/api/v1/centers/{center}/access-grants",ct)).StatusCode);
        Assert.Equal(0L,await CountManagerGrantsAsync(center,claimant,ct));
    }

    [Fact]
    public async Task AdminCanReviewOrDeclineWithoutAssigningManagerAndOnlyOnce()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var fx=new AppointmentApiFixture(ct);
        var adminSubject="review-admin-"+Guid.NewGuid().ToString("N");
        var center=await DefineCenterAsync(fx,adminSubject,ct);
        var claimant="claimant-"+Guid.NewGuid().ToString("N");
        using var applicant=fx.AuthenticatedIdentityClient(claimant);
        using var admin=fx.AuthenticatedIdentityClient(adminSubject);
        using var outsider=fx.AuthenticatedIdentityClient(
            "unprivileged-"+Guid.NewGuid().ToString("N"));
        var id=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,
            (await applicant.PostAsJsonAsync(Base,new {
                requestId=id,centerRef=center,
                reason="I request to be considered for center management" },ct)).StatusCode);
        var reviewPath=AdminBase+"/"+id+"/review";
        var commandId=Guid.NewGuid();
        var review=new {commandId,decision="REVIEWED_PENDING_VERIFICATION",
            reason="Reviewed claim; awaiting independent identity and authority evidence"};
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PostAsJsonAsync(reviewPath,review,ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fx.Factory.CreateClient().PostAsJsonAsync(reviewPath,review,ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(reviewPath,new {
                commandId=Guid.NewGuid(),decision="APPROVED",
                reason="Bypass professional evidence" },ct)).StatusCode);
        var response=await admin.PostAsJsonAsync(reviewPath,review,ct);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Applied",json!.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(review.decision,json.RootElement.GetProperty("state").GetString());
        Assert.False(json.RootElement.GetProperty("managerAssigned").GetBoolean());

        using var adminList=await admin.GetAsync(AdminBase,ct);
        Assert.Equal(HttpStatusCode.OK,adminList.StatusCode);
        var listed=await adminList.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Contains(listed!.RootElement.GetProperty("requests").EnumerateArray(),
            x=>x.GetProperty("requestId").GetString()==id.ToString("D") &&
               x.GetProperty("state").GetString()==review.decision);
        using var ownList=await applicant.GetAsync(Base,ct);
        using var own=await ownList.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var item=Assert.Single(own!.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal(review.decision,item.GetProperty("state").GetString());
        Assert.False(item.GetProperty("managerAssigned").GetBoolean());
        Assert.Equal(0L,await CountManagerGrantsAsync(center,claimant,ct));

        var replay=await admin.PostAsJsonAsync(reviewPath,review,ct);
        Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        using var replayBody=await replay.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal("Replayed",replayBody!.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync(reviewPath,new {
            commandId,decision="DECLINED",reason=review.reason },ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync(reviewPath,new {
            commandId=Guid.NewGuid(),decision=review.decision,reason=review.reason },ct)).StatusCode);

        // A second applicant can be declined; neither result provisions access.
        var second="claimant-"+Guid.NewGuid().ToString("N");
        using var other=fx.AuthenticatedIdentityClient(second);
        var otherId=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await other.PostAsJsonAsync(Base,new {
            requestId=otherId,centerRef=center,
            reason="Second independently authenticated requester" },ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(
            AdminBase+"/"+otherId+"/review",new {
            commandId=Guid.NewGuid(),decision="DECLINED",
            reason="Insufficient authority evidence was presented" },ct)).StatusCode);
        Assert.Equal(0L,await CountManagerGrantsAsync(center,second,ct));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await applicant.GetAsync($"/api/v1/centers/{center}/appointments",ct)).StatusCode);
    }

    [Fact]
    public async Task SelfReviewAndReviewWithoutCurrentAdminGrantFailClosed()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var fx=new AppointmentApiFixture(ct);
        var adminSubject="review-self-"+Guid.NewGuid().ToString("N");
        var center=await DefineCenterAsync(fx,adminSubject,ct);
        using var admin=fx.AuthenticatedIdentityClient(adminSubject);
        var requestId=Guid.NewGuid();
        await admin.PostAsJsonAsync(Base,new {
            requestId,centerRef=center,
            reason="An admin cannot review their own manager claim" },ct);
        var path=AdminBase+"/"+requestId+"/review";
        var command=new {commandId=Guid.NewGuid(),
            decision="REVIEWED_PENDING_VERIFICATION",
            reason="This must be reviewed by a different Admin"};
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync(path,command,ct)).StatusCode);

        var applicant="review-after-revoke-"+Guid.NewGuid().ToString("N");
        using var client=fx.AuthenticatedIdentityClient(applicant);
        var independentId=Guid.NewGuid();
        await client.PostAsJsonAsync(Base,new {
            requestId=independentId,centerRef=center,
            reason="Request awaiting current Admin grant" },ct);
        await using var db=NpgsqlDataSource.Create(
            Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn=await db.OpenConnectionAsync(ct);
        await using(var revoke=new NpgsqlCommand("""
            UPDATE taranom_panel_access_grant
            SET grant_state='revoked',grant_not_revoked=false
            WHERE subject=@subject AND capability='AdminCenterRegistryManage'
            """,conn))
        {
            revoke.Parameters.AddWithValue("subject",adminSubject);
            Assert.Equal(1,await revoke.ExecuteNonQueryAsync(ct));
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync(AdminBase+"/"+independentId+"/review",
                command,ct)).StatusCode);
        Assert.Equal(0L,await CountManagerGrantsAsync(center,applicant,ct));
    }

    [Fact]
    public async Task RequestAndReviewHistoryCannotBeModifiedOrTruncated()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var fx=new AppointmentApiFixture(ct);
        var adminSubject="audit-admin-"+Guid.NewGuid().ToString("N");
        var center=await DefineCenterAsync(fx,adminSubject,ct);
        using var admin=fx.AuthenticatedIdentityClient(adminSubject);
        var applicant="audit-subject-"+Guid.NewGuid().ToString("N");
        using var claimant=fx.AuthenticatedIdentityClient(applicant);
        var requestId=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await claimant.PostAsJsonAsync(Base,new {
            requestId,centerRef=center,
            reason="Request history must remain immutable" },ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(
            AdminBase+"/"+requestId+"/review",new {
            commandId=Guid.NewGuid(),decision="DECLINED",
            reason="Identity and delegation not proven" },ct)).StatusCode);

        await using var db=NpgsqlDataSource.Create(
            Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn=await db.OpenConnectionAsync(ct);
        foreach(var table in new[] {
            "taranom_center_manager_request","taranom_center_manager_request_review"
        })
        {
            // Table names come exclusively from this test's fixed literal array.
            foreach(var sql in new[] {
                $"UPDATE {table} SET reason='tampered'",
                $"DELETE FROM {table}",
                $"TRUNCATE {table}"
            })
            {
                await using var cmd=new NpgsqlCommand(sql,conn);
                var ex=await Assert.ThrowsAsync<PostgresException>(
                    async()=>await cmd.ExecuteNonQueryAsync(ct));
                // PostgreSQL blocks TRUNCATE of the request parent before
                // its trigger because review rows reference it by FK.
                // Both database rejections preserve the append-only history.
                Assert.Equal(table=="taranom_center_manager_request" &&
                    sql.StartsWith("TRUNCATE ",StringComparison.Ordinal)
                    ? "0A000" : "55000",ex.SqlState);
            }
        }
        Assert.Equal(0L,await CountManagerGrantsAsync(center,applicant,ct));
    }

    private static async Task<long> CountManagerGrantsAsync(
        string center,string subject,CancellationToken ct)
    {
        await using var db=NpgsqlDataSource.Create(
            Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn=await db.OpenConnectionAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*) FROM taranom_panel_access_grant
            WHERE scope_ref=@center AND subject=@subject
              AND capability='CenterAccessGrantsManage' AND grant_state='active'
            """,conn);
        cmd.Parameters.AddWithValue("center",center);
        cmd.Parameters.AddWithValue("subject",subject);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
