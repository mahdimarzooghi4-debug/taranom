using Npgsql;
using Taranom.ClinicalCenterGovernance;
using Taranom.ClinicalCenterJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalCenterJournal.IntegrationTests;

// Only ephemeral CI PostgreSQL, isolated test: IDs and hardcoded CI-test role.
// Never connect this fixture to a Production center, real doctor or patient.
public sealed class CenterJournalDatabaseTests
{
    private static readonly Lazy<Task> Ready = new(BootstrapAsync);
    private const string RuntimeUser = "taranom_sprint06_center_test";
    private const string RuntimePassword = "only_ephemeral_center_journal_test";

    private static string AdminConnection => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG required for isolated center CI");

    private static async Task BootstrapAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await PgClinicalCenterJournal.InitializeIsolatedTestSchemaAsync(admin, ct);
        await using var cn = await admin.OpenConnectionAsync(ct);
        await using var grants = new NpgsqlCommand("""
            DO $role$
            BEGIN
                IF NOT EXISTS(SELECT 1 FROM pg_roles
                    WHERE rolname='taranom_sprint06_center_test') THEN
                    CREATE ROLE taranom_sprint06_center_test
                        LOGIN PASSWORD 'only_ephemeral_center_journal_test';
                END IF;
            END;
            $role$;
            REVOKE ALL ON internal_test_center,
                internal_test_center_department, internal_test_center_doctor,
                internal_test_center_assignment, internal_test_center_event,
                internal_test_center_audit, internal_test_center_denial FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO taranom_sprint06_center_test;
            GRANT SELECT,INSERT,UPDATE ON internal_test_center
                TO taranom_sprint06_center_test;
            GRANT SELECT,INSERT ON internal_test_center_department,
                internal_test_center_assignment TO taranom_sprint06_center_test;
            GRANT DELETE ON internal_test_center_assignment TO taranom_sprint06_center_test;
            GRANT SELECT,INSERT,UPDATE ON internal_test_center_doctor
                TO taranom_sprint06_center_test;
            GRANT SELECT,INSERT ON internal_test_center_event,
                internal_test_center_audit,internal_test_center_denial
                TO taranom_sprint06_center_test;
            """, cn);
        await grants.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(NpgsqlDataSource Db, PgClinicalCenterJournal Store)> ConnectAsync()
    {
        await Ready.Value.WaitAsync(TestContext.Current.CancellationToken);
        var settings = new NpgsqlConnectionStringBuilder(AdminConnection)
        {
            Username = RuntimeUser, Password = RuntimePassword
        };
        var db = NpgsqlDataSource.Create(settings.ConnectionString);
        return (db, new PgClinicalCenterJournal(db));
    }

    private static string NewCenter(string prefix = "center") =>
        "test:" + prefix + "-" + Guid.NewGuid().ToString("N");

    private static CenterCommand Cmd(
        string centerRef, CenterCommandKind kind, long version,
        string? department = null, string? doctor = null) =>
        new(Guid.NewGuid(), version, kind, centerRef, centerRef, department, doctor,
            CenterEvidence.Confirmed, CenterEvidence.Confirmed);

    private static Task<CenterChange> Apply(
        PgClinicalCenterJournal store, string center, CenterCommand command) =>
        store.ApplyAsync(center, command, TestContext.Current.CancellationToken);

    private static Task<CenterSnapshot> Read(PgClinicalCenterJournal store, string center) =>
        store.ReadAsync(center, TestContext.Current.CancellationToken);

    private static async Task<long> Count(NpgsqlDataSource db, string table, string center)
    {
        if (table is not ("internal_test_center" or "internal_test_center_department"
            or "internal_test_center_doctor" or "internal_test_center_assignment"
            or "internal_test_center_event" or "internal_test_center_audit"
            or "internal_test_center_denial"))
            throw new ArgumentException("Unknown isolated table", nameof(table));

        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE center_ref=@center", cn);
        cmd.Parameters.AddWithValue("center", center);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingCenterReadsEmptyWithoutCreatingARecord()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var snapshot = await Read(store, center);
        Assert.Equal(0, snapshot.Version);
        Assert.Empty(snapshot.Departments);
        Assert.Equal(0L, await Count(db, "internal_test_center", center));
    }

