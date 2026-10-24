using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-154: ONLY ephemeral, locally persisted, hash-linked CI test
// evidence. SHA-256 detects uncoordinated mutation, NOT a privileged owner
// rewriting the complete file. Never treat this as a regulated audit ledger.
internal enum AiWorkerQaEvidenceKind
{
    Boot, Success, ProcessRejected, InvalidReply, DeadlineExceeded,
    RequestCancelled, Unavailable, CapacityDenied
}
internal readonly record struct AiWorkerQaEvidenceSnapshot(
    long Entries, long BootCount, string LastDigest, string LastEpoch);

internal sealed class AiWorkerQaEvidenceLedger
{
    private const long MaxBytes = 524288;
    private const int MaxEntries = 4096;
    private const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";
    private readonly object gate = new();
    private readonly string file;

    internal AiWorkerQaEvidenceLedger(string path)
    {
        if(Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
            "CI_EPHEMERAL_ONLY" ||
            Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY") !=
            "CI_EPHEMERAL_ONLY" ||
            !Path.IsPathFullyQualified(path) ||
            Path.GetFullPath(path) != path ||
            Path.GetFileName(path) != "worker-evidence.jsonl" ||
            Path.GetDirectoryName(Path.GetDirectoryName(path)) != "/tmp" ||
            !Path.GetFileName(Path.GetDirectoryName(path)!).StartsWith(
                "taranom-worker-",StringComparison.Ordinal) ||
            !Directory.Exists(Path.GetDirectoryName(path)))
            throw new InvalidOperationException("SYNTHETIC_EVIDENCE_PATH_DENIED");
        file = path;
        CheckAttributes();
    }
    private void CheckAttributes()
    {
        var directory=Path.GetDirectoryName(file)!;
        if((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("SYNTHETIC_EVIDENCE_LINK_DENIED");
        // Also reject a preexisting symlink to a different file.
        if(File.Exists(file) &&
           (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("SYNTHETIC_EVIDENCE_LINK_DENIED");
    }
    private static bool ValidEpoch(string epoch) =>
        epoch.Length == 32 && epoch.All(c =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Digest(long sequence,string epoch,string kind,
                                 string previous) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n","TRN-TECH-154",
                sequence.ToString(CultureInfo.InvariantCulture),
                epoch,kind,previous))));
    private static string Line(long sequence,string epoch,string kind,
                               string previous,string digest) =>
        JsonSerializer.Serialize(new {
            sequence,epoch,kind,previousDigest=previous,digest
        });
    private static bool ValidKind(string kind) =>
        Enum.TryParse<AiWorkerQaEvidenceKind>(kind,false,out var parsed) &&
        parsed.ToString() == kind;

    private static AiWorkerQaEvidenceSnapshot ReadVerified(FileStream stream)
    {
        if(stream.Length > MaxBytes)
            throw new InvalidOperationException("SYNTHETIC_EVIDENCE_SIZE");
        var bytes=new byte[checked((int)stream.Length)];
        stream.Position=0;
        stream.ReadExactly(bytes);
        if(bytes.Length==0)
            return new(0,0,Genesis,"");
        if(bytes[^1] != (byte)'\n')
            throw new InvalidOperationException("SYNTHETIC_EVIDENCE_PARTIAL");
        string data;
        try { data=new UTF8Encoding(false,true).GetString(bytes); }
        catch(DecoderFallbackException)
        { throw new InvalidOperationException("SYNTHETIC_EVIDENCE_ENCODING"); }
        var lines=data.Split('\n');
        if(lines.Length-1 > MaxEntries)
            throw new InvalidOperationException("SYNTHETIC_EVIDENCE_CAPACITY");
        var previous=Genesis;
        var lastEpoch="";
        long bootCount=0;
        for(int i=0;i<lines.Length-1;i++)
        {
            try
            {
                var line=lines[i];
                if(line.Length is < 100 or > 512)
                    throw new InvalidOperationException();
                using var parsed=JsonDocument.Parse(line,
                    new JsonDocumentOptions {MaxDepth=2});
                var root=parsed.RootElement;
                var fields=root.EnumerateObject().Select(e=>e.Name).ToArray();
                var expected=new[]{"sequence","epoch","kind","previousDigest","digest"};
                if(root.ValueKind!=JsonValueKind.Object || fields.Length!=5 ||
                   fields.Distinct(StringComparer.Ordinal).Count()!=5 ||
                   fields.Except(expected,StringComparer.Ordinal).Any())
                    throw new InvalidOperationException();
                var seq=root.GetProperty("sequence").GetInt64();
                var epoch=root.GetProperty("epoch").GetString() ?? "";
                var kind=root.GetProperty("kind").GetString() ?? "";
                var prev=root.GetProperty("previousDigest").GetString() ?? "";
                var hash=root.GetProperty("digest").GetString() ?? "";
                if(seq!=i+1 || !ValidEpoch(epoch) || !ValidKind(kind) ||
                   prev!=previous || hash.Length!=64 ||
                   !hash.All(c=> c is >= '0' and <= '9' or >= 'A' and <= 'F'))
                    throw new InvalidOperationException();
                var verified=Digest(seq,epoch,kind,prev);
                if(hash!=verified || line!=Line(seq,epoch,kind,prev,hash))
                    throw new InvalidOperationException();
                // Boot must be the first record for each previously unseen
                // epoch; ordinary events must belong to the current one.
                if(kind==nameof(AiWorkerQaEvidenceKind.Boot))
                {
                    if(epoch==lastEpoch) throw new InvalidOperationException();
                    bootCount++;
                }
                else if(lastEpoch!=epoch)
                    throw new InvalidOperationException();
                lastEpoch=epoch;
                previous=hash;
            }
            catch(Exception ex) when (ex is JsonException or InvalidOperationException
                or FormatException or KeyNotFoundException)
            {
                throw new InvalidOperationException("SYNTHETIC_EVIDENCE_INTEGRITY");
            }
        }
        return new(lines.Length-1,bootCount,previous,lastEpoch);
    }
    private FileStream Open()
    {
        CheckAttributes();
        return new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,
            FileShare.None,4096,FileOptions.WriteThrough);
    }
    internal AiWorkerQaEvidenceSnapshot Verify()
    {
        lock(gate)
        {
            using var stream=Open();
            return ReadVerified(stream);
        }
    }
    internal AiWorkerQaEvidenceSnapshot Append(string epoch,
                                               AiWorkerQaEvidenceKind kind)
    {
        if(!ValidEpoch(epoch) || !Enum.IsDefined(kind))
            throw new InvalidOperationException("SYNTHETIC_EVIDENCE_EVENT_DENIED");
        lock(gate)
        {
            using var stream=Open();
            var s=ReadVerified(stream);
            if(s.Entries>=MaxEntries)
                throw new InvalidOperationException("SYNTHETIC_EVIDENCE_CAPACITY");
            if(kind==AiWorkerQaEvidenceKind.Boot)
            {
                if(epoch==s.LastEpoch)
                    throw new InvalidOperationException("SYNTHETIC_EVIDENCE_DUPLICATE_BOOT");
            }
            else if(s.Entries==0 || s.LastEpoch!=epoch)
                throw new InvalidOperationException("SYNTHETIC_EVIDENCE_EPOCH_DENIED");
            var seq=s.Entries+1;
            var name=kind.ToString();
            var digest=Digest(seq,epoch,name,s.LastDigest);
            var line=Encoding.UTF8.GetBytes(Line(seq,epoch,name,s.LastDigest,digest)+"\n");
            if(stream.Length+line.Length > MaxBytes)
                throw new InvalidOperationException("SYNTHETIC_EVIDENCE_SIZE");
            stream.Position=stream.Length;
            stream.Write(line);
            stream.Flush(true);
            return new(seq,s.BootCount+(kind==AiWorkerQaEvidenceKind.Boot?1:0),
                       digest,epoch);
        }
    }
}
