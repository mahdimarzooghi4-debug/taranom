using System.IO;
using System.Security.Cryptography;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class MedicalSourcePackageBlobStoreTests
{
    private static string NewRoot()=>Path.Combine(Path.GetTempPath(),$"taranom-source-store-{Guid.NewGuid():N}");

    [Fact]
    public async Task StoreUsesContentAddressingAndDeduplicatesIdenticalBytes()
    {
        var root=NewRoot();
        try
        {
            var store=new MedicalSourcePackageBlobStore(root);
            var bytes=System.Text.Encoding.UTF8.GetBytes("synthetic-only package fixture");
            using var first=new MemoryStream(bytes,writable:false);
            using var second=new MemoryStream(bytes,writable:false);
            var a=await store.StoreAsync(first,1024,TestContext.Current.CancellationToken);
            var b=await store.StoreAsync(second,1024,TestContext.Current.CancellationToken);
            Assert.Equal(a,b);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),a.Sha256);
            Assert.Equal(bytes.Length,a.LengthBytes);
            Assert.True(first.CanRead);
            Assert.True(await store.VerifyAsync(a.Sha256,1024,TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(root,"*.blob"));
            Assert.Empty(Directory.GetFiles(root,"*.tmp"));
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,recursive:true);
        }
    }

    [Fact]
    public async Task StoreRejectsEmptyAndOversizedStreamsWithoutLeavingArtifacts()
    {
        var root=NewRoot();
        try
        {
            var store=new MedicalSourcePackageBlobStore(root);
            using var empty=new MemoryStream();
            var emptyError=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                store.StoreAsync(empty,10,TestContext.Current.CancellationToken));
            Assert.Equal("MEDICAL_SOURCE_PACKAGE_EMPTY",emptyError.Message);
            using var oversized=new MemoryStream(new byte[8],writable:false);
            var sizeError=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                store.StoreAsync(oversized,4,TestContext.Current.CancellationToken));
            Assert.Equal("MEDICAL_SOURCE_PACKAGE_SIZE_LIMIT",sizeError.Message);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,recursive:true);
        }
    }

    [Fact]
    public async Task VerifyDetectsAtRestMutationAndRejectsUnsafeDigestPaths()
    {
        var root=NewRoot();
        try
        {
            var store=new MedicalSourcePackageBlobStore(root);
            using var input=new MemoryStream([1,2,3,4],writable:false);
            var stored=await store.StoreAsync(input,16,TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(root,stored.Sha256+".blob"),[4,3,2,1],
                TestContext.Current.CancellationToken);
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                store.VerifyAsync(stored.Sha256,16,TestContext.Current.CancellationToken));
            Assert.Equal("MEDICAL_SOURCE_PACKAGE_AT_REST_TAMPERED",error.Message);
            await Assert.ThrowsAsync<ArgumentException>(()=>
                store.VerifyAsync("../outside",16,TestContext.Current.CancellationToken));
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,recursive:true);
        }
    }

    [Fact]
    public async Task VerifyAndReconcileRejectLinkedBlobWithoutTreatingItsTargetAsStoredContent()
    {
        var root=NewRoot();
        var outside=Path.Combine(Path.GetTempPath(),$"taranom-linked-target-{Guid.NewGuid():N}.bin");
        try
        {
            Directory.CreateDirectory(root);
            var bytes=System.Text.Encoding.UTF8.GetBytes("synthetic external target");
            var digest=Convert.ToHexStringLower(SHA256.HashData(bytes));
            await File.WriteAllBytesAsync(outside,bytes,TestContext.Current.CancellationToken);
            File.CreateSymbolicLink(Path.Combine(root,digest+".blob"),outside);

            var store=new MedicalSourcePackageBlobStore(root);
            var verifyError=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                store.VerifyAsync(digest,1024,TestContext.Current.CancellationToken));
            Assert.Equal("MEDICAL_SOURCE_PACKAGE_LINKED_BLOB_REJECTED",verifyError.Message);

            var report=await store.ReconcileAsync([digest],1024,TestContext.Current.CancellationToken);
            Assert.Contains(report,x=>x.Identifier==digest&&x.Kind==MedicalSourceBlobFindingKind.UnsafeLinkedBlob);
            Assert.Contains(report,x=>x.Identifier==digest&&x.Kind==MedicalSourceBlobFindingKind.MissingReference);
            Assert.DoesNotContain(report,x=>x.Identifier==digest&&x.Kind==MedicalSourceBlobFindingKind.ReferencedIntact);
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,recursive:true);
            if(File.Exists(outside))File.Delete(outside);
        }
    }
}
