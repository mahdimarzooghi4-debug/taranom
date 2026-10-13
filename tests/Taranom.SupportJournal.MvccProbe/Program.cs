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
Console.WriteLine("PASS: genuine support writer lock does not block consistent READ ONLY MVCC; concurrent 11-event audited chain, no real authority");
