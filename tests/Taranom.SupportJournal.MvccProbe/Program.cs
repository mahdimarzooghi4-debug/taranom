using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

// TRN-TECH-109: real ephemeral PostgreSQL row lock and real loopback ASP.NET
// test-only support HTTP. No patient/insurer/credential data or Production.
if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") != "CI_EPHEMERAL_ONLY")
    throw new InvalidOperationException("EPHEMERAL_TEST_ONLY");
var connection = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
    ?? throw new InvalidOperationException("EPHEMERAL_PG_REQUIRED");
var settings = new NpgsqlConnectionStringBuilder(connection);
if (settings.Host is not ("localhost" or "127.0.0.1") ||
    settings.Database != "taranom_sprint64_center" ||
    settings.Username != "postgres")
    throw new InvalidOperationException("UNTRUSTED_DATABASE_TARGET");

using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:4320/"),
    Timeout = TimeSpan.FromSeconds(15) };
var suffix = Guid.NewGuid().ToString("N");
var caseRef = "test:support-mvcc-" + suffix;
var member = "test:member-" + suffix;
var evidence = "test:evidence-" + suffix;
var actor = "test:submitter-" + suffix;
var originalId = Guid.NewGuid();
var root = "internal/demo/v1/support/" + caseRef;

object Command(long version, bool first) => new
{
    requestId = first ? originalId : Guid.NewGuid(),
    expectedRevision = version,
    action = first ? "SUBMIT_UNVERIFIED" : "FOLLOW_UP_UNVERIFIED",
    memberRef = member,
    topic = "INSURANCE_COVERAGE_DISPUTE",
    unverifiedEvidenceRef = evidence,
    claimedSubmitterRef = actor,
    originalSubmissionRequestId = first ? Guid.Empty : originalId,
    claimedFollowUpActorRef = first ? null : "test:follow-up-actor",
    unverifiedFollowUpNoteRef = first ? null : "test:note-" + version
};
async Task<JsonDocument> Accepted(long expected, bool first)
{
    using var response = await http.PostAsJsonAsync(root + "/commands",
        Command(expected, first));
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException("ACCEPTED_SUPPORT_WRITE_MISSING");
    var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    if (doc.RootElement.GetProperty("revision").GetInt64() != expected + 1 ||
        doc.RootElement.GetProperty("outcome").GetString() != "RecordedUnverified")
        throw new InvalidOperationException("ACCEPTED_SUPPORT_REVISION_INVALID");
    return doc;
}
async Task<JsonDocument> VerifiedRead()
{
    using var response = await http.GetAsync(root);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException("VERIFIED_SUPPORT_READ_FAILED");
    var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    var dto = doc.RootElement;
    if (dto.GetProperty("contract").GetString() != "TRN-TECH-075" ||
        !dto.GetProperty("syntheticOnly").GetBoolean() ||
        dto.GetProperty("memberAuthenticated").GetBoolean() ||
        dto.GetProperty("complaintLegallySubmitted").GetBoolean() ||
        dto.GetProperty("insurerContacted").GetBoolean() ||
        dto.GetProperty("clinicalReviewCompleted").GetBoolean() ||
        dto.GetProperty("nationalReleaseAllowed").GetBoolean())
        throw new InvalidOperationException("UNVERIFIED_RIGHTS_ESCALATION");
    var version = dto.GetProperty("revision").GetInt64();
    var count = dto.GetProperty("followUpCount").GetInt64();
    if (count != version - 1 || dto.GetProperty("followUps").GetArrayLength() != count)
        throw new InvalidOperationException("MIXED_OR_INCONSISTENT_JOURNAL_READ");
    return doc;
}
using (var first = await Accepted(0, true))
{
    if (first.RootElement.GetProperty("revision").GetInt64() != 1)
        throw new InvalidOperationException("INITIAL_SUPPORT_WRITE_FAILED");
}

/* TRN-TECH-110: the runtime role used by the actual support HTTP handler
 * must be distinct from the PG owner and lack power to erase history,
 * alter schema, or read a different center bounded context. */
