using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

// All sources and identities here are disposable TEST DOUBLES, not licensed
// reviewers or authoritative issuer integrations.
public sealed class ReviewerProvenancePostgresTests
{
    private const string Doctor = "test:doctor-a";
    private const string Submitter = "test:submitter-a";
    private const string Reviewer = "test:reviewer-a";
    private const string Proof = "test:proof-a";
    private static string Pg => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG required");
    private static string NewCenter() => "test:center-" + Guid.NewGuid().ToString("N");

    private sealed class SyntheticSource(ReviewerProvenanceEvidence? value)
        : IReviewerProvenanceSource
    {
        internal bool Throw { get; set; }
        internal int Calls { get; private set; }
        public Task<ReviewerProvenanceEvidence?> ResolveFromServerAsync(
            Guid noteRequestId, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("synthetic-secret");
            return Task.FromResult(value);
        }
    }

    private static ReviewerProvenanceEvidence Positive(string center,
        string reviewer = Reviewer, string proof = Proof) => new(
        reviewer, reviewer, center, "test:grant-v1", "test:grant-v1", proof,
        ReviewerProvenanceCheck.ConfirmedByTestDouble,
        ReviewerProvenanceCheck.ConfirmedByTestDouble,
        ReviewerProvenanceCheck.ConfirmedByTestDouble,
        ReviewerProvenanceCheck.ConfirmedByTestDouble,
        ReviewerProvenanceCheck.ConfirmedByTestDouble,
        ReviewerProvenanceCheck.ConfirmedByTestDouble);

