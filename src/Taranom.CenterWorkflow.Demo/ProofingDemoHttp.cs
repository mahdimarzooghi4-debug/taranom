using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;
using Taranom.ProofingJournal.Postgres;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-113: isolated, test-only exercise of an ALREADY versioned .NET
// source journal. The values are caller claims and NOT a legal identity
// correction, licensed KYC result, member authentication or activation.
internal static class ProofingDemoHttp
{
    internal static PgProofingCaseJournal Store { get; set; } = null!;

    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "requestId", "expectedCaseRevision", "expectedIdentityRevision",
        "action", "observation", "providerAndEvidenceValidated",
        "applicantLinkValidated", "evidenceCurrent",
        "officialActivationStatusKnown", "officiallyActivated",
        "identityDataChangedSinceEvidence"
    };

    internal static async Task<IResult> ReadAsync(HttpContext context)
    {
        var caseRef = context.Request.RouteValues["caseRef"]?.ToString();
        if (!TestRef(caseRef) || context.Request.Query.Count != 0)
            return Answer(400, "INVALID_TEST_INPUT", caseRef, null);
        try
        {
            var snapshot = await Store.ReadAsync(caseRef!);
            return Answer(200, "READ", caseRef, snapshot);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException
                                    or IOException or OverflowException)
        {
            return Answer(503, "TEST_JOURNAL_UNVERIFIABLE", caseRef, null);
        }
    }

    internal static async Task<IResult> WriteAsync(HttpContext context)
    {
        var req = context.Request;
        var caseRef = req.RouteValues["caseRef"]?.ToString();
        if (!TestRef(caseRef))
            return Answer(400, "INVALID_TEST_INPUT", caseRef, null);
        if (req.Headers.Origin.ToString() is not
            ("http://localhost:4320" or "http://127.0.0.1:4320"))
            return Answer(403, "ORIGIN_DENIED", caseRef, null);
        if (req.Query.Count != 0 || req.ContentType is null ||
            !req.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            req.ContentLength is > 1400)
            return Answer(400, "INVALID_TEST_INPUT", caseRef, null);
        try
        {
            var body = await SyntheticCommandIngress.ReadAsync(req,1400);
            if(body is null)
                return Answer(400, "INVALID_TEST_INPUT", caseRef, null);
            using var doc = JsonDocument.Parse(body.AsMemory(),
                new JsonDocumentOptions { MaxDepth = 3 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != Fields.Count ||
                root.EnumerateObject().Select(x=>x.Name)
                    .ToHashSet(StringComparer.Ordinal).Count != Fields.Count ||
                !root.EnumerateObject().All(x => Fields.Contains(x.Name)))
                return Answer(400, "INVALID_TEST_INPUT", caseRef, null);

            bool Text(string k, out string v)
            {
                var e = root.GetProperty(k);
                v = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
                return e.ValueKind == JsonValueKind.String && v.Length <= 50;
            }
            bool Num(string k, out long n)
            {
                n = 0;
                var e = root.GetProperty(k);
                return e.ValueKind == JsonValueKind.Number &&
                    e.TryGetInt64(out n) && n is >= 0 and < long.MaxValue;
            }
            bool Flag(string k, out bool v)
            {
                var e = root.GetProperty(k);
                v = e.ValueKind == JsonValueKind.True;
                return e.ValueKind is JsonValueKind.True or JsonValueKind.False;
            }
            if (!Text("requestId",out var request) ||
                !Text("action",out var actionText) ||
                !Text("observation",out var observationText) ||
                !Text("providerAndEvidenceValidated",out var providerText) ||
                !Text("applicantLinkValidated",out var linkText) ||
                !Text("evidenceCurrent",out var currentText) ||
                !Text("officialActivationStatusKnown",out var activeStatusText) ||
                !Num("expectedCaseRevision",out var caseVersion) ||
                !Num("expectedIdentityRevision",out var identityVersion) ||
                !Flag("officiallyActivated",out var isActivated) ||
                !Flag("identityDataChangedSinceEvidence",out var changed) ||
                !Guid.TryParseExact(request, "D",out var id) ||
                id.ToString("D")[14] != '4')
                return Answer(400, "INVALID_TEST_INPUT", caseRef, null);

            var action = actionText switch
            {
                "OBSERVE_PROOFING" => ProofingCaseAction.ObserveProofing,
                "APPLICANT_CORRECTED_IDENTITY" => ProofingCaseAction.ApplicantCorrectedIdentity,
                _ => (ProofingCaseAction)0
            };
            var observation = observationText switch
            {
                "MISSING" => ProofingObservation.Missing,
                "PROVIDER_UNAVAILABLE" => ProofingObservation.ProviderUnavailable,
                "INCONCLUSIVE" => ProofingObservation.Inconclusive,
                "VERIFIED_MATCH" => ProofingObservation.VerifiedMatch,
                "DEFINITIVE_MISMATCH" => ProofingObservation.DefinitiveMismatch,
                "NONE" => (ProofingObservation)(-1),
                _ => (ProofingObservation)(-2)
            };
            static EvidenceCheck? Check(string x) => x switch
            {
                "MISSING" => EvidenceCheck.Missing,
                "CONFIRMED" => EvidenceCheck.Confirmed,
                "REJECTED" => EvidenceCheck.Rejected,
                _ => null
            };
            var provider = Check(providerText);
            var link = Check(linkText);
            var evidenceCurrent = Check(currentText);
            var status = Check(activeStatusText);
            if (action == 0 || observation == (ProofingObservation)(-2) ||
                provider is null || link is null || evidenceCurrent is null ||
                status is null)
                return Answer(400, "INVALID_TEST_INPUT", caseRef, null);

            ProofingReviewInput proofing;
            EvidenceCheck correctionStatus;
            bool correctionActivated;
            if (action == ProofingCaseAction.ApplicantCorrectedIdentity)
            {
                if (observation != (ProofingObservation)(-1) ||
                    provider != EvidenceCheck.Missing || link != EvidenceCheck.Missing ||
                    evidenceCurrent != EvidenceCheck.Missing || changed)
                    return Answer(400,"INVALID_TEST_INPUT",caseRef,null);
                proofing = default;
                correctionStatus = status.Value;
                correctionActivated = isActivated;
            }
            else
            {
                if (observation == (ProofingObservation)(-1))
                    return Answer(400,"INVALID_TEST_INPUT",caseRef,null);
                proofing = new ProofingReviewInput(observation,
                    provider.Value, link.Value, evidenceCurrent.Value,
                    status.Value, isActivated, changed);
                correctionStatus = EvidenceCheck.Missing;
                correctionActivated = false;
            }

            var command = new ProofingCaseCommand(id,caseVersion,identityVersion,
                action,proofing,correctionStatus,correctionActivated);
            var result = await Store.ApplyAsync(caseRef!,command);
            var httpCode = result.Outcome switch
            {
                ProofingJournalOutcome.Applied or ProofingJournalOutcome.Replayed => 200,
                ProofingJournalOutcome.InvalidCommand => 400,
                _ => 409
            };
            return Answer(httpCode,result.Outcome.ToString(),caseRef,result.Snapshot);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return Answer(400, "INVALID_TEST_INPUT", caseRef, null);
        }
        catch (OperationCanceledException)
        {return Answer(503,"TEST_JOURNAL_UNVERIFIABLE",caseRef,null);}
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException
                                    or OverflowException)
        {
            return Answer(503, "TEST_JOURNAL_UNVERIFIABLE", caseRef, null);
        }
    }

    private static bool TestRef(string? v) => v is { Length: > 5 and <= 100 } &&
        v.StartsWith("test:", StringComparison.Ordinal) &&
        v.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is ':' or '-' or '_');

    private static IResult Answer(int status,string outcome,
        string? caseRef,ProofingCaseSnapshot? snap) =>
        Results.Json(new
        {
            contract="TRN-TECH-113",syntheticOnly=true,claimsNotVerified=true,
            caseRef, outcome,
            caseRevision=snap?.CaseRevision,identityRevision=snap?.IdentityRevision,
            review=snap?.Review.ToString(),
            events=snap?.History.Select(e=>new {
                caseRevision=e.CaseRevision, identityRevision=e.IdentityRevision,
                action=e.Command.Action.ToString(), review=e.ResultingReview.ToString()
            }).ToArray() ?? Array.Empty<object>(),
            realMemberAuthenticated=false,realIdentityVerified=false,
            nationalIdStored=false,realIdpContacted=false,
            officialMembershipActivated=false,waitingPeriodStarted=false,
            walletCredited=false,clinicalRecordAccessGranted=false,
            sponsorClinicalAccessGranted=false,nationalReleaseAllowed=false
        },statusCode:status);
}
