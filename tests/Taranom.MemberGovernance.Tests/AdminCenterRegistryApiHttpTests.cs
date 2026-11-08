using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

[Collection("AppointmentApiHttp")]
public sealed class AdminCenterRegistryApiHttpTests
{
    [Fact]
    public async Task OnlyExplicitlyGrantedAdminCanCreateCenterWithAuditedIdempotentReference()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var fx=new AppointmentApiFixture(ct);
        using var anon=fx.Factory.CreateClient();
        const string path="/api/v1/operations/centers";
        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(path,ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync(path,new {requestId=Guid.NewGuid(),displayName="A",city="B"},ct)).StatusCode);

        var subject="admin-center-"+Guid.NewGuid().ToString("N");
        var unauthorized="center-user-"+Guid.NewGuid().ToString("N");
        await fx.GrantAsync(unauthorized,"CenterAccessGrantsManage",
            "center-other-"+Guid.NewGuid().ToString("N"),ct);
        using var nonAdmin=fx.AuthenticatedIdentityClient(unauthorized);
        Assert.Equal(HttpStatusCode.Forbidden,(await nonAdmin.GetAsync(path,ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await nonAdmin.PostAsJsonAsync(path,
                new {requestId=Guid.NewGuid(),displayName="مرکز غیرمجاز",city="رشت"},ct)).StatusCode);

        await fx.GrantAsync(subject,"AdminCenterRegistryManage","taranom-center-registry",ct);
        using var admin=fx.AuthenticatedIdentityClient(subject);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(path,new {requestId=Guid.Empty,displayName="مرکز",city="رشت"},ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(path,new {requestId=Guid.NewGuid(),displayName="",city="رشت"},ct)).StatusCode);

        var id=Guid.NewGuid();
        var payload=new {requestId=id,displayName="مرکز تأسیس‌شده توسط ادمین",city="رشت"};
        var response=await admin.PostAsJsonAsync(path,payload,ct);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal("no-store",response.Headers.CacheControl?.ToString());
        using var first=await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var body=first!.RootElement;
        var center=body.GetProperty("centerRef").GetString()!;
        Assert.StartsWith("center-",center);
        Assert.Equal("Applied",body.GetProperty("outcome").GetString());
        Assert.Equal("ADMIN_DEFINED_UNVERIFIED",body.GetProperty("recordState").GetString());
        Assert.False(body.GetProperty("licensed").GetBoolean());
        Assert.False(body.GetProperty("published").GetBoolean());
        Assert.False(body.GetProperty("managerAssigned").GetBoolean());

        var replay=await admin.PostAsJsonAsync(path,payload,ct);
        Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        using var same=await replay.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal(center,same!.RootElement.GetProperty("centerRef").GetString());
        Assert.Equal("Replayed",same.RootElement.GetProperty("outcome").GetString());

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync(path,
                new {requestId=id,displayName="مرکز متفاوت",city="رشت"},ct)).StatusCode);
        var observed=await admin.GetAsync(path+"/creation-requests/"+id,ct);
        Assert.Equal(HttpStatusCode.OK,observed.StatusCode);
        using var record=await observed.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Equal(center,record!.RootElement.GetProperty("centerRef").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync(path+"/creation-requests/"+Guid.NewGuid(),ct)).StatusCode);

        var list=await admin.GetAsync(path,ct);
        Assert.Equal(HttpStatusCode.OK,list.StatusCode);
        Assert.Equal("no-store",list.Headers.CacheControl?.ToString());
        using var listed=await list.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.Contains(listed!.RootElement.GetProperty("centers").EnumerateArray(),
            item=>item.GetProperty("centerRef").GetString()==center &&
                item.GetProperty("displayName").GetString()==payload.displayName &&
                !item.GetProperty("published").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await nonAdmin.GetAsync(path+"/creation-requests/"+id,ct)).StatusCode);

        await using var db=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TARANOM_TEST_PG")!);
        await using var conn=await db.OpenConnectionAsync(ct);
        await using(var q=new NpgsqlCommand("""
            SELECT count(*) FROM taranom_canonical_center
            WHERE center_ref=@center AND defined_by_subject=@subject
            """,conn))
        {
            q.Parameters.AddWithValue("center",center);
            q.Parameters.AddWithValue("subject",subject);
            Assert.Equal(1L,(long)(await q.ExecuteScalarAsync(ct))!);
        }
        await using(var q=new NpgsqlCommand("""
            SELECT count(*) FROM taranom_panel_access_grant
            WHERE scope_ref=@center
            """,conn))
        {
            q.Parameters.AddWithValue("center",center);
            Assert.Equal(0L,(long)(await q.ExecuteScalarAsync(ct))!);
        }
        await using(var canonicalUpdate=new NpgsqlCommand("""
            UPDATE taranom_canonical_center SET city='unapproved-change'
            WHERE center_ref=@center
            """,conn))
        {
            canonicalUpdate.Parameters.AddWithValue("center",center);
            var ex=await Assert.ThrowsAsync<PostgresException>(
                async()=>await canonicalUpdate.ExecuteNonQueryAsync(ct));
            Assert.Equal("55000",ex.SqlState);
        }
        await using(var canonicalDelete=new NpgsqlCommand("""
            DELETE FROM taranom_canonical_center WHERE center_ref=@center
            """,conn))
        {
            canonicalDelete.Parameters.AddWithValue("center",center);
            var ex=await Assert.ThrowsAsync<PostgresException>(
                async()=>await canonicalDelete.ExecuteNonQueryAsync(ct));
            Assert.Equal("55000",ex.SqlState);
        }
        await using(var q=new NpgsqlCommand("""
            UPDATE taranom_canonical_center_creation_receipt
            SET fingerprint=repeat('0',64) WHERE center_ref=@center
            """,conn))
        {
            q.Parameters.AddWithValue("center",center);
            var ex=await Assert.ThrowsAsync<PostgresException>(
                async()=>await q.ExecuteNonQueryAsync(ct));
            Assert.Equal("55000",ex.SqlState);
        }

        await using(var revoke=new NpgsqlCommand("""
            UPDATE taranom_panel_access_grant
            SET grant_state='revoked',grant_not_revoked=false
            WHERE subject=@subject AND capability='AdminCenterRegistryManage'
            """,conn))
        {
            revoke.Parameters.AddWithValue("subject",subject);
            Assert.Equal(1,await revoke.ExecuteNonQueryAsync(ct));
        }
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync(path,ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync(path,
                new {requestId=Guid.NewGuid(),displayName="مرکز دیگر",city="رشت"},ct)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentIdenticalAdminCreateRequestsMustReturnOneCenter()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var fx=new AppointmentApiFixture(ct);
        var subject="admin-concurrent-"+Guid.NewGuid().ToString("N");
        await fx.GrantAsync(subject,"AdminCenterRegistryManage","taranom-center-registry",ct);
        using var admin=fx.AuthenticatedIdentityClient(subject);
        var command=new {requestId=Guid.NewGuid(),displayName="مرکز همزمان",city="تبریز"};
        var tasks=Enumerable.Range(0,2).Select(_=>admin.PostAsJsonAsync(
            "/api/v1/operations/centers",command,ct)).ToArray();
        var responses=await Task.WhenAll(tasks);
        Assert.All(responses,x=>Assert.Equal(HttpStatusCode.OK,x.StatusCode));
        var refs=new List<string>();
        var outcomes=new List<string>();
        foreach(var response in responses)
        {
            using var json=await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
            refs.Add(json!.RootElement.GetProperty("centerRef").GetString()!);
            outcomes.Add(json.RootElement.GetProperty("outcome").GetString()!);
        }
        Assert.Single(refs.Distinct());
        Assert.Contains("Applied",outcomes);
        Assert.Contains("Replayed",outcomes);
    }
}