    private static async Task<(NpgsqlDataSource Owner, NpgsqlDataSource Capture,
        PgClinicalEvidenceJournal Evidence, PgHumanReviewNoteJournal Notes,
        ReviewerProvenanceRequest Request)> SeedAsync(
        string reviewer = Reviewer, string submitter = Submitter,
        string subject = Doctor)
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var owner = NpgsqlDataSource.Create(Pg);
        var settings = new NpgsqlConnectionStringBuilder(Pg) {
            Username = "taranom_sprint12_snapshot_capture",
            Password = "ephemeral_synthetic_snapshot_capture"
        };
        var capture = NpgsqlDataSource.Create(settings.ConnectionString);
        var journal = new PgClinicalEvidenceJournal(owner);
        var frozen = new PgFrozenUnverifiedReviewQueue(capture);
        var notes = new PgHumanReviewNoteJournal(owner, frozen);
        var center = NewCenter();
        var claim = new EvidenceSubmission(Guid.NewGuid(), 0, center, subject,
            EvidenceSubjectKind.Physician, EvidenceKind.PhysicianProfessionalQualificationClaim,
            submitter, Proof);
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated,
            (await journal.SubmitAsync(center, subject, EvidenceSubjectKind.Physician,
                claim, TestContext.Current.CancellationToken)).Outcome);
        var snapshot = await frozen.CaptureAsync(center, null, TestContext.Current.CancellationToken);
        var note = new HumanReviewNoteCommand(Guid.NewGuid(), 0, center, subject,
            EvidenceSubjectKind.Physician, 1, snapshot, 0, null,
            reviewer, "test:rationale-only");
        Assert.Equal(HumanReviewNoteOutcome.PendingSourceVerificationRecorded,
            (await notes.RecordAsync(note, TestContext.Current.CancellationToken)).Outcome);
        return (owner, capture, journal, notes, new ReviewerProvenanceRequest(
            center, subject, EvidenceSubjectKind.Physician, 1, note.RequestId));
    }

    private static Task<ReviewerProvenanceOutcome> Assess(
        PgHumanReviewNoteJournal notes, PgClinicalEvidenceJournal evidence,
        ReviewerProvenanceRequest request, IReviewerProvenanceSource? source) =>
        new PgReviewerProvenanceInterlock(notes, evidence, source)
            .InspectAsync(request, TestContext.Current.CancellationToken);

    [Fact]
    public async Task MissingSourceAlwaysDeniesEvenValidStoredNote()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.AuthoritativeSourceUnavailable,
            await Assess(notes, evidence, request, null));
    }

    [Fact]
    public async Task SourceReturningNoEvidenceFailsClosed()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var stub = new SyntheticSource(null);
        Assert.Equal(ReviewerProvenanceOutcome.AuthoritativeSourceUnavailable,
            await Assess(notes, evidence, request, stub));
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task SourceFailureNeverLeaksProviderInternals()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var stub = new SyntheticSource(Positive(request.CenterRef)) { Throw = true };
        Assert.Equal(ReviewerProvenanceOutcome.AuthoritativeSourceUnavailable,
            await Assess(notes, evidence, request, stub));
    }

    [Fact]
    public async Task FullySyntheticPositiveEvidenceIsNotAuthorizationOrApproval()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.PreconditionsPresentOnly,
            await Assess(notes, evidence, request,
                new SyntheticSource(Positive(request.CenterRef))));
        Assert.DoesNotContain(Enum.GetNames<ReviewerProvenanceOutcome>(),
            n => n is "Approved" or "Authorized" or "Verified" or "Licensed");
    }

    [Fact]
    public async Task ReviewingOwnSubmittedClaimDoesNotPassIndependence()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync(
            reviewer: Submitter);
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.ReviewerNotIndependent,
            await Assess(notes, evidence, request,
                new SyntheticSource(Positive(request.CenterRef, Submitter))));
    }

    [Fact]
    public async Task ClinicianSubjectCannotAttestTheirOwnQualification()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync(
            reviewer: Doctor);
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.ReviewerNotIndependent,
            await Assess(notes, evidence, request,
                new SyntheticSource(Positive(request.CenterRef, Doctor))));
    }

    [Fact]
    public async Task DifferentReviewerInAttestationCannotClaimStoredNote()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.EvidenceMismatch,
            await Assess(notes, evidence, request,
                new SyntheticSource(Positive(request.CenterRef, "test:someone-else"))));
    }

    [Fact]
    public async Task ClaimedSourceMustMatchExactAcceptedEvidencePointer()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.EvidenceMismatch,
            await Assess(notes, evidence, request,
                new SyntheticSource(Positive(request.CenterRef, Reviewer, "test:other-proof"))));
    }

    [Fact]
    public async Task CrossCenterEvidenceCannotBeReused()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.EvidenceMismatch,
            await Assess(notes, evidence, request,
                new SyntheticSource(Positive(NewCenter()))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task InvalidSessionEvidenceFailsClosed(int state)
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var attested = Positive(request.CenterRef) with {
            AuthenticatedSession = (ReviewerProvenanceCheck)state
        };
        Assert.Equal(ReviewerProvenanceOutcome.ReviewerIdentityUnverified,
            await Assess(notes, evidence, request, new SyntheticSource(attested)));
    }

    [Fact]
    public async Task SubjectToReviewerMappingMustBeCurrent()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var attested = Positive(request.CenterRef) with {
            CurrentIdentityMapping = ReviewerProvenanceCheck.Missing
        };
        Assert.Equal(ReviewerProvenanceOutcome.ReviewerIdentityUnverified,
            await Assess(notes, evidence, request, new SyntheticSource(attested)));
    }

    [Fact]
    public async Task ServerSessionCannotImpersonateAnotherReviewer()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var attested = Positive(request.CenterRef) with {
            SessionSubjectRef = "test:another-session-user"
        };
        Assert.Equal(ReviewerProvenanceOutcome.ReviewerIdentityUnverified,
            await Assess(notes, evidence, request, new SyntheticSource(attested)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task MissingOrRevokedReviewerDelegationBlocksReviewProvenance(int state)
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var attested = Positive(request.CenterRef) with {
            CurrentReviewDelegation = (ReviewerProvenanceCheck)state
        };
        Assert.Equal(ReviewerProvenanceOutcome.GrantStaleOrRevoked,
            await Assess(notes, evidence, request, new SyntheticSource(attested)));
    }

    [Fact]
    public async Task StalePermissionVersionIsNeverImplicitlyValid()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var attested = Positive(request.CenterRef) with {
            PreviousGrantVersionRef = "test:old-grant"
        };
        Assert.Equal(ReviewerProvenanceOutcome.GrantStaleOrRevoked,
            await Assess(notes, evidence, request, new SyntheticSource(attested)));
    }

    [Fact]
    public async Task MissingAuditMustBlockAllPrerequisiteClaims()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var attested = Positive(request.CenterRef) with {
            AuditAvailable = ReviewerProvenanceCheck.Missing
        };
        Assert.Equal(ReviewerProvenanceOutcome.GrantStaleOrRevoked,
            await Assess(notes, evidence, request, new SyntheticSource(attested)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task UnverifiedIndependentSourceBlocksInspection(int state)
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var attested = Positive(request.CenterRef) with {
            IndependentOriginAttested = (ReviewerProvenanceCheck)state
        };
        Assert.Equal(ReviewerProvenanceOutcome.EvidenceOriginUnverified,
            await Assess(notes, evidence, request, new SyntheticSource(attested)));
    }

    [Fact]
    public async Task MissingStoredNoteDoesNotBecomeAnAttestedReview()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var missing = request with { ReviewNoteRequestId = Guid.NewGuid() };
        Assert.Equal(ReviewerProvenanceOutcome.ReviewNoteMissingOrCorrupt,
            await Assess(notes, evidence, missing,
                new SyntheticSource(Positive(request.CenterRef))));
    }

    [Fact]
    public async Task WrongEvidenceRevisionDoesNotMatchStoredNote()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.ReviewNoteMissingOrCorrupt,
            await Assess(notes, evidence, request with { EvidenceRevision = 2 },
                new SyntheticSource(Positive(request.CenterRef))));
    }

    [Fact]
    public async Task TamperedAcceptedHeadPreventsClaimingReviewProvenance()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        await using (var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var cmd = new NpgsqlCommand("""
            UPDATE internal_test_review_case SET current_revision=0
            WHERE center_ref=@center
            """, cn))
        {
            cmd.Parameters.AddWithValue("center", request.CenterRef);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        Assert.Equal(ReviewerProvenanceOutcome.ReviewNoteMissingOrCorrupt,
            await Assess(notes, evidence, request,
                new SyntheticSource(Positive(request.CenterRef))));
    }

    [Theory]
    [InlineData("real:center","test:doctor-a")]
    [InlineData("test:center-a","national-id:doctor")]
    [InlineData("test:","test:doctor-a")]
    public async Task MalformedOrRealScopeRejectedWithoutReadingClaims(
        string center, string subject)
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        var source = new SyntheticSource(Positive(request.CenterRef));
        Assert.Equal(ReviewerProvenanceOutcome.InvalidScope,
            await Assess(notes, evidence, request with {
                CenterRef = center, SubjectRef = subject
            }, source));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task MissingRequestIdAndUnknownSubjectKindAreNotVerified()
    {
        var (owner, capture, evidence, notes, request) = await SeedAsync();
        await using var _ = owner;
        await using var __ = capture;
        Assert.Equal(ReviewerProvenanceOutcome.InvalidScope,
            await Assess(notes, evidence, request with {
                ReviewNoteRequestId = Guid.Empty
            }, new SyntheticSource(Positive(request.CenterRef))));
        Assert.Equal(ReviewerProvenanceOutcome.InvalidScope,
            await Assess(notes, evidence, request with {
                SubjectKind = (EvidenceSubjectKind)99
            }, new SyntheticSource(Positive(request.CenterRef))));
    }
}
