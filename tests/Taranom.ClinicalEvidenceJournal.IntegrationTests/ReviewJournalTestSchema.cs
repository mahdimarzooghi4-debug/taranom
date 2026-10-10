using Npgsql;
using Taranom.ClinicalEvidenceJournal.Postgres;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

// Exactly ONE async schema setup task shared by parallel xUnit test classes.
// Concurrent CREATE TABLE IF NOT EXISTS is not safe for a fresh PostgreSQL
// schema because pg_type names can race before either transaction commits.
internal static class ReviewJournalTestSchema
{
    private static readonly Lazy<Task> Once = new(InitializeAsync);

    internal static Task EnsureAsync() => Once.Value;

    private static async Task InitializeAsync()
    {
        var url = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
            ?? throw new InvalidOperationException(
                "TARANOM_TEST_PG required for isolated synthetic CI");
        await using var owner = NpgsqlDataSource.Create(url);
        await PgClinicalEvidenceJournal.InitializeIsolatedTestSchemaAsync(owner);
        await PgFrozenUnverifiedReviewQueue.InitializeTestSchemaAsync(owner);
        await PgHumanReviewNoteJournal.InitializeTestSchemaAsync(owner);
        await PgReviewerConflictDisclosureJournal.InitializeTestSchemaAsync(owner);
        await using var cn = await owner.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            DO $block$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles
                WHERE rolname = 'taranom_sprint11_review_reader') THEN
                CREATE ROLE taranom_sprint11_review_reader LOGIN
                  PASSWORD 'ephemeral_synthetic_review_only';
              END IF;
            END $block$;
            GRANT USAGE ON SCHEMA public TO taranom_sprint11_review_reader;
            GRANT SELECT ON internal_test_review_case, internal_test_review_event,
              internal_test_review_work_item, internal_test_review_audit
              TO taranom_sprint11_review_reader;
            DO $block$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles
                WHERE rolname = 'taranom_sprint12_snapshot_capture') THEN
                CREATE ROLE taranom_sprint12_snapshot_capture LOGIN
                  PASSWORD 'ephemeral_synthetic_snapshot_capture';
              END IF;
            END $block$;
            GRANT USAGE ON SCHEMA public TO taranom_sprint12_snapshot_capture;
            GRANT SELECT ON internal_test_review_case,internal_test_review_event,
              internal_test_review_work_item,internal_test_review_audit
              TO taranom_sprint12_snapshot_capture;
            GRANT INSERT,SELECT ON internal_test_frozen_review_snapshot
              TO taranom_sprint12_snapshot_capture;
            """, cn);
        await command.ExecuteNonQueryAsync();
    }
}
