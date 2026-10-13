using System.Globalization;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-098 — strictly read-only historical projections of the EXISTING
// reattested synthetic PostgreSQL cost/choice append-only journal.
// Not legal acknowledgement, actual insurer coverage, payment or patient data.
internal static class CostHistoryHttp
{
    internal static async Task<IResult> PageAsync(HttpContext context)
    {
        if (!Enabled()) return Results.NotFound();
        if (!RouteId(context, out var id)) return Denied(400,"INVALID_TEST_PREVIEW_REF");
        var q = context.Request.Query;
        if (q.Count != 3 ||
            !q.ContainsKey("afterRevision") || !q.ContainsKey("limit") ||
            !q.ContainsKey("expectedCurrentRevision") ||
            q["afterRevision"].Count != 1 || q["limit"].Count != 1 ||
            q["expectedCurrentRevision"].Count != 1 ||
            !Unsigned(q["afterRevision"].ToString(),out var cursor) ||
            !Unsigned(q["limit"].ToString(),out var limit) ||
            !Unsigned(q["expectedCurrentRevision"].ToString(),out var expected) ||
            limit is < 1 or > 20)
            return Denied(400,"INVALID_TEST_COST_HISTORY_CURSOR");
        try
        {
            // ReadAsync locks one existing preview row, verifies all events,
            // SHA256 digests, audits, and replays canonical CostAwarenessRecorder.
            var snapshot = await CostJournalHttp.Store.ReadAsync(id);
            if (snapshot is null) return Denied(404,"TEST_COST_PREVIEW_NOT_FOUND");
            if (snapshot.Revision != expected)
                return Denied(409,"TEST_COST_HISTORY_REVISION_CHANGED");
            if (cursor > snapshot.Revision)
                return Denied(400,"INVALID_TEST_COST_HISTORY_CURSOR");
            var page = snapshot.History.Where(e=>e.Revision>cursor)
                .Take((int)limit+1).ToArray();
            var more = page.Length>limit;
            var events=page.Take((int)limit).Select(e=>new
            {
                revision=e.Revision, choice=e.Command.Choice.ToString(),
                hypotheticalOnly=true, legallyBinding=false
            }).ToArray();
            return Results.Json(new
            {
                contract="TRN-TECH-098", syntheticOnly=true,
                outcome="VERIFIED_SYNTHETIC_HISTORY_PAGE",
                snapshotIdentity=id, previewRevision=snapshot.PreviewRevision,
                previewFingerprint=snapshot.PreviewFingerprint,
                currentRevision=snapshot.Revision,
                afterRevision=cursor, expectedCurrentRevision=expected,
                hasMore=more,
                nextAfterRevision=more?events[^1].revision:(long?)null,
                events,
                memberAuthenticated=false, officialChoiceRecorded=false,
                choiceSignatureValid=false, insurerEntitlementVerified=false,
                walletMutated=false, financialCommitmentCreated=false,
                clinicalConsentGranted=false, nationalReleaseAllowed=false
            });
        }
        catch(Exception ex) when (ex is NpgsqlException or InvalidOperationException
                                   or IOException or OverflowException)
        {
            return Denied(503,"TEST_COST_HISTORY_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> AtRevisionAsync(HttpContext context)
    {
        if (!Enabled()) return Results.NotFound();
        if (!RouteId(context,out var id) ||
            !Unsigned(context.Request.RouteValues["revision"]?.ToString(),out var at))
            return Denied(400,"INVALID_TEST_COST_HISTORY_REVISION");
        var q=context.Request.Query;
        if (q.Count!=1 || !q.ContainsKey("expectedCurrentRevision") ||
            q["expectedCurrentRevision"].Count!=1 ||
            !Unsigned(q["expectedCurrentRevision"].ToString(),out var expected))
            return Denied(400,"INVALID_TEST_COST_HISTORY_REVISION");
        try
        {
            var current=await CostJournalHttp.Store.ReadAsync(id);
            if(current is null) return Denied(404,"TEST_COST_PREVIEW_NOT_FOUND");
            if(current.Revision!=expected)
                return Denied(409,"TEST_COST_HISTORY_REVISION_CHANGED");
            if(at>current.Revision)
                return Denied(400,"INVALID_TEST_COST_HISTORY_REVISION");
            // The underlying journal has already reattested every historical
            // event by full canonical replay before any prefix is exposed.
            var last=at==0?(CostAwarenessEntry?)null:current.History[(int)(at-1)];
            if(last is not null &&
               (last.Value.Revision!=at ||
                last.Value.Command.ExpectedRevision!=at-1 ||
                last.Value.Command.PreviewFingerprint!=current.PreviewFingerprint))
                return Denied(503,"TEST_COST_HISTORY_UNVERIFIABLE");
            return Results.Json(new
            {
                contract="TRN-TECH-098",syntheticOnly=true,
                outcome="HISTORICAL_UNVERIFIED_CHOICE_READ",
                snapshotIdentity=id,previewRevision=current.PreviewRevision,
                previewFingerprint=current.PreviewFingerprint,
                currentRevision=current.Revision,
                expectedCurrentRevision=expected,
                asOfRevision=at, choiceCount=at,
                lastUnverifiedChoice=last?.Command.Choice.ToString(),
                historicalOnly=true,autoCarriedForward=false,
                memberAuthenticated=false,officialChoiceRecorded=false,
                choiceSignatureValid=false,insurerEntitlementVerified=false,
                walletMutated=false,financialCommitmentCreated=false,
                clinicalConsentGranted=false,nationalReleaseAllowed=false
            });
        }
        catch(Exception ex) when (ex is NpgsqlException or InvalidOperationException
                                   or IOException or OverflowException)
        {
            return Denied(503,"TEST_COST_HISTORY_UNVERIFIABLE");
        }
    }

    private static bool RouteId(HttpContext c,out Guid id)
    {
        id=Guid.Empty;
        var v=c.Request.RouteValues["previewId"]?.ToString();
        return Guid.TryParseExact(v,"D",out id) && id!=Guid.Empty &&
            v==id.ToString("D");
    }

    private static bool Unsigned(string? input,out long value)
    {
        value=0;
        return input is {Length:>0 and <=19} &&
            (input=="0" || input[0]!='0') &&
            input.All(char.IsAsciiDigit) &&
            long.TryParse(input,NumberStyles.None,CultureInfo.InvariantCulture,
                out value) && value>=0;
    }

    private static bool Enabled()=>Environment.GetEnvironmentVariable(
        "TARANOM_COST_JOURNAL_DEMO")=="CI_EPHEMERAL_ONLY";

    private static IResult Denied(int code,string outcome)=>Results.Json(new
    {
        contract="TRN-TECH-098",syntheticOnly=true,outcome,
        memberAuthenticated=false,officialChoiceRecorded=false,
        choiceSignatureValid=false,insurerEntitlementVerified=false,
        walletMutated=false,financialCommitmentCreated=false,
        clinicalConsentGranted=false,nationalReleaseAllowed=false
    },statusCode:code);
}
