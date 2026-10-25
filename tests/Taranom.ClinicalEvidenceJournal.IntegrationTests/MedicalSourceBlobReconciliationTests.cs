using System.IO;
using System.Security.Cryptography;
using System.Text;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class MedicalSourceBlobReconciliationTests
{
    private static string Hash(byte[] value)=>Convert.ToHexStringLower(SHA256.HashData(value));

    [Fact]
    public async Task ReconciliationReportsReferencesOrphansTamperingAndInvalidNamesWithoutMutation()
    {
        var root=Path.Combine(Path.GetTempPath(),$"taranom-blob-reconcile-{Guid.NewGuid():N}");Directory.CreateDirectory(root);
        try
        {
            var referencedBytes=Encoding.UTF8.GetBytes("synthetic referenced package");
            var orphanBytes=Encoding.UTF8.GetBytes("synthetic orphan package");
            var tamperedBytes=Encoding.UTF8.GetBytes("synthetic original package");
            var referenced=Hash(referencedBytes);var orphan=Hash(orphanBytes);var tampered=Hash(tamperedBytes);
            await File.WriteAllBytesAsync(Path.Combine(root,referenced+".blob"),referencedBytes);
            await File.WriteAllBytesAsync(Path.Combine(root,orphan+".blob"),orphanBytes);
            await File.WriteAllBytesAsync(Path.Combine(root,tampered+".blob"),Encoding.UTF8.GetBytes("mutated package"));
            await File.WriteAllBytesAsync(Path.Combine(root,"not-a-digest.blob"),Encoding.UTF8.GetBytes("synthetic invalid name"));
            var before=Directory.GetFiles(root).OrderBy(Path.GetFileName,StringComparer.Ordinal).ToArray();
            var store=new MedicalSourcePackageBlobStore(root);
            var report=await store.ReconcileAsync([referenced,Hash(Encoding.UTF8.GetBytes("missing package")),tampered],1024,
                TestContext.Current.CancellationToken);
            Assert.Contains(report,x=>x.Identifier==referenced&&x.Kind==MedicalSourceBlobFindingKind.ReferencedIntact);
            Assert.Contains(report,x=>x.Identifier==orphan&&x.Kind==MedicalSourceBlobFindingKind.OrphanIntact);
            Assert.Contains(report,x=>x.Identifier==tampered&&x.Kind==MedicalSourceBlobFindingKind.Tampered);
            Assert.Contains(report,x=>x.Kind==MedicalSourceBlobFindingKind.MissingReference);
            Assert.Contains(report,x=>x.Identifier=="not-a-digest.blob"&&x.Kind==MedicalSourceBlobFindingKind.InvalidBlobName);
            Assert.Equal(before,Directory.GetFiles(root).OrderBy(Path.GetFileName,StringComparer.Ordinal).ToArray());
            Assert.Equal("mutated package",Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(root,tampered+".blob"))));
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,recursive:true); }
    }

    [Fact]
    public async Task ReconciliationReportsSizeLimitedBlobsWithoutCallingThemIntact()
    {
        var root=Path.Combine(Path.GetTempPath(),$"taranom-blob-reconcile-limit-{Guid.NewGuid():N}");Directory.CreateDirectory(root);
        try
        {
            var bytes=Encoding.UTF8.GetBytes("synthetic payload over the report limit");var digest=Hash(bytes);
            await File.WriteAllBytesAsync(Path.Combine(root,digest+".blob"),bytes);
            var store=new MedicalSourcePackageBlobStore(root);
            var report=await store.ReconcileAsync([digest],4,TestContext.Current.CancellationToken);
            Assert.Contains(report,x=>x.Identifier==digest&&x.Kind==MedicalSourceBlobFindingKind.ExceedsSizeLimit);
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,recursive:true); }
    }

    [Fact]
    public async Task ReconciliationRejectsInvalidReferenceDigests()
    {
        var root=Path.Combine(Path.GetTempPath(),$"taranom-blob-reconcile-input-{Guid.NewGuid():N}");
        try
        {
            var store=new MedicalSourcePackageBlobStore(root);
            await Assert.ThrowsAsync<ArgumentException>(()=>store.ReconcileAsync(["not-a-digest"],1024,
                TestContext.Current.CancellationToken));
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,recursive:true); }
    }
}