var limited = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
{
    Username = "taranom_sprint101_support_test",
    Password = Environment.GetEnvironmentVariable("TARANOM_SUPPORT_RUNTIME_TEST_PASSWORD")
        ?? throw new InvalidOperationException("SUPPORT_TEST_PASSWORD_UNAVAILABLE"),
    Pooling = false
};
await using var restricted = NpgsqlDataSource.Create(limited.ConnectionString);
await using (var runtime = await restricted.OpenConnectionAsync())
await using (var current = new NpgsqlCommand("""
    SELECT current_user, session_user, rolsuper, rolcreatedb, rolcreaterole
    FROM pg_roles WHERE rolname=current_user
    """, runtime))
await using (var who = await current.ExecuteReaderAsync())
{
    if (!await who.ReadAsync() ||
        who.GetString(0) != "taranom_sprint101_support_test" ||
        who.GetString(1) != "taranom_sprint101_support_test" ||
        who.GetBoolean(2) || who.GetBoolean(3) || who.GetBoolean(4))
        throw new InvalidOperationException("SUPPORT_RUNTIME_NOT_LOW_PRIVILEGE");
}
async Task MustBeDenied(string sql)
{
    await using var cn = await restricted.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(sql, cn);
    cmd.Parameters.AddWithValue("case", caseRef);
    try
    {
        await cmd.ExecuteNonQueryAsync();
    }
    catch (PostgresException e) when (e.SqlState == "42501")
    {
        return; // PostgreSQL permission denial, not an application stub.
    }
    throw new InvalidOperationException("SUPPORT_RUNTIME_UNAUTHORIZED_SQL_SUCCEEDED");
}
await MustBeDenied("""
    UPDATE internal_test_support_event SET command_json='tampered'
    WHERE case_ref=@case
    """);
await MustBeDenied("""
    DELETE FROM internal_test_support_event WHERE case_ref=@case
    """);
await MustBeDenied("""
    UPDATE internal_test_support_audit SET event_digest='tampered'
    WHERE case_ref=@case
    """);
await MustBeDenied("""
    DELETE FROM internal_test_support_audit WHERE case_ref=@case
    """);
await MustBeDenied("""
    DELETE FROM internal_test_support_case WHERE case_ref=@case
    """);
await MustBeDenied("ALTER TABLE internal_test_support_event DISABLE TRIGGER ALL");
await MustBeDenied("TRUNCATE TABLE internal_test_support_event CASCADE");
await MustBeDenied("SELECT count(*) FROM internal_test_center WHERE center_ref=@case");
await MustBeDenied("SELECT count(*) FROM internal_test_center_event WHERE center_ref=@case");
await MustBeDenied("CREATE TABLE public.internal_test_support_intruder (id integer)");
// Reading must still work with the restricted role, as must the existing
// Kestrel writes that use precisely this datasource.
await using (var conn = await restricted.OpenConnectionAsync())
await using (var read = new NpgsqlCommand("""
    SELECT count(*) FROM internal_test_support_event WHERE case_ref=@case
    """, conn))
{
    read.Parameters.AddWithValue("case", caseRef);
    if ((long)(await read.ExecuteScalarAsync() ?? 0L) != 1)
        throw new InvalidOperationException("RUNTIME_CANNOT_READ_OWN_SUPPORT_HISTORY");
}

// TRN-TECH-128: actual database-level separation, not a UI role switch.
// This distinct reader credential is incapable of INSERT, UPDATE, DELETE,
// DDL, SET ROLE writer, or reading another bounded context.
var readerSecret = Environment.GetEnvironmentVariable("TARANOM_SUPPORT_READER_TEST_PASSWORD");
if (readerSecret is not { Length: >= 40 and <= 96 })
    throw new InvalidOperationException("SUPPORT_READER_TEST_PASSWORD_UNAVAILABLE");
