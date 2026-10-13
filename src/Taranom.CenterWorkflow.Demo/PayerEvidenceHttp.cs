using System.Text.Json;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-112 — REAL loopback HTTP invokes the EXISTING domain gate.
// The alleged evidence is entirely caller-authored synthetic input. There is
// no authorized sponsor, actual contract, identity, payment or wallet port.
internal static class PayerEvidenceHttp
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "market", "payerKind", "requestedCapability", "memberRef",
        "memberTenantRef", "individualWalletOwnerRef", "claimedPayerRef",
        "claimedPayerTenantRef", "claimedContractMemberRef",
        "claimedContractTenantRef", "currentContract", "payerAllowedByContract",
        "membershipFee", "independentPayment"
    };

    internal static async Task<IResult> CheckAsync(HttpContext context)
    {
        var req = context.Request;
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
                "CI_EPHEMERAL_ONLY")
            return Deny(404, "TEST_ONLY_UNAVAILABLE", null, null, null);
        if (req.Headers.Origin.ToString() is not
            ("http://127.0.0.1:4320" or "http://localhost:4320"))
            return Deny(403, "ORIGIN_DENIED", null, null, null);
        if (req.Query.Count > 0 || req.ContentType is null ||
            !req.ContentType.StartsWith("application/json",
                StringComparison.OrdinalIgnoreCase) ||
            req.ContentLength is > 2400)
            return Deny(400, "INVALID_TEST_INPUT", null, null, null);

        var data = new byte[2401];
        var length = 0;
        try
        {
            while (length < data.Length)
            {
                var count = await req.Body.ReadAsync(data.AsMemory(length));
                if (count == 0) break;
                length += count;
            }
            if (length is < 2 or > 2400)
                return Deny(400, "INVALID_TEST_INPUT", null, null, null);
            using var json = JsonDocument.Parse(data.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = 3 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != Fields.Count ||
                root.EnumerateObject().Select(x => x.Name).ToHashSet(
                    StringComparer.Ordinal).Count != Fields.Count ||
                !root.EnumerateObject().All(p => Fields.Contains(p.Name) &&
                    p.Value.ValueKind == JsonValueKind.String))
                return Deny(400, "INVALID_TEST_INPUT", null, null, null);

            string Value(string name) => root.GetProperty(name).GetString()!;
            var marketText = Value("market");
            var payerText = Value("payerKind");
            var capabilityText = Value("requestedCapability");
            var market = marketText switch
            {
                "DIRECT_PUBLIC" => SyntheticMembershipMarket.DirectPublic,
                "SUPPORT_INSTITUTION" => SyntheticMembershipMarket.SupportInstitution,
                "CORPORATE_EMPLOYER" => SyntheticMembershipMarket.CorporateEmployer,
                _ => (SyntheticMembershipMarket)0
            };
            var payer = payerText switch
            {
                "MEMBER" => SyntheticSubscriptionPayerKind.Member,
                "SUPPORT_INSTITUTION" => SyntheticSubscriptionPayerKind.SupportInstitution,
                "EMPLOYER" => SyntheticSubscriptionPayerKind.Employer,
                _ => (SyntheticSubscriptionPayerKind)0
            };
            var capability = capabilityText switch
            {
                "INSPECT_FUNDING_EVIDENCE" =>
                    SyntheticPayerRequestedCapability.InspectSubscriptionFundingEvidence,
                "ACTIVATE_MEMBER_ACCOUNT" =>
                    SyntheticPayerRequestedCapability.ActivateMemberAccount,
                "READ_HEALTH_RECORD" =>
                    SyntheticPayerRequestedCapability.ReadMemberHealthRecord,
                "CONTROL_TREATMENT_CREDIT" =>
                    SyntheticPayerRequestedCapability.ControlMemberTreatmentCredit,
                "CHANGE_CLINICAL_CONSENT" =>
                    SyntheticPayerRequestedCapability.ChangeMemberClinicalConsent,
                "READ_PRIVATE_WALLET" =>
                    SyntheticPayerRequestedCapability.ReadMemberPrivateWallet,
                "WITHDRAW_CASH" =>
                    SyntheticPayerRequestedCapability.WithdrawTreatmentCreditAsCash,
                _ => (SyntheticPayerRequestedCapability)0
            };
            static EvidenceCheck? Evidence(string value) => value switch
            {
                "MISSING" => EvidenceCheck.Missing,
                "REJECTED" => EvidenceCheck.Rejected,
                "CONFIRMED" => EvidenceCheck.Confirmed,
                _ => null
            };
            var contract = Evidence(Value("currentContract"));
            var allows = Evidence(Value("payerAllowedByContract"));
            var fee = Evidence(Value("membershipFee"));
            var payment = Evidence(Value("independentPayment"));
            if (market == 0 || payer == 0 || capability == 0 ||
                contract is null || allows is null || fee is null || payment is null)
                return Deny(400, "INVALID_TEST_INPUT", null, null, null);

            var input = new SyntheticSubscriptionPayerInput(
                market, payer, capability,
                Value("memberRef"), Value("memberTenantRef"),
                Value("individualWalletOwnerRef"), Value("claimedPayerRef"),
                Value("claimedPayerTenantRef"), Value("claimedContractMemberRef"),
                Value("claimedContractTenantRef"), contract.Value, allows.Value,
                fee.Value, payment.Value);
            var result = SyntheticSubscriptionPayerBoundary.Evaluate(input);
            var status = result switch
            {
                SyntheticPayerBoundaryResult.FundingEvidencePresentOnly => 200,
                SyntheticPayerBoundaryResult.EvidenceMissing or
                SyntheticPayerBoundaryResult.EvidenceRejected => 409,
                SyntheticPayerBoundaryResult.InvalidInput => 400,
                _ => 403
            };
            return Deny(status, result.ToString(), marketText, payerText,
                capabilityText);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return Deny(400, "INVALID_TEST_INPUT", null, null, null);
        }
    }

    private static IResult Deny(int status, string outcome,
        string? market, string? payerKind, string? requestedCapability) =>
        Results.Json(new
        {
            contract = "TRN-TECH-112",
            syntheticOnly = true,
            untrustedEvidenceClaimsOnly = true,
            outcome, market, payerKind, requestedCapability,
            individualOwnsMembership = true,
            individualOwnsHealthcareWallet = true,
            sponsorClinicalAccessAuthorized = false,
            sponsorWalletControlAuthorized = false,
            contractVerified = false,
            paymentVerified = false,
            membershipActivated = false,
            treatmentCreditAllocated = false,
            clinicalConsentGranted = false,
            realMemberAuthenticated = false,
            nationalReleaseAllowed = false
        }, statusCode: status);
}
