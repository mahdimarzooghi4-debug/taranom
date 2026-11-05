using System.IO;
using Npgsql;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class MedicalSourceReviewPostgresTests
{
    private static string Pg=>Environment.GetEnvironmentVariable("TARANOM_TEST_PG")??throw new InvalidOperationException("TARANOM_TEST_PG required");
    private static string Hash(string s)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));
    private static string Source(string p="source")=>$"test:{p}-{Guid.NewGuid():N}";

    private static async Task<(NpgsqlDataSource Owner,NpgsqlDataSource Writer,NpgsqlDataSource Reader,PgMedicalSourceReviewJournal Journal,string Catalog,string Taxonomy,MedicalSourceReviewSource Source)> Connect(string prefix="source")
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var owner=NpgsqlDataSource.Create(Pg);
        var writerSettings=new NpgsqlConnectionStringBuilder(Pg) { Username="taranom_sprint201_medical_review_writer", Password="ephemeral_medical_review_writer" };
        var readerSettings=new NpgsqlConnectionStringBuilder(Pg) { Username="taranom_sprint201_medical_review_reader", Password="ephemeral_medical_review_reader" };
        var writer=NpgsqlDataSource.Create(writerSettings.ConnectionString);
        var reader=NpgsqlDataSource.Create(readerSettings.ConnectionString);
        var journal=new PgMedicalSourceReviewJournal(owner,writer,reader);
        var catalog=Hash(Guid.NewGuid().ToString("N"));var taxonomy=Hash("taxonomy");
        var packageBytes=System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
        var source=new MedicalSourceReviewSource(Source(prefix),Hash(System.Text.Encoding.UTF8.GetString(packageBytes)));
        using var packageStream=new MemoryStream(packageBytes,writable:false);
        await journal.OpenFromPackageStreamsAsync(catalog,taxonomy,[new(source.SourceRef,packageStream)],
            maximumBytesPerPackage:1024,TestContext.Current.CancellationToken);
        return(owner,writer,reader,journal,catalog,taxonomy,source);
    }

    private static MedicalSourceReviewCommand Command(MedicalSourceReviewSource s,long revision=0,Guid? id=null)=>
        new(id??Guid.NewGuid(),revision,s.SourceRef,s.PackageSha256,"RIGHTS","RECOMMEND_ACCEPT","reviewer:claimed-test","evidence:claimed-test");

    private static Task<MedicalSourceReviewAppendResult> Append(PgMedicalSourceReviewJournal journal,
        string catalog,string taxonomy,MedicalSourceReviewCommand command) =>
        journal.AppendAsync(catalog,taxonomy,command,TestContext.Current.CancellationToken);

    private static Task<MedicalSourceReviewSnapshot> Read(PgMedicalSourceReviewJournal journal,
        string catalog,string taxonomy) =>
        journal.ReadAsync(catalog,taxonomy,TestContext.Current.CancellationToken);

    private static async Task AssertPermissionDenied(NpgsqlDataSource source,string sql,string catalog)
    {
        await using var cn=await source.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command=new NpgsqlCommand(sql,cn);
        command.Parameters.AddWithValue("c",catalog);
        var error=await Assert.ThrowsAsync<PostgresException>(async ()=>
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("42501",error.SqlState);
    }

    [Fact]
    public async Task RuntimeWriterAndReaderRolesAreLeastPrivilege()
    {
        var (owner,writer,reader,journal,catalog,taxonomy,source)=await Connect();
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;
        var request=Command(source);
        Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await Append(journal,catalog,taxonomy,request)).Outcome);
        Assert.Single((await Read(journal,catalog,taxonomy)).Events);
        await AssertPermissionDenied(writer,
            "UPDATE internal_test_medical_review_event SET event_sha256='0000000000000000000000000000000000000000000000000000000000000000' WHERE catalog_sha256=@c",catalog);
        await AssertPermissionDenied(writer,
            "DELETE FROM internal_test_medical_review_source WHERE catalog_sha256=@c",catalog);
        await AssertPermissionDenied(writer,
            "INSERT INTO internal_test_medical_review_source(catalog_sha256,source_ref,package_sha256) VALUES(@c,'test:forbidden','1111111111111111111111111111111111111111111111111111111111111111')",catalog);
        await AssertPermissionDenied(reader,
            "UPDATE internal_test_medical_review_head SET current_revision=99 WHERE catalog_sha256=@c",catalog);
        await AssertPermissionDenied(reader,
            "INSERT INTO internal_test_medical_review_head(catalog_sha256,taxonomy_sha256,head_sha256) VALUES(@c,'2222222222222222222222222222222222222222222222222222222222222222','3333333333333333333333333333333333333333333333333333333333333333')",catalog);
    }

    [Fact]
    public async Task CatalogOpeningFingerprintsRawPackageStreamBytes()
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var owner=NpgsqlDataSource.Create(Pg);
        var writerSettings=new NpgsqlConnectionStringBuilder(Pg) { Username="taranom_sprint201_medical_review_writer", Password="ephemeral_medical_review_writer" };
        var readerSettings=new NpgsqlConnectionStringBuilder(Pg) { Username="taranom_sprint201_medical_review_reader", Password="ephemeral_medical_review_reader" };
        var writer=NpgsqlDataSource.Create(writerSettings.ConnectionString);
        var reader=NpgsqlDataSource.Create(readerSettings.ConnectionString);
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;
        var journal=new PgMedicalSourceReviewJournal(owner,writer,reader);
        var catalog=Hash(Guid.NewGuid().ToString("N"));var taxonomy=Hash("taxonomy");
        var sourceRef=Source("byte-verified");
        var packageBytes=System.Text.Encoding.UTF8.GetBytes("synthetic-only medical source package bytes");
        using var packageStream=new MemoryStream(packageBytes,writable:false);
        await journal.OpenFromPackageStreamsAsync(catalog,taxonomy,
            [new(sourceRef,packageStream)],maximumBytesPerPackage:1024,TestContext.Current.CancellationToken);
        Assert.True(packageStream.CanRead);
        var source=new MedicalSourceReviewSource(sourceRef,Hash(System.Text.Encoding.UTF8.GetString(packageBytes)));
        var request=Command(source);
        Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await Append(journal,catalog,taxonomy,request)).Outcome);
        Assert.Equal(request,Assert.Single((await Read(journal,catalog,taxonomy)).Events).Command);
    }

    [Fact]
    public async Task PackageStreamSizeLimitRejectsBeforeCatalogScopeIsCreated()
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var owner=NpgsqlDataSource.Create(Pg);
        var writerSettings=new NpgsqlConnectionStringBuilder(Pg) { Username="taranom_sprint201_medical_review_writer", Password="ephemeral_medical_review_writer" };
        var readerSettings=new NpgsqlConnectionStringBuilder(Pg) { Username="taranom_sprint201_medical_review_reader", Password="ephemeral_medical_review_reader" };
        var writer=NpgsqlDataSource.Create(writerSettings.ConnectionString);
        var reader=NpgsqlDataSource.Create(readerSettings.ConnectionString);
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;
        var journal=new PgMedicalSourceReviewJournal(owner,writer,reader);
        var catalog=Hash(Guid.NewGuid().ToString("N"));var taxonomy=Hash("taxonomy");
        using var packageStream=new MemoryStream(new byte[8],writable:false);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>journal.OpenFromPackageStreamsAsync(catalog,taxonomy,
            [new(Source("oversized"),packageStream)],maximumBytesPerPackage:4,TestContext.Current.CancellationToken));
        Assert.Equal("MEDICAL_SOURCE_PACKAGE_SIZE_LIMIT",error.Message);
        await using var cn=await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var check=new NpgsqlCommand("SELECT count(*) FROM internal_test_medical_review_head WHERE catalog_sha256=@c",cn);
        check.Parameters.AddWithValue("c",catalog);
        Assert.Equal(0L,(long)(await check.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task PackageFingerprintRejectsEmptyStreamsAndNonPositiveLimits()
    {
        using var empty=new MemoryStream();
        var emptyError=await Assert.ThrowsAsync<InvalidOperationException>(()=>
            MedicalSourcePackageFingerprint.ComputeSha256Async(empty,10,TestContext.Current.CancellationToken));
        Assert.Equal("MEDICAL_SOURCE_PACKAGE_EMPTY",emptyError.Message);
        using var bytes=new MemoryStream([1,2,3]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
            MedicalSourcePackageFingerprint.ComputeSha256Async(bytes,0,TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoredPackageMustPassIntegrityCheckBeforeReviewAppend()
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var root=Path.Combine(Path.GetTempPath(),$"taranom-source-review-{Guid.NewGuid():N}");
        try
        {
            var owner=NpgsqlDataSource.Create(Pg);
            var writerSettings=new NpgsqlConnectionStringBuilder(Pg) { Username="taranom_sprint201_medical_review_writer", Password="ephemeral_medical_review_writer" };
            var readerSettings=new NpgsqlConnectionStringBuilder(Pg) { Username="taranom_sprint201_medical_review_reader", Password="ephemeral_medical_review_reader" };
            var writer=NpgsqlDataSource.Create(writerSettings.ConnectionString);
            var reader=NpgsqlDataSource.Create(readerSettings.ConnectionString);
            await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;
            var journal=new PgMedicalSourceReviewJournal(owner,writer,reader);
            var store=new MedicalSourcePackageBlobStore(root);
            var catalog=Hash(Guid.NewGuid().ToString("N"));var taxonomy=Hash("taxonomy");
            var sourceRef=Source("stored-package");
            var bytes=System.Text.Encoding.UTF8.GetBytes("synthetic-only stored medical source bytes");
            using(var package=new MemoryStream(bytes,writable:false))
                await journal.OpenAndStorePackageStreamsAsync(catalog,taxonomy,[new(sourceRef,package)],store,
                    maximumBytesPerPackage:1024,TestContext.Current.CancellationToken);
            var source=new MedicalSourceReviewSource(sourceRef,Hash(System.Text.Encoding.UTF8.GetString(bytes)));
            var request=Command(source);
            Assert.Equal(MedicalSourceReviewOutcome.Recorded,
                (await journal.AppendWithStoredPackageAsync(catalog,taxonomy,request,store,1024,TestContext.Current.CancellationToken)).Outcome);
            Assert.True(await store.VerifyAsync(source.PackageSha256,1024,TestContext.Current.CancellationToken));
            await File.WriteAllBytesAsync(Path.Combine(root,source.PackageSha256+".blob"),new byte[bytes.Length],
                TestContext.Current.CancellationToken);
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                journal.AppendWithStoredPackageAsync(catalog,taxonomy,Command(source,revision:1),store,1024,
                    TestContext.Current.CancellationToken));
            Assert.Equal("MEDICAL_SOURCE_PACKAGE_AT_REST_TAMPERED",error.Message);
            var snapshot=await Read(journal,catalog,taxonomy);
            Assert.Equal(1L,snapshot.Revision);Assert.Single(snapshot.Events);
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,recursive:true);
        }
    }

    [Fact]
    public async Task CatalogFingerprintCannotBeReopenedWithDifferentSources()
    {
        var (owner,writer,reader,journal,catalog,taxonomy,source)=await Connect();
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;
        var added=new MedicalSourceReviewSource(Source("added"),Hash("another package"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            journal.OpenFromPackageStreamsAsync(catalog,taxonomy,
                [new(source.SourceRef,new MemoryStream([1,2,3])),new(added.SourceRef,new MemoryStream([4,5,6]))],
                maximumBytesPerPackage:16,TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            journal.OpenFromPackageStreamsAsync(catalog,taxonomy,
                [new(added.SourceRef,new MemoryStream([4,5,6]))],
                maximumBytesPerPackage:16,TestContext.Current.CancellationToken));
        Assert.Equal(0L,(await Read(journal,catalog,taxonomy)).Revision);
    }

    [Fact]
    public async Task DurableRecommendationsNeverGrantAuthority()
    {
        var (owner,writer,reader,journal,catalog,taxonomy,source)=await Connect();
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;var command=Command(source);
        Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await Append(journal,catalog,taxonomy,command)).Outcome);
        var read=await Read(journal,catalog,taxonomy);Assert.Equal(1L,read.Revision);Assert.Equal(command,Assert.Single(read.Events).Command);
        Assert.False(read.ReviewerIdentityAuthenticated);Assert.False(read.EvidenceCustodyVerified);Assert.False(read.RightsVerified);
        Assert.False(read.ScientificallyReviewed);Assert.False(read.TaxonomyApproved);Assert.False(read.TrainingAllowed);
        Assert.False(read.ClinicalUseAllowed);Assert.False(read.ProductionAllowed);
    }

    [Fact]
    public async Task ReplayIsIdempotentAndPayloadConflictRejected()
    {
        var (owner,writer,reader,journal,catalog,taxonomy,source)=await Connect();
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;var c=Command(source);
        Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await Append(journal,catalog,taxonomy,c)).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.Replayed,(await Append(journal,catalog,taxonomy,c)).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.ConflictingReplay,(await Append(journal,catalog,taxonomy,c with {Recommendation="RECOMMEND_REJECT"})).Outcome);
        Assert.Single((await Read(journal,catalog,taxonomy)).Events);
    }

    [Fact]
    public async Task StaleTaxonomyAndSourceScopesFailClosed()
    {
        var (owner,writer,reader,journal,catalog,taxonomy,source)=await Connect();
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;var c=Command(source);
        Assert.Equal(MedicalSourceReviewOutcome.ScopeDenied,(await Append(journal,catalog,Hash("other"),c)).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.ScopeDenied,(await Append(journal,catalog,taxonomy,c with {PackageSha256=Hash("other")})).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await Append(journal,catalog,taxonomy,c)).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.StaleRevision,(await Append(journal,catalog,taxonomy,c with {RequestId=Guid.NewGuid()})).Outcome);
    }

    [Fact]
    public async Task ConcurrentWritersAtOneRevisionOnlyCommitOnce()
    {
        var (owner,writer,reader,journal,catalog,taxonomy,source)=await Connect();
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;var a=Command(source);
        var b=a with {RequestId=Guid.NewGuid(),EvidenceRefClaimed="evidence:second"};
        var results=await Task.WhenAll(Append(journal,catalog,taxonomy,a),Append(journal,catalog,taxonomy,b));
        Assert.Single(results,x=>x.Outcome==MedicalSourceReviewOutcome.Recorded);
        Assert.Single(results,x=>x.Outcome==MedicalSourceReviewOutcome.StaleRevision);
        Assert.Single((await Read(journal,catalog,taxonomy)).Events);
    }

    [Fact]
    public async Task FailedAuditInsertRollsBackEventAndRevision()
    {
        var (owner,writer,reader,journal,catalog,taxonomy,source)=await Connect("abort-medical-audit");
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;var c=Command(source);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>Append(journal,catalog,taxonomy,c));
        Assert.Equal("TEST_MEDICAL_REVIEW_AUDIT_UNAVAILABLE",error.MessageText);
        var state=await Read(journal,catalog,taxonomy);Assert.Equal(0L,state.Revision);Assert.Empty(state.Events);
    }
}
