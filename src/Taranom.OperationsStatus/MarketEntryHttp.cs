// TRN-TECH-081: only an approved business audience directory, not enrollment.
internal static class MarketEntryHttp
{
    internal static IResult Read(HttpContext context)
    {
        // An anonymous technical GET cannot bind any guessed subject, role,
        // sponsor, clinical permission, or query-driven product entitlement.
        if (context.Request.QueryString.HasValue ||
            context.Request.ContentLength is > 0)
            return Results.Json(new
            {
                contract = "TRN-TECH-081",
                outcome = "UNTRUSTED_AUDIENCE_QUERY",
                releaseAllowed = false, nationalReleaseAllowed = false,
                onboardingEnabled = false, markets = Array.Empty<object>()
            }, statusCode: StatusCodes.Status400BadRequest);

        // Reuse the exact source validation that proves 13 required Epics,
        // three markets and national coverage all remain BLOCKED_UNVERIFIED.
        var source = ReleaseDenialSource.Read(
            Environment.GetEnvironmentVariable("TARANOM_OPS_REPO_ROOT"));
        if (source is null)
            return Results.Json(new
            {
                contract = "TRN-TECH-081",
                outcome = "SOURCE_UNVERIFIABLE",
                releaseAllowed = false, nationalReleaseAllowed = false,
                onboardingEnabled = false, markets = Array.Empty<object>()
            }, statusCode: StatusCodes.Status503ServiceUnavailable);

        string[] ids = ["MARKET-PUBLIC", "MARKET-SUPPORT", "MARKET-CORPORATE"];
        var audience = new[]
        {
            new { id = ids[0], label = "افراد و خانواده‌ها",
                description = "مسیر برنامه‌ریزی عضویت مستقیم فردی و خانوادگی",
                thirdPartyPayerContractRequired = false },
            new { id = ids[1], label = "نهادهای حمایتی",
                description = "مسیر برنامه‌ریزی همکاری نهادهای حمایتی برای عضویت افراد تحت پوشش",
                thirdPartyPayerContractRequired = true },
            new { id = ids[2], label = "سازمان‌ها و شرکت‌ها",
                description = "مسیر برنامه‌ریزی عضویت گروهی کارکنان و، در صورت توافق، خانواده‌ها",
                thirdPartyPayerContractRequired = true }
        };
        var markets = audience.Select(m =>
        {
            var actual = source.Single(s => s.Id == m.id);
            return new
            {
                m.id, m.label, m.description,
                m.thirdPartyPayerContractRequired,
                status = actual.Status,
                missingEvidence = actual.MissingEvidence,
                ownershipAlwaysIndividual = true,
                sponsorClinicalAccessAuthorized = false,
                allowedActions = Array.Empty<string>()
            };
        }).ToArray();

        return Results.Json(new
        {
            contract = "TRN-TECH-081",
            source = "PUBLIC_17_DENIAL_INVENTORY",
            scope = "THREE_MARKETS_AND_NATIONAL_FIRST_RELEASE",
            marketCount = 3,
            blockerCount = source.Length,
            markets,
            onboardingEnabled = false,
            memberAuthenticated = false,
            sponsorClinicalAccessAuthorized = false,
            paymentsEnabled = false,
            providerIntegrationsVerified = false,
            officialMembershipActivated = false,
            releaseAllowed = false,
            nationalReleaseAllowed = false
        });
    }
}