var readerConfig = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
{
    Username = "taranom_sprint119_support_reader_test",
    Password = readerSecret,
    Pooling = false
};
await using var readerSource = NpgsqlDataSource.Create(readerConfig.ConnectionString);
await using (var readerConn = await readerSource.OpenConnectionAsync())
await using (var rightsCmd = new NpgsqlCommand("""
    SELECT current_user,session_user,
           has_table_privilege(current_user,'internal_test_support_case','SELECT'),
           has_table_privilege(current_user,'internal_test_support_event','SELECT'),
           has_table_privilege(current_user,'internal_test_support_audit','SELECT'),
           has_table_privilege(current_user,'internal_test_support_case','INSERT'),
           has_table_privilege(current_user,'internal_test_support_case','UPDATE'),
           has_table_privilege(current_user,'internal_test_support_case','DELETE'),
           has_table_privilege(current_user,'internal_test_support_event','INSERT'),
           has_table_privilege(current_user,'internal_test_support_event','UPDATE'),
           has_table_privilege(current_user,'internal_test_support_event','DELETE'),
           has_table_privilege(current_user,'internal_test_support_audit','INSERT'),
           has_table_privilege(current_user,'internal_test_support_audit','UPDATE'),
           has_table_privilege(current_user,'internal_test_support_audit','DELETE')
    """, readerConn))
await using (var rights = await rightsCmd.ExecuteReaderAsync())
{
    if (!await rights.ReadAsync() ||
        rights.GetString(0) != "taranom_sprint119_support_reader_test" ||
        rights.GetString(1) != "taranom_sprint119_support_reader_test" ||
        Enumerable.Range(2,3).Any(i => !rights.GetBoolean(i)) ||
        Enumerable.Range(5,9).Any(i => rights.GetBoolean(i)))
        throw new InvalidOperationException("SUPPORT_READER_INHERITED_WRITE_RIGHTS");
}
async Task ReaderMustBeDenied(string sql)
{
    await using var conn = await readerSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("case", caseRef);
    try { await cmd.ExecuteNonQueryAsync(); }
    catch (PostgresException ex) when (ex.SqlState == "42501") { return; }
    throw new InvalidOperationException("SUPPORT_READER_CAN_WRITE_OR_CROSS_SCOPE");
}
await ReaderMustBeDenied("""
    INSERT INTO internal_test_support_case(case_ref) VALUES (@case)
    """);
await ReaderMustBeDenied("""
    UPDATE internal_test_support_case SET current_revision=55 WHERE case_ref=@case
    """);
await ReaderMustBeDenied("""
    DELETE FROM internal_test_support_case WHERE case_ref=@case
    """);
await ReaderMustBeDenied("""
    DELETE FROM internal_test_support_event WHERE case_ref=@case
    """);
await ReaderMustBeDenied("""
    INSERT INTO internal_test_support_event(case_ref,revision,request_ref,command_json,event_digest)
    VALUES (@case,999,gen_random_uuid(),'{}','fake')
    """);
await ReaderMustBeDenied("""
    UPDATE internal_test_support_audit SET event_digest='bad' WHERE case_ref=@case
    """);
await ReaderMustBeDenied("ALTER TABLE internal_test_support_case ADD COLUMN privileged_field text");
await ReaderMustBeDenied("SELECT count(*) FROM internal_test_center");
await ReaderMustBeDenied("SET ROLE taranom_sprint101_support_test");
await using (var conn = await readerSource.OpenConnectionAsync())
await using (var cmd = new NpgsqlCommand("""
    SELECT count(*) FROM internal_test_support_event WHERE case_ref=@case
    """, conn))
{
    cmd.Parameters.AddWithValue("case", caseRef);
    if ((long)(await cmd.ExecuteScalarAsync() ?? 0L) != 1)
        throw new InvalidOperationException("SUPPORT_READER_CANNOT_READ_EXISTING_HISTORY");
}
Console.WriteLine("PASS: independent PostgreSQL reader SELECT-only, nine real SQL escalation attempts denied");

