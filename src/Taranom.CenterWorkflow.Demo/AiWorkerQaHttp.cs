using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-149: CI-ephemeral, local child-process transport. This is NOT
// a selected Production IPC architecture or a trusted admin authorization.
internal static class AiWorkerQaHttp
{
    private const string Contract = "TRN-TECH-149";
    private const string Command = "READ_SYNTHETIC_INVENTORY";
    private static readonly string[] Fields =
        ["datasets", "modelVersions", "evaluations", "trainingRuns", "benchmarks"];

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

    internal static async Task<IResult> Read(HttpContext c)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_QA") !=
            "CI_EPHEMERAL_ONLY" ||
            Environment.GetEnvironmentVariable("TARANOM_AI_ADMIN_QA") !=
            "CI_EPHEMERAL_ONLY")
            return Reply(503, "WORKER_NOT_CONFIGURED");
        if (c.Request.Headers.ContainsKey("Authorization") ||
            c.Request.Headers.ContainsKey("Cookie") ||
            c.Request.Headers.ContainsKey("X-Actor") ||
            c.Request.Headers.ContainsKey("X-Tenant") ||
            c.Request.Headers.ContainsKey("X-Organization") ||
            c.Request.Headers.ContainsKey("X-Role") ||
            c.Request.Headers.ContainsKey("X-Admin") ||
            c.Request.Headers.ContainsKey("Origin") ||
            c.Request.Query.Count != 0)
            return Reply(403, "UNTRUSTED_REQUEST_HINT");
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
            var counts = await InvokeAsync(request.GetString()!,
                tenant.GetString()!);
            return Reply(200, "SYNTHETIC_INVENTORY_OBSERVED",
                new {requestRef=request.GetString(), tenantRef=tenant.GetString(),
                    counts, workerStatus="SYNTHETIC_READ_ONLY"});
        }
        catch (JsonException)
        {
            return Reply(400, "INVALID_TEST_FRAME");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or
                                  Win32Exception or OperationCanceledException or
                                  UnauthorizedAccessException)
        {
            return Reply(503, "INTERNAL_WORKER_UNAVAILABLE");
        }
    }

    private static async Task<Dictionary<string,int>> InvokeAsync(
        string request, string tenant)
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
            RedirectStandardOutput = true, CreateNoWindow = true,
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

        using var process = new Process { StartInfo = info };
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("WORKER_START_FAILED");
            await process.StandardInput.WriteAsync(payload.AsMemory(), limit.Token);
            process.StandardInput.Close();
            var buffer = new char[2049];
            var length = 0;
            while (length < buffer.Length)
            {
                var n = await process.StandardOutput.ReadAsync(
                    buffer.AsMemory(length), limit.Token);
                if (n == 0) break;
                length += n;
            }
            if (length is < 2 or > 2048)
                throw new InvalidOperationException("INVALID_WORKER_LENGTH");
            await process.WaitForExitAsync(limit.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("WORKER_REJECTED");
            using var response = JsonDocument.Parse(
                new string(buffer, 0, length), new JsonDocumentOptions { MaxDepth = 3 });
            var body = response.RootElement;
            if (body.ValueKind != JsonValueKind.Object ||
                body.EnumerateObject().Count() != 9 ||
                body.GetProperty("requestRef").GetString() != request ||
                body.GetProperty("nonce").GetString() != nonce ||
                body.GetProperty("status").GetString() != "SYNTHETIC_READ_ONLY" ||
                !body.GetProperty("syntheticOnly").GetBoolean() ||
                body.GetProperty("adminAuthenticated").GetBoolean() ||
                body.GetProperty("trainingAllowed").GetBoolean() ||
                body.GetProperty("productionAllowed").GetBoolean())
                throw new InvalidOperationException("WORKER_AUTHORITY_MISMATCH");
            var rawCounts = body.GetProperty("counts");
            if (rawCounts.ValueKind != JsonValueKind.Object ||
                rawCounts.EnumerateObject().Count() != Fields.Length ||
                rawCounts.EnumerateObject().Select(p => p.Name)
                    .Distinct(StringComparer.Ordinal).Count() != Fields.Length)
                throw new InvalidOperationException("WORKER_FIELDS_CHANGED");
            var counts = new Dictionary<string,int>(StringComparer.Ordinal);
            foreach (var field in Fields)
            {
                if (!rawCounts.TryGetProperty(field, out var value) ||
                    value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetInt32(out var n) || n < 0 || n > 4096)
                    throw new InvalidOperationException("WORKER_COUNTS_INVALID");
                counts.Add(field, n);
            }
            var responseMac = body.GetProperty("mac").GetString();
            if (responseMac is not { Length: 64 } ||
                !responseMac.All(Uri.IsHexDigit) ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(responseMac),
                    Convert.FromHexString(Mac(key, request, nonce,
                        "SYNTHETIC_READ_ONLY",
                        counts["datasets"].ToString(),
                        counts["modelVersions"].ToString(),
                        counts["evaluations"].ToString(),
                        counts["trainingRuns"].ToString(),
                        counts["benchmarks"].ToString()))))
                throw new InvalidOperationException("WORKER_PROOF_INVALID");
            return counts;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("WORKER_DEADLINE_EXCEEDED");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
