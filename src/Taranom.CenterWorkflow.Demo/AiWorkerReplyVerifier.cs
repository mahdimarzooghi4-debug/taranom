using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-150: exact response admission before HTTP may disclose even
// synthetic counts. This is NOT a real service-identity verifier.
internal static class AiWorkerReplyVerifier
{
    internal const string Contract = "TRN-TECH-149";
    internal const string Command = "READ_SYNTHETIC_INVENTORY";
    internal const string Status = "SYNTHETIC_READ_ONLY";
    internal static readonly string[] Fields =
        ["datasets", "modelVersions", "evaluations", "trainingRuns", "benchmarks"];
    private static readonly string[] Names =
    [
        "contract", "command", "requestRef", "tenantRef", "nonce",
        "status", "counts", "syntheticOnly", "adminAuthenticated",
        "trainingAllowed", "productionAllowed", "mac"
    ];
    internal static string Mac(byte[] key, params string[] parts) =>
        Convert.ToHexString(HMACSHA256.HashData(key,
            Encoding.UTF8.GetBytes(string.Join("\n", parts))));

    internal static Dictionary<string,int> Verify(ReadOnlyMemory<byte> bytes,
        byte[] key, string request, string tenant, string nonce)
    {
        try
        {
            if (bytes.Length is < 2 or > 2048 || key.Length != 32)
                throw new InvalidOperationException();
            // JSON's permissive replacement UTF-8 decoder must not allow
            // malformed byte sequences to become admitted response evidence.
            _ = new UTF8Encoding(false, true).GetString(bytes.Span);
            using var doc = JsonDocument.Parse(bytes,
                new JsonDocumentOptions {MaxDepth=3});
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException();
            var names = root.EnumerateObject().Select(x => x.Name).ToArray();
            if (names.Length != Names.Length ||
                names.Distinct(StringComparer.Ordinal).Count() != Names.Length ||
                names.Except(Names, StringComparer.Ordinal).Any())
                throw new InvalidOperationException();
            static bool EqualsText(JsonElement obj,string name,string expected)
                => obj.GetProperty(name).ValueKind == JsonValueKind.String &&
                   obj.GetProperty(name).GetString() == expected;
            if (!EqualsText(root,"contract",Contract) ||
                !EqualsText(root,"command",Command) ||
                !EqualsText(root,"requestRef",request) ||
                !EqualsText(root,"tenantRef",tenant) ||
                !EqualsText(root,"nonce",nonce) ||
                !EqualsText(root,"status",Status) ||
                root.GetProperty("syntheticOnly").ValueKind != JsonValueKind.True ||
                root.GetProperty("adminAuthenticated").ValueKind != JsonValueKind.False ||
                root.GetProperty("trainingAllowed").ValueKind != JsonValueKind.False ||
                root.GetProperty("productionAllowed").ValueKind != JsonValueKind.False)
                throw new InvalidOperationException();
            var data = root.GetProperty("counts");
            if (data.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException();
            var countNames = data.EnumerateObject().Select(x => x.Name).ToArray();
            if (countNames.Length != Fields.Length ||
                countNames.Distinct(StringComparer.Ordinal).Count() != Fields.Length ||
                countNames.Except(Fields, StringComparer.Ordinal).Any())
                throw new InvalidOperationException();
            var counts = new Dictionary<string,int>(StringComparer.Ordinal);
            foreach (var field in Fields)
            {
                var value = data.GetProperty(field);
                if (value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetInt32(out var n) || n is < 0 or > 4096)
                    throw new InvalidOperationException();
                counts.Add(field,n);
            }
            var proof = root.GetProperty("mac");
            if (proof.ValueKind != JsonValueKind.String ||
                proof.GetString() is not {Length:64} encoded ||
                !encoded.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'))
                throw new InvalidOperationException();
            var expected = Mac(key,Contract,Command,request,tenant,nonce,Status,
                "true","false","false","false",
                counts["datasets"].ToString(CultureInfo.InvariantCulture),
                counts["modelVersions"].ToString(CultureInfo.InvariantCulture),
                counts["evaluations"].ToString(CultureInfo.InvariantCulture),
                counts["trainingRuns"].ToString(CultureInfo.InvariantCulture),
                counts["benchmarks"].ToString(CultureInfo.InvariantCulture));
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(encoded),Convert.FromHexString(expected)))
                throw new InvalidOperationException();
            return counts;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException
            or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // Consistent fail-closed error; never echo worker JSON or secrets.
            throw new InvalidOperationException("WORKER_REPLY_UNVERIFIABLE");
        }
    }
}
