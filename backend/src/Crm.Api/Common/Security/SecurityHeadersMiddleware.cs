namespace Crm.Api.Common.Security;

/// <summary>
/// Applies the FR-053 baseline security headers to every response, centrally.
///
/// These live in the application rather than in IIS configuration so they survive a server
/// rebuild and are assertable in the integration suite (clarification 2026-08-26).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // The API returns data, never markup: the strictest possible policy is also the correct one.
    private const string ContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Frame-Options"] = "DENY";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // The server version is not information a caller needs.
            headers.Remove("Server");

            return Task.CompletedTask;
        });

        return next(context);
    }
}