await using var pg = NpgsqlDataSource.Create(settings.ConnectionString);
await using var cn = await pg.OpenConnectionAsync();
await using var held = await cn.BeginTransactionAsync();
try
{
    // A real PostgreSQL conflicting writer holds the case header row lock.
    // The uncommitted fake revision MUST be invisible to the HTTP reader.
    await using var update = new NpgsqlCommand("""
        UPDATE internal_test_support_case SET current_revision=77
        WHERE case_ref=@case
        """, cn, held);
    update.Parameters.AddWithValue("case", caseRef);
    if (await update.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("TEST_CASE_ROW_NOT_LOCKED");

    var clock = Stopwatch.StartNew();
    var read = VerifiedRead();
    var completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(7)));
    if (completed != read)
        throw new InvalidOperationException("READ_BLOCKED_BEHIND_SUPPORT_WRITER_LOCK");
    using var result = await read;
    if (clock.Elapsed > TimeSpan.FromSeconds(7) ||
        result.RootElement.GetProperty("revision").GetInt64() != 1 ||
        result.RootElement.GetProperty("followUpCount").GetInt32() != 0)
        throw new InvalidOperationException("DIRTY_OR_DELAYED_SUPPORT_READ");
}
finally
{
    await held.RollbackAsync();
}
// After the writer lock is released, accepted new events and all
// independent concurrent readers must see whole committed versions only.
async Task WriteSeries()
{
    for (long revision = 1; revision <= 10; revision++)
    {
        using var _ = await Accepted(revision, false);
        await Task.Yield();
    }
}
async Task ReadSeries()
{
    for (var i = 0; i < 15; i++)
    {
        using var _ = await VerifiedRead();
        await Task.Yield();
    }
}
await Task.WhenAll(WriteSeries(), ReadSeries(), ReadSeries());
using (var final = await VerifiedRead())
{
    if (final.RootElement.GetProperty("revision").GetInt64() != 11 ||
        final.RootElement.GetProperty("followUpCount").GetInt32() != 10)
        throw new InvalidOperationException("FINAL_SUPPORT_CHAIN_NOT_COMPLETE");
}
await using var count = new NpgsqlCommand("""
    SELECT count(*) FROM internal_test_support_audit WHERE case_ref=@case
    """, cn);
count.Parameters.AddWithValue("case", caseRef);
if ((long)(await count.ExecuteScalarAsync() ?? 0L) != 11)
    throw new InvalidOperationException("SUPPORT_AUDIT_EVENT_COUNT_CHANGED");
Console.WriteLine("PASS: support runtime role denied 10 cross-scope/history/DDL mutations; real HTTP MVCC and 11-event audited chain remain intact");


/* TRN-TECH-111 — genuine cost journal PostgreSQL writer lock and complete
 * committed choice history via the ORIGINAL source-backed Kestrel route.
 * No real money, insurer entitlement or authenticated clinical consent. */
if (Environment.GetEnvironmentVariable("TARANOM_COST_JOURNAL_DEMO") != "CI_EPHEMERAL_ONLY")
    throw new InvalidOperationException("SYNTHETIC_COST_PROBE_DISABLED");
http.DefaultRequestHeaders.TryAddWithoutValidation("Origin","http://127.0.0.1:4320");
var costId = Guid.NewGuid();
var costMember = "test:cost-mvcc-" + suffix;
var costRoot = "internal/demo/v1/cost-previews/" + costId.ToString("D");
var components = new[] {
    "ProviderService","PrimaryInsurance","SupplementalInsurance",
    "TreatmentCredit","MemberRemainder"
};
var sample = new {
    memberRef = costMember, previewId = costId, previewRevision = 1L,
    lines = components.Select(component => new {
        component, state = "Missing", amount = (string?)null,
        currency = (string?)null, sourceRef = (string?)null,
        evidenceRef = (Guid?)null, sourceRevision = 0L
    }).ToArray()
};
using var costRegister = await http.PostAsJsonAsync(
    "internal/demo/v1/cost-previews", sample);
if (!costRegister.IsSuccessStatusCode)
    throw new InvalidOperationException("COST_PREVIEW_REGISTRATION_FAILED");
using var costRegistered = await JsonDocument.ParseAsync(
    await costRegister.Content.ReadAsStreamAsync());
var costFingerprint = costRegistered.RootElement.GetProperty(
    "previewFingerprint").GetString()
    ?? throw new InvalidOperationException("MISSING_COST_FINGERPRINT");
if (costRegistered.RootElement.GetProperty("choiceRevision").GetInt64() != 0 ||
    costRegistered.RootElement.GetProperty("historyCount").GetInt32() != 0 ||
    costFingerprint.Length != 64)
    throw new InvalidOperationException("COST_PREVIEW_SOURCE_INVALID");

