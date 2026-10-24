using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-149: CI-ephemeral, local child-process transport. This is NOT
// a selected Production IPC architecture or a trusted admin authorization.
internal static class AiWorkerQaHttp
{
    private const string Contract = "TRN-TECH-149";
    private const string Command = "READ_SYNTHETIC_INVENTORY";
    private static readonly SemaphoreSlim Slots = new(4, 4);
    private static readonly AiWorkerQaCounters Counters = new();
    private static AiWorkerQaEvidenceLedger? Evidence;

    internal static void InitializeEvidence()
    {
        if(Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY")
            != "CI_EPHEMERAL_ONLY")
            return;
        if(Evidence != null)
            throw new InvalidOperationException("SYNTHETIC_EVIDENCE_ALREADY_INITIALIZED");
        var path=Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_EVIDENCE_FILE")
            ?? throw new InvalidOperationException("SYNTHETIC_EVIDENCE_FILE_REQUIRED");
        var ledger=new AiWorkerQaEvidenceLedger(path);
        ledger.Append(Counters.Snapshot().InstanceEpoch,AiWorkerQaEvidenceKind.Boot);
        Evidence=ledger;
    }
    private static AiWorkerQaEvidenceKind AsEvidence(AiWorkerQaResult result) =>
        result switch
        {
            AiWorkerQaResult.Success => AiWorkerQaEvidenceKind.Success,
            AiWorkerQaResult.ProcessRejected => AiWorkerQaEvidenceKind.ProcessRejected,
            AiWorkerQaResult.InvalidReply => AiWorkerQaEvidenceKind.InvalidReply,
            AiWorkerQaResult.DeadlineExceeded => AiWorkerQaEvidenceKind.DeadlineExceeded,
            AiWorkerQaResult.RequestCancelled => AiWorkerQaEvidenceKind.RequestCancelled,
            _ => AiWorkerQaEvidenceKind.Unavailable
        };

