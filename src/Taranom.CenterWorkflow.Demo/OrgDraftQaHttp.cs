using System.Globalization;
using System.Text.Json;
using Npgsql;
using Taranom.OrgScope.Foundation;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-135: read/write on a single FIXED, ephemeral SUPPORT PostgreSQL
// credential. Never install in a real web API: localhost is NOT identity.
internal static class OrgDraftQaHttp
{
    internal static PgScopedDraftWorkspace Drafts { get; set; } = null!;
    internal static PgScopedDraftActivity Activity { get; set; } = null!;
    internal static PgScopedDraftWorkQueue QueueStore { get; set; } = null!;

    private static IResult Respond(int code, string outcome, object? content = null) =>
        Results.Json(new
        {
            contract = "TRN-TECH-135",
            source = "SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY",
            syntheticOnly = true,
            organizationAuthenticated = false,
            operatorAuthorized = false,
            businessStatusChanged = false,
            nationalReleaseAllowed = false,
            outcome,
            data = content
        }, statusCode: code);

    private static bool Safe(HttpContext c, bool write = false)
    {
        // A test-only fixed service database principal must never silently
        // treat a browser Authorization/cookie/tenant selector as credentials.
        if (c.Request.Headers.ContainsKey("Authorization") ||
            c.Request.Headers.ContainsKey("Cookie") ||
            c.Request.Headers.ContainsKey("X-Organization") ||
            c.Request.Headers.ContainsKey("X-Org-Scope"))
            return false;
        var origin = c.Request.Headers.Origin.ToString();
        return !write || origin.Length == 0 ||
            origin is "http://localhost:4320" or "http://127.0.0.1:4320";
    }