async Task<JsonDocument> ReadCost()
{
    using var response = await http.GetAsync(costRoot);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException("COST_MVCC_READ_FAILED");
    var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    var body = doc.RootElement;
    foreach (var flag in new[] {
        "memberAuthenticated","officialChoiceRecorded","choiceSignatureValid",
        "financialCommitmentCreated","clinicalConsentGranted","walletMutated",
        "insurerEntitlementVerified","officialEstimateVerified",
        "nationalReleaseAllowed"
    })
        if (body.GetProperty(flag).GetBoolean())
            throw new InvalidOperationException("COST_REAL_RIGHTS_ESCALATION");
    var version = body.GetProperty("choiceRevision").GetInt64();
    if (body.GetProperty("contract").GetString() != "TRN-TECH-095" ||
        !body.GetProperty("syntheticOnly").GetBoolean() ||
        body.GetProperty("previewId").GetGuid() != costId ||
        body.GetProperty("memberRef").GetString() != costMember ||
        body.GetProperty("previewFingerprint").GetString() != costFingerprint ||
        body.GetProperty("historyCount").GetInt64() != version)
        throw new InvalidOperationException("COST_JOURNAL_MIXED_SNAPSHOT");
    return doc;
}

using (var lockedConn = await pg.OpenConnectionAsync())
await using (var lockedTx = await lockedConn.BeginTransactionAsync())
{
    try
    {
        // The preview guard allows precisely OLD+1. Hold the uncommitted
        // first choice revision with a real SQL UPDATE row lock, but do NOT
        // insert any event. The reader must see committed revision zero.
        await using var update = new NpgsqlCommand("""
            UPDATE internal_test_cost_preview SET choice_revision=1
            WHERE preview_ref=@preview AND choice_revision=0
            """, lockedConn, lockedTx);
        update.Parameters.AddWithValue("preview",costId);
        if (await update.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("COST_ROW_NOT_LOCKED");
        var clock = Stopwatch.StartNew();
        var inFlight = ReadCost();
        if (await Task.WhenAny(inFlight,Task.Delay(TimeSpan.FromSeconds(7))) != inFlight)
            throw new InvalidOperationException("COST_READ_BLOCKED_BEHIND_WRITER");
        using var committed = await inFlight;
        if (clock.Elapsed > TimeSpan.FromSeconds(7) ||
            committed.RootElement.GetProperty("choiceRevision").GetInt64() != 0)
            throw new InvalidOperationException("COST_DIRTY_UNCOMMITTED_REVISION");
    }
    finally { await lockedTx.RollbackAsync(); }
}

async Task ChoiceWriteSeries()
{
    for (long i = 0; i < 8; i++)
    {
        using var response = await http.PostAsJsonAsync(costRoot + "/choices", new {
            requestId = Guid.NewGuid(), expectedRevision = i,
            memberRef = costMember, previewId = costId,
            previewRevision = 1L, previewFingerprint = costFingerprint,
            choice = i % 2 == 0 ? "AcknowledgedUncertainty" : "Deferred"
        });
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("COST_CONCURRENT_WRITE_REJECTED");
        using var accepted = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        if (accepted.RootElement.GetProperty("outcome").GetString()
                != "RecordedUnverified" ||
            accepted.RootElement.GetProperty("choiceRevision").GetInt64() != i + 1)
            throw new InvalidOperationException("COST_WRONG_WRITE_REVISION");
        await Task.Yield();
    }
}
async Task CostReadSeries()
{
    for (var i = 0; i < 14; i++)
    {
        using var read = await ReadCost();
        await Task.Yield();
    }
}
await Task.WhenAll(ChoiceWriteSeries(), CostReadSeries(), CostReadSeries());
using (var final = await ReadCost())
{
    if (final.RootElement.GetProperty("choiceRevision").GetInt64() != 8 ||
        final.RootElement.GetProperty("historyCount").GetInt32() != 8 ||
        final.RootElement.GetProperty("lastUnverifiedChoice").GetString() != "Deferred")
        throw new InvalidOperationException("COST_FINAL_VERIFIED_READ_INVALID");
}
await using (var audit = new NpgsqlCommand("""
    SELECT count(*) FROM internal_test_cost_choice_audit WHERE preview_ref=@preview
    """, cn))
{
    audit.Parameters.AddWithValue("preview",costId);
    if ((long)(await audit.ExecuteScalarAsync() ?? 0L) != 8)
        throw new InvalidOperationException("COST_PAIRED_AUDIT_MISSING");
}
Console.WriteLine("PASS: cost journal unlocked read-only MVCC under real writer row lock; 8 concurrent source-backed choices/audits and all financial/clinical grants denied");