    private static bool Ref(string? value) =>
        value is {Length: >= 6 and <= 128} &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ':');

    private static string Mac(byte[] key, params string[] parts) =>
        Convert.ToHexString(HMACSHA256.HashData(key,
            Encoding.UTF8.GetBytes(string.Join("\n", parts))));

    private static IResult Reply(int code, string outcome, object? data = null) =>
        Results.Json(new {
            contract = Contract, outcome, syntheticOnly = true,
            adminAuthenticated = false, operatorAuthorized = false,
            trainingAllowed = false, productionAllowed = false,
            committeeApproved = false, data
        }, statusCode: code);

    // Read-only, never durable audit evidence. Host-level loopback and
    // forwarded-authority denial continue to run before this handler.
    internal static IResult Telemetry(HttpContext c)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_QA") !=
            "CI_EPHEMERAL_ONLY" ||
            Environment.GetEnvironmentVariable("TARANOM_AI_ADMIN_QA") !=
            "CI_EPHEMERAL_ONLY")
            return Reply(503, "WORKER_NOT_CONFIGURED");
        if (!Safe(c)) return Reply(403, "UNTRUSTED_REQUEST_HINT");
        try { Evidence?.Verify(); }
        catch (InvalidOperationException)
        { return Reply(503,"INTERNAL_WORKER_UNAVAILABLE"); }
        var s = Counters.Snapshot();
        return Reply(200,"EPHEMERAL_WORKER_COUNTERS_ONLY",new {
            s.Started, s.Success, s.ProcessRejected, s.InvalidReply,
            s.DeadlineExceeded, s.RequestCancelled, s.Unavailable,
            s.CapacityDenied, s.InFlight, s.PeakInFlight,
            s.InstanceEpoch,
            // A new epoch says exactly that volatile counters restarted;
            // it is NOT continuity attestation, uptime or audit evidence.
            counterResetOnRestart=true, continuityAttested=false,
            auditEvidence=false, operationalIdentityVerified=false,
            productionReady=false
        });
    }

    private static bool Safe(HttpContext c) =>
        !c.Request.Headers.ContainsKey("Authorization") &&
        !c.Request.Headers.ContainsKey("Cookie") &&
        !c.Request.Headers.ContainsKey("X-Actor") &&
        !c.Request.Headers.ContainsKey("X-Tenant") &&
        !c.Request.Headers.ContainsKey("X-Organization") &&
        !c.Request.Headers.ContainsKey("X-Role") &&
        !c.Request.Headers.ContainsKey("X-Org-Scope") &&
        !c.Request.Headers.ContainsKey("X-Admin") &&
        !c.Request.Headers.ContainsKey("Origin") &&
        c.Request.Query.Count == 0;

    internal static async Task<IResult> Read(HttpContext c)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_QA") !=
            "CI_EPHEMERAL_ONLY" ||
            Environment.GetEnvironmentVariable("TARANOM_AI_ADMIN_QA") !=
            "CI_EPHEMERAL_ONLY")
            return Reply(503, "WORKER_NOT_CONFIGURED");
        if (!Safe(c)) return Reply(403, "UNTRUSTED_REQUEST_HINT");
        if (c.Request.ContentType != "application/json" ||
            c.Request.ContentLength is null or < 2 or > 256)
            return Reply(400, "INVALID_TEST_FRAME");
        try
        {
            var bytes = new byte[257];
            var count = 0;
            while (count < bytes.Length)
            {
                var n = await c.Request.Body.ReadAsync(bytes.AsMemory(count));
                if (n == 0) break;
                count += n;
            }
            if (count is < 2 or > 256 || c.Request.ContentLength != count)
                return Reply(400, "INVALID_TEST_FRAME");
            using var doc = JsonDocument.Parse(bytes.AsMemory(0, count),
                new JsonDocumentOptions { MaxDepth = 2 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 2 ||
                root.EnumerateObject().Select(p => p.Name)
                    .Distinct(StringComparer.Ordinal).Count() != 2 ||
                !root.TryGetProperty("requestRef", out var request) ||
                !root.TryGetProperty("tenantRef", out var tenant) ||
                request.ValueKind != JsonValueKind.String ||
                tenant.ValueKind != JsonValueKind.String ||
                !Ref(request.GetString()) || !Ref(tenant.GetString()))
                return Reply(400, "INVALID_TEST_FRAME");
            // Immediate capacity admission; never queue unbounded child
            // processes behind unauthenticated CI-only requests.
            if (!Slots.Wait(0))
            {
                Counters.DenyCapacity();
                Evidence?.Append(Counters.Snapshot().InstanceEpoch,
                    AiWorkerQaEvidenceKind.CapacityDenied);
                return Reply(503, "WORKER_CAPACITY_EXCEEDED");
            }
            Counters.Start();
            var outcome = AiWorkerQaResult.Unavailable;
            try
            {
                var counts = await InvokeAsync(request.GetString()!,
                    tenant.GetString()!,c.RequestAborted);
                outcome = AiWorkerQaResult.Success;
                return Reply(200, "SYNTHETIC_INVENTORY_OBSERVED",
                    new {requestRef=request.GetString(), tenantRef=tenant.GetString(),
                        counts, workerStatus="SYNTHETIC_READ_ONLY"});
            }
            catch (AiWorkerQaFailure rejected)
            {
                outcome = rejected.Result;
                throw;
            }
            finally
            {
                // One completion per admitted request even for unexpected
                // IO/process errors. There are no request-specific labels.
                try
                {
                    Counters.Complete(outcome);
                    // A persistence error overrides even a read-only success:
                    // the caller receives generic 503, never a fake positive.
                    Evidence?.Append(Counters.Snapshot().InstanceEpoch,
                        AsEvidence(outcome));
                }
                finally { Slots.Release(); }
            }
        }
        catch (JsonException)
        {
            return Reply(400, "INVALID_TEST_FRAME");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or
                                  Win32Exception or OperationCanceledException or
                                  UnauthorizedAccessException or ArgumentException or
                                  SecurityException)
        {
            return Reply(503, "INTERNAL_WORKER_UNAVAILABLE");
        }
    }

    private static async Task<Dictionary<string,int>> InvokeAsync(
        string request, string tenant,CancellationToken aborted)
    {
        var root = Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_REPO_ROOT");
        var db = Environment.GetEnvironmentVariable("TARANOM_AI_BRIDGE_TEST_DB");
        var rawKey = Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_BRIDGE_TEST_KEY");
        if (root is null || !Path.IsPathFullyQualified(root) ||
            !Directory.Exists(root) ||
            !File.Exists(Path.Combine(root, "ai", "taranom_internal",
                "bridge_worker.py")) ||
            db is null || !Path.IsPathFullyQualified(db) ||
            !db.StartsWith("/tmp/taranom-worker-", StringComparison.Ordinal) ||
            !File.Exists(db) || rawKey is not { Length: 64 } ||
            !rawKey.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            rawKey.All(c => c == '0'))
            throw new InvalidOperationException("TEST_WORKER_UNAVAILABLE");
        var key = Convert.FromHexString(rawKey);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var payload = JsonSerializer.Serialize(new {
            contract = Contract, command = Command, requestRef = request,
            tenantRef = tenant, nonce, mac = Mac(key, Contract, Command,
                request, tenant, nonce)
        });
        if (Encoding.UTF8.GetByteCount(payload) > 1024)
            throw new InvalidOperationException("BRIDGE_FRAME_TOO_LARGE");
        var info = new ProcessStartInfo("/usr/bin/python3")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        info.ArgumentList.Add("-B");
        info.ArgumentList.Add("-m");
        info.ArgumentList.Add("ai.taranom_internal.bridge_worker");
        // Do not forward owner Postgres, OIDC, audit-writer, CI, or host secrets.
        info.Environment.Clear();
        info.Environment["PYTHONPATH"] = root;
        info.Environment["PYTHONNOUSERSITE"] = "1";
        info.Environment["TARANOM_AI_OFFLINE_TEST_ONLY"] = "CI_EPHEMERAL_ONLY";
        info.Environment["TARANOM_AI_WORKER_QA"] = "CI_EPHEMERAL_ONLY";
        info.Environment["TARANOM_AI_BRIDGE_TEST_DB"] = db;
        info.Environment["TARANOM_AI_WORKER_BRIDGE_TEST_KEY"] = rawKey;

        return await RunProcessAsync(info,payload,key,request,tenant,nonce,
            TimeSpan.FromSeconds(4),aborted);
    }

    // One-shot subprocess lifecycle shared by the fixed HTTP caller and
    // the nondeployable fault-injection probe. A caller cannot choose the
    // process path or fault mode through any HTTP request.
    internal static async Task<Dictionary<string,int>> RunProcessAsync(
        ProcessStartInfo info,string payload,byte[] key,string request,
        string tenant,string nonce,TimeSpan deadline,
        CancellationToken abort = default)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
                "CI_EPHEMERAL_ONLY" ||
            info.UseShellExecute || !info.RedirectStandardInput ||
            !info.RedirectStandardOutput || !info.RedirectStandardError ||
            key.Length != 32 || Encoding.UTF8.GetByteCount(payload) is < 2 or > 1024 ||
            deadline <= TimeSpan.Zero || deadline > TimeSpan.FromSeconds(4))
            throw new InvalidOperationException("TEST_WORKER_EXECUTION_DENIED");

        using var process = new Process { StartInfo = info };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(abort);
        stop.CancelAfter(deadline);
        var started = false;
        try
        {
            started = process.Start();
            if (!started)
                throw new AiWorkerQaFailure(AiWorkerQaResult.Unavailable);
            // Both streams are read at the same time, never written to
            // console or logs. Cap stdout and stderr independently.
            var errorTask = ReadCappedAsync(
                process.StandardError.BaseStream,1024,stop.Token);
            await process.StandardInput.WriteAsync(payload.AsMemory(),stop.Token);
            process.StandardInput.Close();
            var (output,count) = await ReadCappedAsync(
                process.StandardOutput.BaseStream,2048,stop.Token);
            if (count is < 2 or > 2048)
                throw new AiWorkerQaFailure(AiWorkerQaResult.InvalidReply);
            await process.WaitForExitAsync(stop.Token);
            var (_,errors) = await errorTask;
            if (errors != 0 || process.ExitCode != 0)
                throw new AiWorkerQaFailure(AiWorkerQaResult.ProcessRejected);
            try
            {
                return AiWorkerReplyVerifier.Verify(
                    output.AsMemory(0,count),key,request,tenant,nonce);
            }
            catch (InvalidOperationException)
            {
                throw new AiWorkerQaFailure(AiWorkerQaResult.InvalidReply);
            }
        }
        catch(OperationCanceledException)
        {
            // Both deny. Counters distinguish caller abort from deadline
            // without preserving request, tenant, credential or model labels.
            throw new AiWorkerQaFailure(abort.IsCancellationRequested
                ? AiWorkerQaResult.RequestCancelled
                : AiWorkerQaResult.DeadlineExceeded);
        }
        finally
        {
            if (started)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree:true);
                }
                catch(InvalidOperationException) { }
                catch(Win32Exception) { }
                // Best effort process reaping after kill. Do not hide the
                // original failure if the OS cannot reap within 2 seconds.
                try { await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2)); }
                catch(TimeoutException) { }
                catch(InvalidOperationException) { }
            }
        }
    }

    private static async Task<(byte[] bytes,int count)> ReadCappedAsync(
        Stream source,int limit,CancellationToken deadline)
    {
        var result = new byte[limit+1];
        var count = 0;
        while(count < result.Length)
        {
            var n = await source.ReadAsync(result.AsMemory(count),deadline);
            if (n == 0) break;
            count += n;
        }
        return (result,count);
    }
}
