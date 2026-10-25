using System.IO;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class PgMedicalSourceBlobReconcilerTests
{
    private static string Pg=>Environment.GetEnvironmentVariable("TARANOM_TEST_PG")??throw new InvalidOperationException("TARANOM_TEST_PG required");
    private static string Hash(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Source(string name)=>$"test:{name}-{Guid.NewGuid():N}";

    [Fact]
    public async Task ReconciliationDerivesReferencesAndValidatesAllJournalsFromOneReadOnlySnapshot()
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var root=Path.Combine(Path.GetTempPath(),$"taranom-pg-blob-reconcile-{Guid.NewGuid():N}");
        try
        {
            var owner=NpgsqlDataSource.Create(Pg);
            var writerSettings=new NpgsqlConnectionStringBuilder(Pg){Username="taranom_sprint201_medical_review_writer",Password="ephemeral_medical_review_writer"};
            var readerSettings=new NpgsqlConnectionStringBuilder(Pg){Username="taranom_sprint201_medical_review_reader",Password="ephemeral_medical_review_reader"};
            var writer=NpgsqlDataSource.Create(writerSettings.ConnectionString);
            var reader=NpgsqlDataSource.Create(readerSettings.ConnectionString);
            await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;
            var journal=new PgMedicalSourceReviewJournal(owner,writer,reader);
            var store=new MedicalSourcePackageBlobStore(root);
            var taxonomy=Hash("synthetic taxonomy");
            var catalog=Hash(Guid.NewGuid().ToString("N"));
            var sourceRef=Source("reconcile-source");
            var sourceBytes=Encoding.UTF8.GetBytes("synthetic catalog package");
            var sourceSha=Hash(Encoding.UTF8.GetString(sourceBytes));
            using(var sourceStream=new MemoryStream(sourceBytes,writable:false))
                await journal.OpenAndStorePackageStreamsAsync(catalog,taxonomy,[new(sourceRef,sourceStream)],store,1024,
                    TestContext.Current.CancellationToken);

            var evidenceBytes=Encoding.UTF8.GetBytes("synthetic review evidence");
            var evidenceSha=Hash(Encoding.UTF8.GetString(evidenceBytes));
            var submission=new MedicalSourceReviewSubmission(Guid.NewGuid(),0,sourceRef,sourceSha,"RIGHTS",
                "RECOMMEND_ACCEPT","reviewer:claimed-test","evidence:claimed-test");
            using(var evidenceStream=new MemoryStream(evidenceBytes,writable:false))
                Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await journal.AppendWithStoredEvidenceAsync(
                    catalog,taxonomy,submission,evidenceStream,store,1024,TestContext.Current.CancellationToken)).Outcome);

            var missingCatalog=Hash(Guid.NewGuid().ToString("N"));
            var missingBytes=Encoding.UTF8.GetBytes("synthetic missing package fixture");
            using(var package=new MemoryStream(missingBytes,writable:false))
                await journal.OpenFromPackageStreamsAsync(missingCatalog,taxonomy,
                    [new(Source("missing-blob"),package)],1024,TestContext.Current.CancellationToken);
            var missingSha=Hash(Encoding.UTF8.GetString(missingBytes));

            var orphanBytes=Encoding.UTF8.GetBytes("synthetic unreferenced blob");
            string orphanSha;
            using(var orphanStream=new MemoryStream(orphanBytes,writable:false))
                orphanSha=(await store.StoreAsync(orphanStream,1024,TestContext.Current.CancellationToken)).Sha256;

            var reconciler=new PgMedicalSourceBlobReconciler(reader,store);
            var report=await reconciler.ReconcileAsync(1024,TestContext.Current.CancellationToken);
            Assert.Contains(report,x=>x.Identifier==sourceSha&&x.Kind==MedicalSourceBlobFindingKind.ReferencedIntact);
            Assert.Contains(report,x=>x.Identifier==evidenceSha&&x.Kind==MedicalSourceBlobFindingKind.ReferencedIntact);
            Assert.Contains(report,x=>x.Identifier==missingSha&&x.Kind==MedicalSourceBlobFindingKind.MissingReference);
            Assert.Contains(report,x=>x.Identifier==orphanSha&&x.Kind==MedicalSourceBlobFindingKind.OrphanIntact);
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,recursive:true); }
    }
}
