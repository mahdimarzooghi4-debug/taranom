using Npgsql;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class MedicalSourceReviewPostgresTests
{
    private static string Pg=>Environment.GetEnvironmentVariable("TARANOM_TEST_PG")??throw new InvalidOperationException("TARANOM_TEST_PG required");
    private static string Hash(string s)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));
    private static string Source(string p="source")=>$"test:{p}-{Guid.NewGuid():N}";

    private static async Task<(NpgsqlDataSource Owner,PgMedicalSourceReviewJournal Journal,string Catalog,string Taxonomy,MedicalSourceReviewSource Source)> Connect(string prefix="source")
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var owner=NpgsqlDataSource.Create(Pg);var journal=new PgMedicalSourceReviewJournal(owner);
        var catalog=Hash(Guid.NewGuid().ToString("N"));var taxonomy=Hash("taxonomy");
        var source=new MedicalSourceReviewSource(Source(prefix),Hash(Guid.NewGuid().ToString("N")));
        await journal.OpenAsync(catalog,taxonomy,[source],TestContext.Current.CancellationToken);
        return(owner,journal,catalog,taxonomy,source);
    }

    private static MedicalSourceReviewCommand Command(MedicalSourceReviewSource s,long revision=0,Guid? id=null)=>
        new(id??Guid.NewGuid(),revision,s.SourceRef,s.PackageSha256,"RIGHTS","RECOMMEND_ACCEPT","reviewer:claimed-test","evidence:claimed-test");

    [Fact]
    public async Task DurableRecommendationsNeverGrantAuthority()
    {
        var (owner,journal,catalog,taxonomy,source)=await Connect();await using var _=owner;var command=Command(source);
        Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await journal.AppendAsync(catalog,taxonomy,command)).Outcome);
        var read=await journal.ReadAsync(catalog,taxonomy);Assert.Equal(1L,read.Revision);Assert.Equal(command,Assert.Single(read.Events).Command);
        Assert.False(read.ReviewerIdentityAuthenticated);Assert.False(read.EvidenceCustodyVerified);Assert.False(read.RightsVerified);
        Assert.False(read.ScientificallyReviewed);Assert.False(read.TaxonomyApproved);Assert.False(read.TrainingAllowed);
        Assert.False(read.ClinicalUseAllowed);Assert.False(read.ProductionAllowed);
    }

    [Fact]
    public async Task ReplayIsIdempotentAndPayloadConflictRejected()
    {
        var (owner,journal,catalog,taxonomy,source)=await Connect();await using var _=owner;var c=Command(source);
        Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await journal.AppendAsync(catalog,taxonomy,c)).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.Replayed,(await journal.AppendAsync(catalog,taxonomy,c)).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.ConflictingReplay,(await journal.AppendAsync(catalog,taxonomy,c with {Recommendation="RECOMMEND_REJECT"})).Outcome);
        Assert.Single((await journal.ReadAsync(catalog,taxonomy)).Events);
    }

    [Fact]
    public async Task StaleTaxonomyAndSourceScopesFailClosed()
    {
        var (owner,journal,catalog,taxonomy,source)=await Connect();await using var _=owner;var c=Command(source);
        Assert.Equal(MedicalSourceReviewOutcome.ScopeDenied,(await journal.AppendAsync(catalog,Hash("other"),c)).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.ScopeDenied,(await journal.AppendAsync(catalog,taxonomy,c with {PackageSha256=Hash("other")})).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.Recorded,(await journal.AppendAsync(catalog,taxonomy,c)).Outcome);
        Assert.Equal(MedicalSourceReviewOutcome.StaleRevision,(await journal.AppendAsync(catalog,taxonomy,c with {RequestId=Guid.NewGuid()})).Outcome);
    }

    [Fact]
    public async Task ConcurrentWritersAtOneRevisionOnlyCommitOnce()
    {
        var (owner,journal,catalog,taxonomy,source)=await Connect();await using var _=owner;var a=Command(source);
        var b=a with {RequestId=Guid.NewGuid(),EvidenceRefClaimed="evidence:second"};
        var results=await Task.WhenAll(journal.AppendAsync(catalog,taxonomy,a),journal.AppendAsync(catalog,taxonomy,b));
        Assert.Single(results,x=>x.Outcome==MedicalSourceReviewOutcome.Recorded);
        Assert.Single(results,x=>x.Outcome==MedicalSourceReviewOutcome.StaleRevision);
        Assert.Single((await journal.ReadAsync(catalog,taxonomy)).Events);
    }

    [Fact]
    public async Task FailedAuditInsertRollsBackEventAndRevision()
    {
        var (owner,journal,catalog,taxonomy,source)=await Connect("abort-medical-audit");await using var _=owner;var c=Command(source);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>journal.AppendAsync(catalog,taxonomy,c));
        Assert.Equal("TEST_MEDICAL_REVIEW_AUDIT_UNAVAILABLE",error.MessageText);
        var state=await journal.ReadAsync(catalog,taxonomy);Assert.Equal(0L,state.Revision);Assert.Empty(state.Events);
    }
}
