using System.IO;
using System.Security.Cryptography;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

internal enum MedicalSourceBlobFindingKind
{
    MissingReference, ReferencedIntact, OrphanIntact, Tampered, ExceedsSizeLimit, InvalidBlobName, UnsafeLinkedBlob
}
internal sealed record MedicalSourceBlobFinding(string Identifier,MedicalSourceBlobFindingKind Kind);
internal readonly record struct StoredMedicalSourcePackage(string Sha256,long LengthBytes);

// Explicit-root content-addressed storage primitive. Callers own the root and size policy.
internal sealed class MedicalSourcePackageBlobStore
{
    private readonly string root;
    internal MedicalSourcePackageBlobStore(string root)
    {
        if(string.IsNullOrWhiteSpace(root))throw new ArgumentException("A package store root is required.",nameof(root));
        this.root=Path.GetFullPath(root);Directory.CreateDirectory(this.root);
    }
    internal async Task<StoredMedicalSourcePackage> StoreAsync(Stream content,long maximumBytes,CancellationToken ct=default)
    {
        if(content is null||!content.CanRead)throw new ArgumentException("A readable package stream is required.",nameof(content));
        if(maximumBytes<=0)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var temporary=Path.Combine(root,$".ingest-{Guid.NewGuid():N}.tmp");long total=0;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,64*1024,
                FileOptions.Asynchronous|FileOptions.SequentialScan))
            {
                var buffer=new byte[64*1024];
                while(true)
                {
                    var read=await content.ReadAsync(buffer.AsMemory(),ct);if(read==0)break;
                    if(read>maximumBytes-total)throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_SIZE_LIMIT");
                    hash.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),ct);total+=read;
                }
                if(total==0)throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_EMPTY");
                await output.FlushAsync(ct);
            }
            var digest=Convert.ToHexStringLower(hash.GetHashAndReset());var destination=BlobPath(digest);
            try { File.Move(temporary,destination); }
            catch(IOException) when(File.Exists(destination))
            {
                var verified=await VerifyAsync(digest,maximumBytes,ct);
                if(!verified||new FileInfo(destination).Length!=total)throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_BLOB_CONFLICT");
            }
            return new(digest,total);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    internal async Task<bool> VerifyAsync(string sha256,long maximumBytes,CancellationToken ct=default)
    {
        if(!ValidSha(sha256))throw new ArgumentException("Invalid package SHA-256.",nameof(sha256));
        if(maximumBytes<=0)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var path=BlobPath(sha256);if(!File.Exists(path))return false;
        if(IsLinkedBlob(path))throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_LINKED_BLOB_REJECTED");
        await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,64*1024,
            FileOptions.Asynchronous|FileOptions.SequentialScan);
        var actual=await MedicalSourcePackageFingerprint.ComputeSha256Async(input,maximumBytes,ct);
        if(actual!=sha256)throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_AT_REST_TAMPERED");
        return true;
    }

    // Read-only point-in-time report. It never repairs, renames, or deletes files.
    internal async Task<IReadOnlyList<MedicalSourceBlobFinding>> ReconcileAsync(
        IEnumerable<string> referencedSha256,long maximumBytesPerBlob,CancellationToken ct=default)
    {
        if(referencedSha256 is null)throw new ArgumentNullException(nameof(referencedSha256));
        if(maximumBytesPerBlob<=0)throw new ArgumentOutOfRangeException(nameof(maximumBytesPerBlob));
        var referenced=new HashSet<string>(StringComparer.Ordinal);
        foreach(var digest in referencedSha256)
        {
            if(!ValidSha(digest))throw new ArgumentException("Invalid referenced package SHA-256.",nameof(referencedSha256));
            referenced.Add(digest);
        }
        var files=Directory.EnumerateFiles(root,"*.blob",SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName,StringComparer.Ordinal).ToArray();
        var findings=new List<MedicalSourceBlobFinding>();var validNames=new HashSet<string>(StringComparer.Ordinal);
        foreach(var path in files)
        {
            ct.ThrowIfCancellationRequested();var fileName=Path.GetFileName(path);
            var digest=Path.GetFileNameWithoutExtension(path);
            if(!ValidSha(digest)){findings.Add(new(fileName,MedicalSourceBlobFindingKind.InvalidBlobName));continue;}
            if(IsLinkedBlob(path))
            {
                findings.Add(new(digest,MedicalSourceBlobFindingKind.UnsafeLinkedBlob));continue;
            }
            validNames.Add(digest);string actual;
            try
            {
                await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,64*1024,
                    FileOptions.Asynchronous|FileOptions.SequentialScan);
                actual=await MedicalSourcePackageFingerprint.ComputeSha256Async(input,maximumBytesPerBlob,ct);
            }
            catch(InvalidOperationException ex) when(ex.Message=="MEDICAL_SOURCE_PACKAGE_SIZE_LIMIT")
            { findings.Add(new(digest,MedicalSourceBlobFindingKind.ExceedsSizeLimit));continue; }
            catch(InvalidOperationException ex) when(ex.Message=="MEDICAL_SOURCE_PACKAGE_EMPTY")
            { findings.Add(new(digest,MedicalSourceBlobFindingKind.Tampered));continue; }
            if(!StringComparer.Ordinal.Equals(actual,digest))findings.Add(new(digest,MedicalSourceBlobFindingKind.Tampered));
            else findings.Add(new(digest,referenced.Contains(digest)?MedicalSourceBlobFindingKind.ReferencedIntact:MedicalSourceBlobFindingKind.OrphanIntact));
        }
        foreach(var digest in referenced.OrderBy(x=>x,StringComparer.Ordinal))
            if(!validNames.Contains(digest))findings.Add(new(digest,MedicalSourceBlobFindingKind.MissingReference));
        return findings.OrderBy(x=>x.Identifier,StringComparer.Ordinal).ThenBy(x=>x.Kind).ToArray();
    }
    private string BlobPath(string sha256)=>Path.Combine(root,sha256+".blob");
    private static bool IsLinkedBlob(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
    private static bool ValidSha(string value)=>value is not null&&value.Length==64&&
        value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
