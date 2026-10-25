using System.Text.Json;
using Npgsql;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class PgMedicalSourceBlobReconcilerTamperTests
{
    private static string Pg=>Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ??throw new InvalidOperationException("TARANOM_TEST_PG required");

    [Theory]
    [InlineData("event-hash","MEDICAL_REVIEW_CHAIN_OR_AUDIT_TAMPERED")]
    [InlineData("audit-link","MEDICAL_REVIEW_CHAIN_OR_AUDIT_TAMPERED")]
    [InlineData("missing-audit","MEDICAL_REVIEW_CHAIN_OR_AUDIT_TAMPERED")]
    [InlineData("invalid-command","MEDICAL_REVIEW_CHAIN_OR_AUDIT_TAMPERED")]
    [InlineData("head","MEDICAL_REVIEW_HEAD_DIVERGENCE")]
    public async Task ReconcilerFailsClosedOnTamperedSnapshot(string corruption,string expectedError)
    {
        await AssertTamperedSnapshotRejectedAsync(corruption,expectedError,TestContext.Current.CancellationToken);
    }

    private static async Task AssertTamperedSnapshotRejectedAsync(
        string corruption,string expectedError,CancellationToken ct)
    {
        var schema="taranom_sprint210_"+Guid.NewGuid().ToString("N");
        await using var owner=NpgsqlDataSource.Create(Pg);
        await using var setup=await owner.OpenConnectionAsync(ct);
        try
        {
            await using(var create=new NpgsqlCommand($"""
                CREATE SCHEMA {schema};
                CREATE TABLE {schema}.internal_test_medical_review_head(
                  catalog_sha256 text,taxonomy_sha256 text,current_revision bigint,head_sha256 text);
                CREATE TABLE {schema}.internal_test_medical_review_source(
                  catalog_sha256 text,source_ref text,package_sha256 text);
                CREATE TABLE {schema}.internal_test_medical_review_event(
                  catalog_sha256 text,revision bigint,request_id uuid,command_json text,
                  previous_sha256 text,event_sha256 text);
                CREATE TABLE {schema}.internal_test_medical_review_audit(
                  catalog_sha256 text,revision bigint,request_id uuid,event_sha256 text);
                """,setup))
                await create.ExecuteNonQueryAsync(ct);

            var catalog=MedicalSourceReviewIntegrity.Digest(Guid.NewGuid().ToString("N"));
            var taxonomy=MedicalSourceReviewIntegrity.Digest("synthetic taxonomy");
            var sourceRef="test:sprint210-"+Guid.NewGuid().ToString("N");
            var packageSha=MedicalSourceReviewIntegrity.Digest("synthetic source package");
            var requestId=Guid.NewGuid();
            var command=new MedicalSourceReviewCommand(requestId,0,sourceRef,packageSha,"SCIENCE",
                "RECOMMEND_ACCEPT","reviewer:claimed-test","evidence:claimed-test",
                MedicalSourceReviewIntegrity.Digest("synthetic evidence package"));
            if(corruption=="invalid-command")command=command with { Recommendation="ACCEPT" };
            var json=JsonSerializer.Serialize(command);
            var previous=MedicalSourceReviewIntegrity.Digest($"genesis\n{catalog}\n{taxonomy}");
            var validEventSha=MedicalSourceReviewIntegrity.EventDigest(previous,1,json);
            var eventSha=corruption=="event-hash"?new string('f',64):validEventSha;
            var headSha=corruption=="head"?new string('d',64):eventSha;

            await using(var insertHead=new NpgsqlCommand($"""
                INSERT INTO {schema}.internal_test_medical_review_head
                  (catalog_sha256,taxonomy_sha256,current_revision,head_sha256)
                VALUES(@catalog,@taxonomy,1,@head)
                """,setup))
            {
                insertHead.Parameters.AddWithValue("catalog",catalog);
                insertHead.Parameters.AddWithValue("taxonomy",taxonomy);
                insertHead.Parameters.AddWithValue("head",headSha);
                await insertHead.ExecuteNonQueryAsync(ct);
            }
            await using(var insertSource=new NpgsqlCommand($"""
                INSERT INTO {schema}.internal_test_medical_review_source
                  (catalog_sha256,source_ref,package_sha256) VALUES(@catalog,@source,@package)
                """,setup))
            {
                insertSource.Parameters.AddWithValue("catalog",catalog);
                insertSource.Parameters.AddWithValue("source",sourceRef);
                insertSource.Parameters.AddWithValue("package",packageSha);
                await insertSource.ExecuteNonQueryAsync(ct);
            }
            await using(var insertEvent=new NpgsqlCommand($"""
                INSERT INTO {schema}.internal_test_medical_review_event
                  (catalog_sha256,revision,request_id,command_json,previous_sha256,event_sha256)
                VALUES(@catalog,1,@request,@json,@previous,@event)
                """,setup))
            {
                insertEvent.Parameters.AddWithValue("catalog",catalog);
                insertEvent.Parameters.AddWithValue("request",requestId);
                insertEvent.Parameters.AddWithValue("json",json);
                insertEvent.Parameters.AddWithValue("previous",previous);
                insertEvent.Parameters.AddWithValue("event",eventSha);
                await insertEvent.ExecuteNonQueryAsync(ct);
            }
            if(corruption!="missing-audit")
            {
                await using var insertAudit=new NpgsqlCommand($"""
                    INSERT INTO {schema}.internal_test_medical_review_audit
                      (catalog_sha256,revision,request_id,event_sha256)
                    VALUES(@catalog,1,@request,@event)
                    """,setup);
                insertAudit.Parameters.AddWithValue("catalog",catalog);
                insertAudit.Parameters.AddWithValue("request",corruption=="audit-link"?Guid.NewGuid():requestId);
                insertAudit.Parameters.AddWithValue("event",eventSha);
                await insertAudit.ExecuteNonQueryAsync(ct);
            }

            var readerSettings=new NpgsqlConnectionStringBuilder(Pg){SearchPath=schema};
            await using var reader=NpgsqlDataSource.Create(readerSettings.ConnectionString);
            var store=new MedicalSourcePackageBlobStore(Path.Combine(Path.GetTempPath(),
                "taranom-sprint210-reconcile-"+Guid.NewGuid().ToString("N")));
            var reconciler=new PgMedicalSourceBlobReconciler(reader,store);
            var error=await Assert.ThrowsAsync<InvalidOperationException>(
                ()=>reconciler.ReconcileAsync(1024,ct));
            Assert.Equal(expectedError,error.Message);
        }
        finally
        {
            await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE",setup);
            await drop.ExecuteNonQueryAsync(ct);
        }
    }
}
