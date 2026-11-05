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
        var source=new MedicalSourceReviewSource(Source(prefix),Hash(Guid.NewGuid().ToString("N")));
        await journal.OpenAsync(catalog,taxonomy,[source],TestContext.Current.CancellationToken);
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
    public async Task CatalogFingerprintCannotBeReopenedWithDifferentSources()
    {
        var (owner,writer,reader,journal,catalog,taxonomy,source)=await Connect();
        await using var ownerScope=owner;await using var writerScope=writer;await using var readerScope=reader;
        var added=new MedicalSourceReviewSource(Source("added"),Hash("another package"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            journal.OpenAsync(catalog,taxonomy,[source,added],TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            journal.OpenAsync(catalog,taxonomy,[added],TestContext.Current.CancellationToken));
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
