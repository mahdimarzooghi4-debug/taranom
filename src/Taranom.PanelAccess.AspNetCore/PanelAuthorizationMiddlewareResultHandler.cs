using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Taranom.PanelAccess.AspNetCore;

// API-safe response contract. It deliberately omits policy names, claims,
// scope references, and evidence-source failure details.
internal sealed class PanelAuthorizationMiddlewareResultHandler
    : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        var challenged = authorizeResult.Challenged;
        var status = challenged
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var response = new
        {
            type = "about:blank",
            title = challenged ? "Authentication required" : "Access denied",
            status,
            code = challenged
                ? "PANEL_AUTHENTICATION_REQUIRED"
                : "PANEL_ACCESS_DENIED"
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
