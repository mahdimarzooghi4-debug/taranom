using System.Text.Json;

// TRN-TECH-090: strictly hypothetical payer structure, no account/payment.
internal static class PayerScenarioHttp
{
    private const string Contract = "TRN-TECH-090";

    internal static async Task<IResult> Evaluate(HttpContext context)
    {
        var req = context.Request;
        if (req.Query.Count != 0 || req.Headers.Origin.Count > 1 ||
            (req.Headers.Origin.Count == 1 &&
             req.Headers.Origin.ToString() is not
             ("http://127.0.0.1:4318" or "http://localhost:4318")) ||
            req.ContentType is null ||
            !req.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            req.ContentLength is > 256)
            return Denied(400, "INVALID_HYPOTHETICAL_INPUT");

        string? market, payer;
        var buffer = new byte[257];
        int length = 0;
        try
        {
            while (length < buffer.Length)
            {
                int count = await req.Body.ReadAsync(buffer.AsMemory(length));
                if (count == 0) break;
                length += count;
            }
            if (length is < 2 or > 256) return Denied(400, "INVALID_HYPOTHETICAL_INPUT");
            using var doc = JsonDocument.Parse(buffer.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = 3 });
            var json = doc.RootElement;
            if (json.ValueKind != JsonValueKind.Object ||
                json.EnumerateObject().Count() != 2 ||
                json.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
                !json.TryGetProperty("marketId", out var marketValue) ||
                !json.TryGetProperty("payerMode", out var payerValue) ||
                marketValue.ValueKind != JsonValueKind.String ||
                payerValue.ValueKind != JsonValueKind.String)
                return Denied(400, "INVALID_HYPOTHETICAL_INPUT");

            market = marketValue.GetString();
            payer = payerValue.GetString();
        }
        catch (JsonException) { return Denied(400, "INVALID_HYPOTHETICAL_INPUT"); }
        catch (IOException) { return Denied(400, "INVALID_HYPOTHETICAL_INPUT"); }

        if (market is not ("MARKET-PUBLIC" or "MARKET-SUPPORT" or "MARKET-CORPORATE") ||
            payer is not ("MEMBER" or "SPONSOR") ||
            (market == "MARKET-PUBLIC" && payer == "SPONSOR"))
            return Denied(400, "UNSUPPORTED_POLICY_SCENARIO");

        var blockers = ReleaseDenialSource.Read(Environment.GetEnvironmentVariable("TARANOM_OPS_REPO_ROOT"));
        if (blockers is null) return Denied(503, "DENIAL_SOURCE_UNVERIFIABLE");
        var item = blockers.SingleOrDefault(b => b.Id == market);
        if (item is null || item.Status != "BLOCKED_UNVERIFIED")
            return Denied(503, "DENIAL_SOURCE_UNVERIFIABLE");

        return Results.Json(new
        {
            contract = Contract,
            outcome = "HYPOTHETICAL_POLICY_ONLY",
            hypotheticalOnly = true,
            marketId = market,
            payerMode = payer,
            structurallyDescribedByBusinessPolicy = true,
            sponsorContractRequired = payer == "SPONSOR",
            sponsorContractVerified = false,
            payerVerified = false,
            individualOwnsMembership = true,
            individualOwnsHealthcareWallet = true,
            sponsorClinicalAccessAuthorized = false,
            sponsorWalletControlAuthorized = false,
            splitPaymentTermsDefined = false,
            membershipActivated = false,
            paymentEnabled = false,
            onboardingEnabled = false,
            nationalReleaseAllowed = false,
            missingEvidence = item.MissingEvidence
        });
    }

    private static IResult Denied(int status, string outcome) => Results.Json(new
    {
        contract = Contract,
        outcome,
        hypotheticalOnly = true,
        structurallyDescribedByBusinessPolicy = false,
        sponsorContractVerified = false,
        payerVerified = false,
        individualOwnsMembership = true,
        individualOwnsHealthcareWallet = true,
        sponsorClinicalAccessAuthorized = false,
        sponsorWalletControlAuthorized = false,
        splitPaymentTermsDefined = false,
        membershipActivated = false,
        paymentEnabled = false,
        onboardingEnabled = false,
        nationalReleaseAllowed = false
    }, statusCode: status);
}