    [Fact]
    public async Task TwoDepartmentsDoctorAndTwoAssignmentsSurviveFreshReads()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var d1 = "test:ward_1";
        var d2 = "test:ward-2";
        var doctor = "test:doctor-a";
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center, Cmd(center,
            CenterCommandKind.DefineDepartment, 0, d1))).Outcome);
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center, Cmd(center,
            CenterCommandKind.DefineDepartment, 1, d2))).Outcome);
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center, Cmd(center,
            CenterCommandKind.RecordDoctorAffiliation, 2, doctor: doctor))).Outcome);
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center, Cmd(center,
            CenterCommandKind.AssignDoctorToDepartment, 3, d1, doctor))).Outcome);
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center, Cmd(center,
            CenterCommandKind.AssignDoctorToDepartment, 4, d2, doctor))).Outcome);
        var persisted = await Read(store, center);
        Assert.Equal(5L, persisted.Version);
        Assert.Equal(2, persisted.Departments.Count);
        Assert.Equal(2, persisted.DoctorDepartments[doctor].Count);
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, persisted.Doctors[doctor]);
        Assert.Equal(5, persisted.Events.Length);
        Assert.Equal(5L, await Count(db, "internal_test_center_event", center));
        Assert.Equal(5L, await Count(db, "internal_test_center_audit", center));
        Assert.Equal(2L, await Count(db, "internal_test_center_assignment", center));
    }

    [Fact]
    public async Task TwoCentersCannotModifyEachOthersAggregate()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var first = NewCenter();
        var second = NewCenter();
        var command = Cmd(first, CenterCommandKind.DefineDepartment, 0, "test:ward");
        Assert.Equal(CenterOutcome.ScopeDenied, (await Apply(store, second, command)).Outcome);
        Assert.Equal(0L, await Count(db, "internal_test_center", second));
        Assert.Equal(1L, await Count(db, "internal_test_center_denial", second));
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, first, command)).Outcome);
        Assert.Equal(1L, (await Read(store, first)).Version);
        Assert.Equal(0L, (await Read(store, second)).Version);
    }

    [Fact]
    public async Task HistoricalExactReplayDoesNotRepeatMutationOrDenial()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var original = Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:one");
        await Apply(store, center, original);
        await Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 1, "test:two"));
        Assert.Equal(CenterOutcome.Replayed, (await Apply(store, center, original)).Outcome);
        Assert.Equal(2L, await Count(db, "internal_test_center_event", center));
        Assert.Equal(2L, await Count(db, "internal_test_center_audit", center));
        Assert.Equal(0L, await Count(db, "internal_test_center_denial", center));
    }

    [Fact]
    public async Task ConflictingIdempotencyKeyRecordsRejectionButNotAnEvent()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var original = Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:one");
        await Apply(store, center, original);
        Assert.Equal(CenterOutcome.ConflictingReplay, (await Apply(store, center,
            original with { DepartmentRef = "test:two" })).Outcome);
        Assert.Equal(1L, await Count(db, "internal_test_center_event", center));
        Assert.Equal(1L, await Count(db, "internal_test_center_denial", center));
    }

    [Fact]
    public async Task CompetingWritersCannotBothCommitAtSameRevision()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var results = await Task.WhenAll(
            Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:one")),
            Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:two")));
        Assert.Single(results, result => result.Outcome == CenterOutcome.Applied);
        Assert.Single(results, result => result.Outcome == CenterOutcome.StaleRevision);
        Assert.Equal(1L, await Count(db, "internal_test_center_event", center));
        Assert.Equal(1L, await Count(db, "internal_test_center_denial", center));
    }

    [Fact]
    public async Task ConcurrentSameRequestHasSingleEventAndAnExactReplay()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var command = Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:one");
        var results = await Task.WhenAll(
            Apply(store, center, command), Apply(store, center, command));
        Assert.Single(results, x => x.Outcome == CenterOutcome.Applied);
        Assert.Single(results, x => x.Outcome == CenterOutcome.Replayed);
        Assert.Equal(1L, await Count(db, "internal_test_center_event", center));
        Assert.Equal(1L, await Count(db, "internal_test_center_audit", center));
    }

    [Fact]
    public async Task InvalidOrMissingManagerAuthorityNeverCreatesCenter()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var command = Cmd(center, CenterCommandKind.RecordDoctorAffiliation,
            0, doctor: "test:doctor");
        Assert.Equal(CenterOutcome.EvidenceMissing,
            (await Apply(store, center, command with
            { CurrentCenterManagerAuthority = CenterEvidence.Missing })).Outcome);
        Assert.Equal(CenterOutcome.EvidenceDenied,
            (await Apply(store, center, command with
            { CurrentCenterManagerAuthority = CenterEvidence.Rejected })).Outcome);
        Assert.Equal(0L, await Count(db, "internal_test_center", center));
        Assert.Equal(0L, await Count(db, "internal_test_center_doctor", center));
        Assert.Equal(2L, await Count(db, "internal_test_center_denial", center));
    }

    [Fact]
    public async Task NonexistentDepartmentAssignmentDeniesWithoutCreatingLinks()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var c = Cmd(center, CenterCommandKind.AssignDoctorToDepartment, 0,
            "test:missing-ward", "test:unknown-doctor");
        Assert.Equal(CenterOutcome.NotApplicable, (await Apply(store, center, c)).Outcome);
        Assert.Equal(0L, await Count(db, "internal_test_center", center));
        Assert.Equal(1L, await Count(db, "internal_test_center_denial", center));
    }

    [Fact]
    public async Task EndingAffiliationClearsCurrentAssignmentsAndPreservesEvents()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        var doctor = "test:doctor";
        await Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:ward"));
        await Apply(store, center, Cmd(center, CenterCommandKind.RecordDoctorAffiliation, 1, doctor: doctor));
        await Apply(store, center, Cmd(center, CenterCommandKind.AssignDoctorToDepartment, 2, "test:ward", doctor));
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center,
            Cmd(center, CenterCommandKind.EndDoctorAffiliation, 3, doctor: doctor))).Outcome);
        var persisted = await Read(store, center);
        Assert.Equal(DoctorAffiliation.Ended, persisted.Doctors[doctor]);
        Assert.Empty(persisted.DoctorDepartments);
        Assert.Equal(4, persisted.Events.Length);
        Assert.Equal(0L, await Count(db, "internal_test_center_assignment", center));
        Assert.Equal(4L, await Count(db, "internal_test_center_audit", center));
        Assert.Equal(CenterOutcome.NotApplicable, (await Apply(store, center,
            Cmd(center, CenterCommandKind.RecordDoctorAffiliation, 4, doctor: doctor))).Outcome);
        Assert.Equal(4L, (await Read(store, center)).Version);
    }

    [Fact]
    public async Task AcceptedAuditFailureRollsBackEveryProjectionAndHistoryInsert()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter("abort-audit");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:ward")));
        Assert.Equal("TEST_CENTER_AUDIT_UNAVAILABLE", error.MessageText);
        Assert.Equal("P0001", error.SqlState);
        foreach (var table in new[] { "internal_test_center", "internal_test_center_department",
                     "internal_test_center_event", "internal_test_center_audit" })
            Assert.Equal(0L, await Count(db, table, center));
    }

    [Fact]
    public async Task DenialAuditFailureThrowsAndDoesNotCreateState()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter("abort-denial");
        var input = Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:ward")
            with { CurrentCenterManagerAuthority = CenterEvidence.Rejected };
        var error = await Assert.ThrowsAsync<PostgresException>(() => Apply(store, center, input));
        Assert.Equal("TEST_CENTER_DENIAL_AUDIT_UNAVAILABLE", error.MessageText);
        Assert.Equal(0L, await Count(db, "internal_test_center", center));
        Assert.Equal(0L, await Count(db, "internal_test_center_denial", center));
    }

    [Fact]
    public async Task RuntimeCannotRewriteOrDeleteEventsOrAudits()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        await Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:ward"));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach (var table in new[] { "internal_test_center_event", "internal_test_center_audit",
                     "internal_test_center_denial" })
        {
            await using var command = new NpgsqlCommand(
                $"DELETE FROM {table} WHERE center_ref=@center", cn);
            command.Parameters.AddWithValue("center", center);
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", error.SqlState);
        }
        Assert.Equal(1L, await Count(db, "internal_test_center_event", center));
    }

    [Fact]
    public async Task OwnerNormalDmlCannotRemoveCommittedAudit()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        await Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:ward"));
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await using var cn = await admin.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM internal_test_center_audit WHERE center_ref=@center
            """, cn);
        cmd.Parameters.AddWithValue("center", center);
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("TEST_CENTER_APPEND_ONLY", error.MessageText);
    }

    [Fact]
    public async Task PrivilegedProjectionTamperingBlocksReadsAndNewWrites()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        await Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:ward"));
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await using (var cn = await admin.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_center SET current_version=0
            WHERE center_ref=@center
            """, cn))
        {
            update.Parameters.AddWithValue("center", center);
            await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var read = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(store, center));
        Assert.Equal("TEST_CENTER_JOURNAL_CORRUPT", read.Message);
        var write = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 1, "test:ward-two")));
        Assert.Equal("TEST_CENTER_JOURNAL_CORRUPT", write.Message);
    }

    [Fact]
    public async Task MaterializedDepartmentTamperingDetectedEvenWhenVersionMatches()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter();
        await Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:ward"));
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await using (var cn = await admin.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO internal_test_center_department(center_ref,department_ref)
            VALUES (@center,'test:injected')
            """, cn))
        {
            insert.Parameters.AddWithValue("center", center);
            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(store, center));
        Assert.Equal("TEST_CENTER_PROJECTION_CORRUPT", error.Message);
    }

    [Fact]
    public async Task RealIdentifiersAreRejectedAndNeverPersisted()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Apply(store, "real:hospital-123", Cmd("real:hospital-123",
                CenterCommandKind.DefineDepartment, 0, "test:ward")));
        var center = NewCenter();
        var invalid = Cmd(center, CenterCommandKind.RecordDoctorAffiliation, 0,
            doctor: "national-id:1234");
        Assert.Equal(CenterOutcome.Invalid, (await Apply(store, center, invalid)).Outcome);
        Assert.Equal(0L, await Count(db, "internal_test_center", center));
        Assert.Equal(1L, await Count(db, "internal_test_center_denial", center));
    }


    [Fact]
    public async Task SelectiveUnassignmentHasOneAuditAndPreservesAnotherWard()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter("selective"), doctor = "test:doctor";
        var a = "test:ward-a"; var b = "test:ward-b";
        await Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 0, a));
        await Apply(store, center, Cmd(center, CenterCommandKind.DefineDepartment, 1, b));
        await Apply(store, center, Cmd(center, CenterCommandKind.RecordDoctorAffiliation, 2, doctor: doctor));
        await Apply(store, center, Cmd(center, CenterCommandKind.AssignDoctorToDepartment, 3, a, doctor));
        await Apply(store, center, Cmd(center, CenterCommandKind.AssignDoctorToDepartment, 4, b, doctor));
        var original = Cmd(center, CenterCommandKind.UnassignDoctorFromDepartment, 5, a, doctor);
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center, original)).Outcome);
        var read = await Read(store, center);
        Assert.Equal(6L, read.Version);
        Assert.Equal(new[] { b }, read.DoctorDepartments[doctor]);
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, read.Doctors[doctor]);
        Assert.Equal(1L, await Count(db, "internal_test_center_assignment", center));
        Assert.Equal(6L, await Count(db, "internal_test_center_event", center));
        Assert.Equal(6L, await Count(db, "internal_test_center_audit", center));
        Assert.Equal(CenterOutcome.Replayed, (await Apply(store, center, original)).Outcome);
        Assert.Equal(CenterOutcome.NotApplicable, (await Apply(store, center, Cmd(center,
            CenterCommandKind.UnassignDoctorFromDepartment, 6, a, doctor))).Outcome);
        Assert.Equal(CenterOutcome.StaleRevision, (await Apply(store, center, Cmd(center,
            CenterCommandKind.UnassignDoctorFromDepartment, 5, b, doctor))).Outcome);
        Assert.Equal(6L, await Count(db, "internal_test_center_audit", center));
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center, Cmd(center,
            CenterCommandKind.UnassignDoctorFromDepartment, 6, b, doctor))).Outcome);
        var last = await Read(store, center);
        Assert.Equal(7L, last.Version);
        Assert.Empty(last.DoctorDepartments);
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, last.Doctors[doctor]);
        Assert.Equal(0L, await Count(db, "internal_test_center_assignment", center));
        Assert.Equal(7L, await Count(db, "internal_test_center_audit", center));
    }

    [Fact]
    public async Task IdenticalSyntheticDoctorKeysStayScopedToTheirOwnCenter()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var centerA = NewCenter();
        var centerB = NewCenter();
        var a = Cmd(centerA, CenterCommandKind.RecordDoctorAffiliation, 0, doctor: "test:doctor-x");
        var b = Cmd(centerB, CenterCommandKind.RecordDoctorAffiliation, 0, doctor: "test:doctor-x");
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, centerA, a)).Outcome);
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, centerB, b)).Outcome);
        Assert.Equal(1L, await Count(db, "internal_test_center_doctor", centerA));
        Assert.Equal(1L, await Count(db, "internal_test_center_doctor", centerB));
        // No one has proven both synthetic strings represent one real person.
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, (await Read(store, centerA)).Doctors["test:doctor-x"]);
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, (await Read(store, centerB)).Doctors["test:doctor-x"]);
    }

    [Fact]
    public async Task ReadOnlyMvccSeesLastCommittedSnapshotWithoutWaitingForCenterWriterLock()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter("mvcc");
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center,
            Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:committed"))).Outcome);

        // Hold a genuine PostgreSQL row UPDATE lock and an uncommitted
        // relational projection change on a separate connection. The read
        // must neither wait on the writer row lock nor expose the new values.
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await using var cn = await admin.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var held = await cn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        try
        {
            await using (var update = new NpgsqlCommand("""
                UPDATE internal_test_center SET current_version=99 WHERE center_ref=@center
                """, cn, held))
            {
                update.Parameters.AddWithValue("center", center);
                Assert.Equal(1, await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            }
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO internal_test_center_department(center_ref,department_ref)
                VALUES (@center,'test:uncommitted')
                """, cn, held))
            {
                insert.Parameters.AddWithValue("center", center);
                await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            var current = await store.ReadAsync(center, deadline.Token);
            Assert.Equal(1L, current.Version);
            Assert.Single(current.Departments);
            Assert.Contains("test:committed", current.Departments);
            Assert.DoesNotContain("test:uncommitted", current.Departments);
            Assert.Single(current.Events);
        }
        finally
        {
            await held.RollbackAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center,
            Cmd(center, CenterCommandKind.DefineDepartment, 1, "test:after-rollback"))).Outcome);
        var committed = await Read(store, center);
        Assert.Equal(2L, committed.Version);
        Assert.Equal(2, committed.Departments.Count);
        Assert.Equal(2L, await Count(db, "internal_test_center_audit", center));
    }

    [Fact]
    public async Task SimultaneousMvccReadersAndWriterReturnOnlyFullyAttestedVersions()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = NewCenter("reader-writer");
        Assert.Equal(CenterOutcome.Applied, (await Apply(store, center,
            Cmd(center, CenterCommandKind.DefineDepartment, 0, "test:initial"))).Outcome);

        async Task WriteSeries()
        {
            for (var version = 1; version <= 12; version++)
            {
                var change = await Apply(store, center, Cmd(center,
                    CenterCommandKind.DefineDepartment, version, "test:department-" + version));
                Assert.Equal(CenterOutcome.Applied, change.Outcome);
                await Task.Yield();
            }
        }

        async Task InspectSeries()
        {
            for (var sample = 0; sample < 15; sample++)
            {
                var snapshot = await Read(store, center);
                Assert.Equal(snapshot.Version, snapshot.Events.Length);
                Assert.Equal(snapshot.Version, snapshot.Departments.Count);
                for (var i = 0; i < snapshot.Events.Length; i++)
                    Assert.Equal(i + 1L, snapshot.Events[i].Version);
                await Task.Yield();
            }
        }

        await Task.WhenAll(WriteSeries(), InspectSeries(), InspectSeries());
        var final = await Read(store, center);
        Assert.Equal(13L, final.Version);
        Assert.Equal(13, final.Departments.Count);
        Assert.Equal(13L, await Count(db, "internal_test_center_event", center));
        Assert.Equal(13L, await Count(db, "internal_test_center_audit", center));
    }
}
