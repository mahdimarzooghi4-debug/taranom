using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-147: genuine Kestrel -> .NET identity boundary -> PostgreSQL.
// This is an EPHEMERAL DENY-only QA endpoint, NOT actual AI administration.
// No model management callback, training worker or promotion route exists.
internal static class AiAdminQaHttp
{
    internal static PgSyntheticAiAdminDenial Journal { get; set; }=null!;

    private static bool Ref(string? v) =>
        v is {Length: >= 6 and <= 128} &&
        v.StartsWith("test:",StringComparison.Ordinal) &&
        v.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '_' or ':' or '-');

    private static bool Safe(HttpContext c)
    {
        if (c.Request.Headers.ContainsKey("Cookie") ||
            c.Request.Headers.ContainsKey("X-Actor") ||
            c.Request.Headers.ContainsKey("X-Tenant") ||
            c.Request.Headers.ContainsKey("X-Organization") ||
            c.Request.Headers.ContainsKey("X-Org-Scope") ||
            c.Request.Headers.ContainsKey("X-Role") ||
            c.Request.Headers.ContainsKey("X-Admin"))
            return false;
        var origin=c.Request.Headers.Origin.ToString();
        return origin.Length==0 ||
            origin is "http://localhost:4320" or "http://127.0.0.1:4320";
    }

    private static IResult Reply(int code,string outcome,object? data=null) =>
        Results.Json(new {
            contract="TRN-TECH-147",
            outcome,
            syntheticOnly=true,
            adminAuthenticated=false,
            operatorAuthorized=false,
            committeeApproved=false,
            productionAllowed=false,
            trainingAllowed=false,
            realIdentityEstablished=false,
            data
        },statusCode:code);

    internal static async Task<IResult> Record(HttpContext context)
    {
        if (!Safe(context)) return Reply(403,"UNTRUSTED_IDENTITY_HINT");
        if (context.Request.Query.Count!=0) return Reply(400,"UNEXPECTED_QUERY");
        if (context.Request.ContentType != "application/json" ||
            context.Request.ContentLength is null or < 2 or > 512)
            return Reply(400,"INVALID_SYNTHETIC_COMMAND");
        try
        {
            var bytes=new byte[513];
            int count=0;
            while(count<bytes.Length)
            {
                var read=await context.Request.Body.ReadAsync(bytes.AsMemory(count));
                if(read==0) break;
                count+=read;
            }
            if(count<2||count>512||
                context.Request.ContentLength!=count)
                return Reply(400,"INVALID_SYNTHETIC_COMMAND");
            using var doc=JsonDocument.Parse(bytes.AsMemory(0,count),
                new JsonDocumentOptions{MaxDepth=3});
            var obj=doc.RootElement;
            if(obj.ValueKind!=JsonValueKind.Object||
                obj.EnumerateObject().Count()!=3||
                obj.EnumerateObject().Select(v=>v.Name).Distinct(StringComparer.Ordinal).Count()!=3||
                !obj.TryGetProperty("requestRef",out var req)||
                !obj.TryGetProperty("operation",out var op)||
                !obj.TryGetProperty("targetRef",out var target)||
                req.ValueKind!=JsonValueKind.String||
                op.ValueKind!=JsonValueKind.String||
                target.ValueKind!=JsonValueKind.String||
                !Ref(req.GetString())||!Ref(target.GetString())||
                !Enum.TryParse<AiControlPlaneOperation>(op.GetString(),
                    ignoreCase:false,out var operation)||
                !Enum.IsDefined(operation) ||
                operation.ToString()!=op.GetString())
                return Reply(400,"INVALID_SYNTHETIC_COMMAND");

            // No real trust anchor/role binding is configured. An unsolicited
            // Bearer header (even signed) CANNOT turn into user authority.
            var rawAuth=context.Request.Headers.Authorization.ToString();
            if(rawAuth.Length>8199 ||
                (rawAuth.Length>0&&!rawAuth.StartsWith("Bearer ",StringComparison.Ordinal)))
                return Reply(403,"UNTRUSTED_AUTHORIZATION_FORMAT");
            var bearer=rawAuth.Length>7?rawAuth[7..]:null;
            var decision=AiAdminIdentityBoundary.Decide(
                bearer,null,DateTimeOffset.UtcNow,operation);
            if(decision.Allowed||decision.ProductionAllowed||
                decision.AdminAuthenticated)
                return Reply(503,"FORBIDDEN_AUTHORITY_STATE");
            var result=await Journal.RecordAsync(
                req.GetString()!,operation.ToString(),
                target.GetString()!,decision.Status);
            return Reply(result.Outcome=="CONFLICTING_DENIAL_REPLAY"?409:403,
                result.Outcome,Format(result.Record));
        }
        catch(JsonException){return Reply(400,"INVALID_SYNTHETIC_COMMAND");}
        catch(Exception e) when(e is NpgsqlException or InvalidOperationException
            or IOException or OverflowException)
        {
            return Reply(503,"AI_DENIAL_AUDIT_UNAVAILABLE");
        }
    }

    internal static async Task<IResult> Read(HttpContext context)
    {
        if(!Safe(context))return Reply(403,"UNTRUSTED_IDENTITY_HINT");
        if(context.Request.Query.Count!=0)return Reply(400,"UNEXPECTED_QUERY");
        var refValue=context.Request.RouteValues["requestRef"]?.ToString();
        if(!Ref(refValue))return Reply(400,"INVALID_TEST_REFERENCE");
        try
        {
            var result=await Journal.ReadAsync(refValue!);
            return result is null?Reply(404,"DENIAL_NOT_FOUND"):
                Reply(200,"AUDIT_READ_ONLY",Format(result));
        }
        catch(Exception e) when(e is NpgsqlException or InvalidOperationException
            or IOException or OverflowException)
        {
            return Reply(503,"AI_DENIAL_AUDIT_UNAVAILABLE");
        }
    }

    private static object Format(AiAdminDenialRecord value) => new
    {
        seq=value.Seq,
        requestRef=value.RequestRef,
        operation=value.Operation,
        targetRef=value.TargetRef,
        identityEvidenceStatus=value.EvidenceStatus,
        previousSha256=value.PreviousSha256,
        eventSha256=value.EventSha256,
        disposition="DENIED",
        immutable=true
    };
}
