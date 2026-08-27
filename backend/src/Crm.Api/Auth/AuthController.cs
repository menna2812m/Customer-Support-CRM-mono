using Asp.Versioning;
using Crm.Api.Common.Correlation;
using Crm.Api.Common.Errors;
using Crm.Api.Configuration;
using Crm.Application.Abstractions;
using Crm.Application.Common;
using Crm.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Crm.Api.Auth;

/// <summary>
/// Sign-in, session, and sign-out (spec US1, US2).
///
/// The OIDC handshake runs server-side: the browser is redirected to the provider and back, and the
/// application never receives a provider token. What it does receive - through a normal API call
/// authenticated by the renewal cookie - is a short-lived credential the CRM issued itself.
///
/// Three endpoints here are anonymous by necessity (spec AR-001). Nothing else in this feature is.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(
    IIdentityProviderClient provider,
    IIdentityStore identityStore,
    ISessionStore sessions,
    ITokenIssuer tokenIssuer,
    IAuthenticationEventLog events,
    StaffSignIn signIn,
    AuthCookies cookies,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IOptions<IdentityOptions> identityOptions,
    IOptions<AuthOptions> authOptions,
    IOptions<CorsOptions> corsOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Begins sign-in by redirecting the browser to the identity provider.</summary>
    [HttpGet("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting(CrmRateLimitPolicies.SignIn)]
    public async Task<IActionResult> SignIn(
        [FromQuery] string? returnUrl,
        [FromQuery] string? lang,
        CancellationToken cancellationToken)
    {
        if (!authOptions.Value.Staff.Enabled)
        {
            return ProviderUnavailable("Sign-in is not configured on this environment.");
        }

        // An unvalidated return path is an open redirect: an attacker sends a victim to our sign-in
        // and lands them on a look-alike site carrying the trust of our domain.
        var safeReturnUrl = SanitizeReturnUrl(returnUrl);

        try
        {
            var request = await provider.CreateAuthorizationRequestAsync(CallbackUri(), lang, cancellationToken);

            cookies.WriteFlow(Response, new SignInFlowState(
                request.CodeVerifier,
                request.Nonce,
                safeReturnUrl,
                lang));

            return Redirect(request.AuthorizationUri.ToString());
        }
        catch (IdentityProviderException exception)
        {
            logger.LogError(exception, "Sign-in could not start because the identity provider is unavailable.");

            return ProviderUnavailable("The sign-in service is temporarily unavailable.");
        }
    }

    /// <summary>
    /// A browser arrives here by navigating, not by calling an API, so it is sent to the screen that
    /// explains the outage in its own language. Anything else - a client that did not ask for HTML -
    /// gets the error contract every other endpoint uses.
    /// </summary>
    private IActionResult ProviderUnavailable(string title)
    {
        if (Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToApp(flow: null, ErrorCodes.ProviderUnavailable);
        }

        return Problem(StatusCodes.Status503ServiceUnavailable, ErrorCodes.ProviderUnavailable, title);
    }

    /// <summary>
    /// The provider's redirect target. Completes the exchange and redirects back to the
    /// application, carrying no credential in the URL.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    [EnableRateLimiting(CrmRateLimitPolicies.SignIn)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        var flow = cookies.ReadFlow(Request);
        cookies.ClearFlow(Response);

        if (flow is null)
        {
            return RedirectToApp(null, ErrorCodes.SignInFailed);
        }

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            // The provider's own error text is not shown to the user; it is logged and translated
            // into one code the client understands (spec FR-009).
            logger.LogWarning("The identity provider returned an error during sign-in.");

            return RedirectToApp(flow, ErrorCodes.SignInFailed);
        }

        ProviderIdentity identity;

        try
        {
            identity = await provider.ExchangeCodeAsync(
                code,
                flow.CodeVerifier,
                flow.Nonce,
                CallbackUri(),
                cancellationToken);
        }
        catch (IdentityProviderException exception)
        {
            logger.LogError(exception, "The code exchange with the identity provider failed.");

            return RedirectToApp(flow, ErrorCodes.ProviderUnavailable);
        }

        var settings = new IdentitySettings(
            identityOptions.Value.BootstrapAdministrator,
            identityOptions.Value.DefaultRole);

        var outcome = await signIn.ExecuteAsync(identity, settings, cancellationToken);

        if (!outcome.Succeeded)
        {
            return RedirectToApp(flow, outcome.Refusal switch
            {
                SignInRefusal.IdentityCollision => ErrorCodes.IdentityCollision,
                SignInRefusal.Inactive => ErrorCodes.NoAccess,
                _ => ErrorCodes.NoAccess,
            });
        }

        var session = await sessions.StartAsync(
            outcome.User!.Id,
            DescribeClient(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        cookies.WriteRenewal(Response, session.RenewalCredential, session.RenewalExpiresAt);

        return RedirectToApp(flow, code: null);
    }

    /// <summary>
    /// Exchanges the renewal cookie for an access credential. Used immediately after sign-in and on
    /// every renewal, so there is one path rather than two.
    /// </summary>
    [HttpPost("session")]
    [AllowAnonymous]
    [EnableRateLimiting(CrmRateLimitPolicies.Session)]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        if (RejectCrossSite() is { } refused)
        {
            return refused;
        }

        var renewal = cookies.ReadRenewal(Request);

        if (string.IsNullOrWhiteSpace(renewal))
        {
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.SessionExpired, "No session was presented.");
        }

        var result = await sessions.RenewAsync(renewal, cancellationToken);

        if (!result.Succeeded)
        {
            cookies.ClearRenewal(Response);

            if (result.FailureReason == "reused" && result is { UserId: { } reusedUser, SessionId: { } reusedSession })
            {
                // Reuse means somebody holds a copy. The session is already revoked by the store;
                // recording it is what makes the incident visible afterwards.
                await events.RecordSessionRevokedAsync(reusedUser, reusedSession, "credential_reused", cancellationToken);
            }

            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.SessionExpired, "The session has ended.");
        }

        var user = await identityStore.FindByIdAsync(result.UserId!.Value, cancellationToken);

        if (user is null || !user.IsActive)
        {
            await sessions.RevokeAsync(result.SessionId!.Value, "user_deactivated", cancellationToken);
            cookies.ClearRenewal(Response);

            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.SessionExpired, "The session has ended.");
        }

        // Permissions are recomputed here, which is what bounds a role change to one renewal cycle.
        var permissions = await identityStore.GetEffectivePermissionsAsync(user.Id, cancellationToken);

        cookies.WriteRenewal(Response, result.Renewed!.RenewalCredential, result.Renewed.RenewalExpiresAt);

        await events.RecordSessionRenewedAsync(user.Id, result.SessionId!.Value, cancellationToken);

        var credential = tokenIssuer.Issue(new IssuedIdentity(
            user.Id,
            result.SessionId!.Value,
            user.DisplayName,
            user.Email,
            CallerPopulation.Staff,
            permissions,
            user.Scope));

        return Ok(new SessionResponse(
            credential.Value,
            credential.ExpiresInSeconds,
            new CurrentUserResponse(
                user.Id,
                user.DisplayName,
                user.Email,
                CallerPopulation.Staff.ToString(),
                [.. permissions.Order(StringComparer.Ordinal)],
                user.Scope is null
                    ? null
                    : new OrganizationScopeResponse(user.Scope.DepartmentId, user.Scope.BranchId, user.Scope.TeamId))));
    }

    /// <summary>
    /// Who the caller is, as this request's credential asserts it (spec FR-022).
    ///
    /// Answers only about the caller. There is no identifier to pass and no way to ask about
    /// anybody else, so this endpoint cannot become a directory of the workforce: everything it
    /// returns is read from the validated credential the caller already holds.
    ///
    /// Useful when a screen needs the current permissions without waiting for the next renewal -
    /// and as the one place a client can confirm that its credential is still accepted.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        if (currentUser is not { IsAuthenticated: true, UserId: { } userId })
        {
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, "No session was presented.");
        }

        return Ok(new CurrentUserResponse(
            userId,
            User.FindFirst("name")?.Value ?? string.Empty,
            User.FindFirst("email")?.Value ?? string.Empty,
            (currentUser.Population ?? CallerPopulation.Staff).ToString(),
            [.. currentUser.Permissions.Order(StringComparer.Ordinal)],
            currentUser.Scope is null
                ? null
                : new OrganizationScopeResponse(
                    currentUser.Scope.DepartmentId,
                    currentUser.Scope.BranchId,
                    currentUser.Scope.TeamId)));
    }

    /// <summary>Ends the CRM session, and optionally offers the provider's own sign-out.</summary>
    [HttpPost("sign-out")]
    [Authorize]
    public async Task<IActionResult> SignOutSession(
        [FromBody] SignOutRequest? request,
        CancellationToken cancellationToken)
    {
        if (RejectCrossSite() is { } refused)
        {
            return refused;
        }

        var userId = ReadUserId();
        var sessionId = ReadSessionId();

        if (userId is null || sessionId is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, "No session was presented.");
        }

        // Acts only on the caller's own sessions - the identifiers come from the validated
        // credential, never from the request body (spec AR-002).
        if (request?.AllSessions == true)
        {
            await sessions.RevokeAllForUserAsync(userId.Value, "signed_out_everywhere", cancellationToken);
            await events.RecordSessionRevokedAsync(userId.Value, sessionId.Value, "signed_out_everywhere", cancellationToken);
        }
        else
        {
            await sessions.RevokeAsync(sessionId.Value, "signed_out", cancellationToken);
            await events.RecordSessionRevokedAsync(userId.Value, sessionId.Value, "signed_out", cancellationToken);
        }

        cookies.ClearRenewal(Response);

        Uri? providerSignOut = null;

        if (request?.EndProviderSession == true)
        {
            // Returned rather than redirected to, so the application can finish its own cleanup
            // first. Declining leaves other corporate applications in the browser untouched.
            providerSignOut = await provider.GetEndSessionUriAsync(new Uri(AppBaseUri(), "/"), cancellationToken);
        }

        return Ok(new SignOutResponse(true, providerSignOut?.ToString()));
    }

    /// <summary>
    /// Rejects anything that is not a relative path inside this application. A protocol-relative
    /// value such as <c>//evil.example</c> is a URL, not a path, which is why the check is explicit
    /// rather than a simple "starts with a slash".
    /// </summary>
    internal static string SanitizeReturnUrl(string? returnUrl)
    {
        const string fallback = "/";

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return fallback;
        }

        var candidate = returnUrl.Trim();

        if (!candidate.StartsWith('/')
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith("/\\", StringComparison.Ordinal)
            || candidate.Contains("://", StringComparison.Ordinal))
        {
            return fallback;
        }

        return candidate;
    }

    /// <summary>
    /// The CSRF defence for the two endpoints the renewal cookie authenticates (spec FR-017).
    ///
    /// Two independent checks, because either alone has a gap. A cross-site form post cannot set a
    /// custom header, which is what the header check relies on - but a header can be set by a
    /// cross-origin XHR that CORS would then block only at the response, by which time the request
    /// has been processed. Checking <c>Origin</c> against the same allowlist CORS uses refuses that
    /// request before it does anything.
    ///
    /// Returns null when the request is acceptable.
    /// </summary>
    private ObjectResult? RejectCrossSite()
    {
        if (!HasRequiredHeader())
        {
            return Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "This endpoint requires the application request header.");
        }

        if (!HasAcceptableOrigin())
        {
            logger.LogWarning("A cookie-authenticated request was refused because its origin is not allow-listed.");

            return Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "This endpoint does not accept requests from that origin.");
        }

        return null;
    }

    private bool HasRequiredHeader() =>
        Request.Headers.TryGetValue("X-Requested-With", out var value)
        && string.Equals(value.ToString(), "CrmWeb", StringComparison.Ordinal);

    /// <summary>
    /// Accepts the application's own origin and the CORS allowlist. A request with no
    /// <c>Origin</c> header is accepted: browsers send one on every cross-site request, so its
    /// absence means a caller that is not a browser - and such a caller has no ambient cookie to
    /// be abused in the first place.
    /// </summary>
    private bool HasAcceptableOrigin()
    {
        var origin = Request.Headers.Origin.ToString();

        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (string.Equals(origin, BaseUri().ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return corsOptions.Value.AllowedOrigins.Any(allowed =>
            string.Equals(allowed.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase));
    }

    private Guid? ReadUserId() =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : null;

    private Guid? ReadSessionId() =>
        Guid.TryParse(User.FindFirst(Common.Security.CrmClaims.SessionId)?.Value, out var id) ? id : null;

    private string CallbackUri() => new Uri(BaseUri(), "/api/v1/auth/callback").ToString();

    private Uri BaseUri() => new($"{Request.Scheme}://{Request.Host}");

    /// <summary>
    /// Where the browser is sent after the handshake. The application is a separate origin in
    /// development, so this comes from configuration rather than from the request.
    /// </summary>
    private Uri AppBaseUri()
    {
        var configured = authOptions.Value.Staff.ApplicationBaseUrl;

        return string.IsNullOrWhiteSpace(configured) ? BaseUri() : new Uri(configured);
    }

    private RedirectResult RedirectToApp(SignInFlowState? flow, string? code)
    {
        var destination = new UriBuilder(new Uri(AppBaseUri(), "/auth/complete"));
        var query = new List<string>();

        if (flow is not null)
        {
            query.Add($"returnUrl={Uri.EscapeDataString(flow.ReturnUrl)}");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            query.Add($"error={Uri.EscapeDataString(code)}");

            // The refusal screen shows this so a person can quote one identifier to support, who can
            // then find the matching server-side entry. It identifies a request, not a person.
            query.Add($"correlationId={Uri.EscapeDataString(correlation.Id)}");
        }

        destination.Query = string.Join('&', query);

        return Redirect(destination.Uri.ToString());
    }

    /// <summary>A coarse description so a person recognises their own session. Not a fingerprint.</summary>
    private string? DescribeClient()
    {
        var agent = Request.Headers.UserAgent.ToString();

        return string.IsNullOrWhiteSpace(agent)
            ? null
            : agent[..Math.Min(agent.Length, 200)];
    }

    private ObjectResult Problem(int status, string code, string title) =>
        StatusCode(status, new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = ProblemTypes.ForCode(code),
            Instance = Request.Path.Value,
            Extensions =
            {
                ["code"] = code,
                ["correlationId"] = correlation.Id,
            },
        });
}

/// <param name="AccessToken">Short-lived credential. Held in memory by the client, never stored.</param>
public sealed record SessionResponse(string AccessToken, int ExpiresInSeconds, CurrentUserResponse User);

public sealed record CurrentUserResponse(
    Guid Id,
    string DisplayName,
    string Email,
    string Population,
    IReadOnlyList<string> Permissions,
    OrganizationScopeResponse? Scope);

public sealed record OrganizationScopeResponse(Guid? DepartmentId, Guid? BranchId, Guid? TeamId);

/// <param name="AllSessions">End every session for this user, not only the current one.</param>
/// <param name="EndProviderSession">Also end the session at the identity provider.</param>
public sealed record SignOutRequest(bool AllSessions, bool EndProviderSession);

public sealed record SignOutResponse(bool SignedOut, string? ProviderSignOutUrl);