    private static bool Ref(string? v) =>
        v is { Length: > 5 and <= 128 } &&
        v.StartsWith("test:",StringComparison.Ordinal) &&
        v.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '_' or ':' or '-');

    private static bool NoQuery(HttpContext c) => c.Request.Query.Count == 0;

    private static async Task<JsonElement?> Body(HttpContext c, string[] names)
    {
        if (c.Request.ContentType is null ||
            !c.Request.ContentType.StartsWith(
                "application/json",StringComparison.OrdinalIgnoreCase) ||
            c.Request.ContentLength is > 640)
            return null;
        var bytes = new byte[641];
        int n = 0;
        while (n < bytes.Length)
        {
            int got = await c.Request.Body.ReadAsync(bytes.AsMemory(n));
            if (got == 0) break;
            n += got;
        }
        if (n < 2 || n > 640) return null;
        try
        {
            using var doc = JsonDocument.Parse(bytes.AsMemory(0,n),
                new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != names.Length ||
                root.EnumerateObject().Select(x=>x.Name)
                    .Distinct(StringComparer.Ordinal).Count() != names.Length ||
                names.Any(name=>!root.TryGetProperty(name,out _)))
                return null;
            return root.Clone();
        }
        catch (JsonException) { return null; }
    }


    // Queue is one coherent, verified MVCC read, not an independently merged
    // front-end list+history. Organization is always fixed by DB session_user.
    internal static async Task<IResult> Queue(HttpContext c)
    {
        if (!Safe(c)) return Respond(403,"UNTRUSTED_IDENTITY_HINT");
        var q=c.Request.Query;
        if (q.Count!=2||!q.ContainsKey("afterItemRef")||
            !q.ContainsKey("limit")||q["afterItemRef"].Count!=1||
            q["limit"].Count!=1)
            return Respond(400,"INVALID_TEST_CURSOR");
        string after=q["afterItemRef"].ToString();
        string raw=q["limit"].ToString();
        if ((after.Length>0&&!Ref(after))||
            !int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,
                out int limit)||limit is < 1 or > 5||
            raw!=limit.ToString(CultureInfo.InvariantCulture))
            return Respond(400,"INVALID_TEST_CURSOR");
        try
        {
            var result=await QueueStore.ReadAsync(
                after.Length==0?null:after,limit);
            return Respond(200,"Read",new{
                afterItemRef=after,limit,
                nextAfterItemRef=result.NextAfterItemRef,
                items=result.Items.Select(x=>new {
                    x.ItemRef,x.SourceRef,x.ActivityCount,
                    x.LastRequestRef,x.LastNoteRef
                }).ToArray()
            });
        }
        catch(Exception e) when(e is NpgsqlException or InvalidOperationException)
        { return Respond(503,"TEST_SOURCE_UNVERIFIABLE"); }
    }

    internal static async Task<IResult> List(HttpContext c)
    {
        if (!Safe(c)) return Respond(403,"UNTRUSTED_IDENTITY_HINT");
        var q = c.Request.Query;
        if (q.Count != 2 || !q.ContainsKey("afterItemRef") ||
            !q.ContainsKey("limit") || q["afterItemRef"].Count != 1 ||
            q["limit"].Count != 1)
            return Respond(400,"INVALID_TEST_CURSOR");
        var after = q["afterItemRef"].ToString();
        var rawLimit = q["limit"].ToString();
        if (after.Length > 0 && !Ref(after) ||
            !int.TryParse(rawLimit,NumberStyles.None,CultureInfo.InvariantCulture,out var limit) ||
            limit is < 1 or > 10 ||
            rawLimit != limit.ToString(CultureInfo.InvariantCulture))
            return Respond(400,"INVALID_TEST_CURSOR");
        try
        {
            var page = await Drafts.ListAsync(after.Length==0?null:after,limit);
            return Respond(200,"Read",new {
                afterItemRef = after, limit,
                nextAfterItemRef = page.NextAfterItemRef,
                items = page.Items.Select(x=>new {x.ItemRef,x.SourceRef}).ToArray()
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        { return Respond(503,"TEST_SOURCE_UNVERIFIABLE"); }
    }

    internal static async Task<IResult> Create(HttpContext c)
    {
        if (!Safe(c,true)) return Respond(403,"UNTRUSTED_IDENTITY_HINT");
        if (!NoQuery(c)) return Respond(400,"UNEXPECTED_QUERY");
        var json=await Body(c,["itemRef","sourceRef"]);
        if (json is null) return Respond(400,"INVALID_TEST_BODY");
        var body=json.Value;
        if (body.GetProperty("itemRef").ValueKind != JsonValueKind.String ||
            body.GetProperty("sourceRef").ValueKind != JsonValueKind.String)
            return Respond(400,"INVALID_TEST_BODY");
        var item=body.GetProperty("itemRef").GetString();
        var source=body.GetProperty("sourceRef").GetString();
        if (!Ref(item)||!Ref(source)) return Respond(400,"INVALID_TEST_REF");
        try
        {
            var result=await Drafts.RecordAsync(item,source);
            return result.Status switch
            {
                DraftWriteStatus.Recorded or DraftWriteStatus.Replayed =>
                    Respond(200,result.Status.ToString(),
                        new {itemRef=result.Draft!.ItemRef,sourceRef=result.Draft.SourceRef}),
                DraftWriteStatus.ConflictingReplay =>
                    Respond(409,"ConflictingReplay"),
                _ => Respond(400,"INVALID_TEST_REF")
            };
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        { return Respond(503,"TEST_SOURCE_UNVERIFIABLE"); }
    }

    internal static async Task<IResult> Read(HttpContext c)
    {
        if (!Safe(c)) return Respond(403,"UNTRUSTED_IDENTITY_HINT");
        if (!NoQuery(c)) return Respond(400,"UNEXPECTED_QUERY");
        var item=c.Request.RouteValues["itemRef"]?.ToString();
        if (!Ref(item)) return Respond(400,"INVALID_TEST_REF");
        try
        {
            var found=await Drafts.ReadAsync(item);
            return found is null ? Respond(404,"NotFound") :
                Respond(200,"Read",new {itemRef=found.ItemRef,sourceRef=found.SourceRef});
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        { return Respond(503,"TEST_SOURCE_UNVERIFIABLE"); }
    }

    internal static async Task<IResult> History(HttpContext c)
    {
        if (!Safe(c)) return Respond(403,"UNTRUSTED_IDENTITY_HINT");
        if (!NoQuery(c)) return Respond(400,"UNEXPECTED_QUERY");
        var item=c.Request.RouteValues["itemRef"]?.ToString();
        if (!Ref(item)) return Respond(400,"INVALID_TEST_REF");
        try
        {
            var found=await Drafts.ReadAsync(item);
            if (found is null) return Respond(404,"NotFound");
            var events=await Activity.ReadHistoryAsync(item);
            return Respond(200,"Read",new {
                itemRef=item, currentNo=events.Count,
                events=events.Select(x=>new {
                    x.ActivityNo,x.RequestRef,x.NoteRef,
                    x.ExpectedPreviousNo,x.PreviousSha256,x.ActivitySha256
                }).ToArray()
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        { return Respond(503,"TEST_SOURCE_UNVERIFIABLE"); }
    }

    internal static async Task<IResult> Append(HttpContext c)
    {
        if (!Safe(c,true)) return Respond(403,"UNTRUSTED_IDENTITY_HINT");
        if (!NoQuery(c)) return Respond(400,"UNEXPECTED_QUERY");
        var item=c.Request.RouteValues["itemRef"]?.ToString();
        if (!Ref(item)) return Respond(400,"INVALID_TEST_REF");
        var json=await Body(c,["requestRef","noteRef","expectedPreviousNo"]);
        if (json is null) return Respond(400,"INVALID_TEST_BODY");
        var body=json.Value;
        if (body.GetProperty("requestRef").ValueKind != JsonValueKind.String ||
            body.GetProperty("noteRef").ValueKind != JsonValueKind.String ||
            body.GetProperty("expectedPreviousNo").ValueKind != JsonValueKind.Number ||
            !body.GetProperty("expectedPreviousNo").TryGetInt32(out int expected))
            return Respond(400,"INVALID_TEST_BODY");
        var req=body.GetProperty("requestRef").GetString();
        var note=body.GetProperty("noteRef").GetString();
        if (!Ref(req)||!Ref(note)||expected < 0||expected==int.MaxValue)
            return Respond(400,"INVALID_TEST_REF");
        try
        {
            var result=await Activity.AppendAsync(item,req,note,expected);
            var code=result.Status switch
            {
                DraftActivityStatus.Recorded or DraftActivityStatus.Replayed => 200,
                DraftActivityStatus.StaleRevision or
                    DraftActivityStatus.ConflictingReplay => 409,
                DraftActivityStatus.NotFound => 404,
                _ => 400
            };
            return Respond(code,result.Status.ToString(),new {
                itemRef=item,currentNo=result.CurrentNo
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        { return Respond(503,"TEST_SOURCE_UNVERIFIABLE"); }
    }
}
