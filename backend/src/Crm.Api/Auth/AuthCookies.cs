using System.Text.Json;
using Crm.Api.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Crm.Api.Auth;

/// <summary>
/// The two cookies this feature uses, and the rules that make them safe (spec FR-016, FR-017).
///
/// Both are HttpOnly: a page script can read neither. The renewal cookie is path-scoped so it is
/// sent only where it is needed, and SameSite=Lax so a cross-site request cannot carry it. The
/// flow cookie holds the PKCE verifier for the seconds between leaving for the provider and coming
/// back, and is encrypted because it is state we must trust when it returns.
/// </summary>
public sealed class AuthCookies(
    IOptions<CrmSessionOptions> sessionOptions,
    IDataProtectionProvider dataProtection,
    IWebHostEnvironment environment)
{
    /// <summary>Path the renewal cookie is limited to - the two endpoints that need it.</summary>
    public const string RenewalPath = "/api/v1/auth";

    private const string FlowCookieName = "crm_signin_flow";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Crm.Auth.SignInFlow");

    public string RenewalCookieName => sessionOptions.Value.CookieName;

    public void WriteFlow(HttpResponse response, SignInFlowState state)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(state);

        var payload = _protector.Protect(JsonSerializer.Serialize(state));

        response.Cookies.Append(FlowCookieName, payload, new CookieOptions
        {
            HttpOnly = true,
            Secure = RequireSecure,
            SameSite = SameSiteMode.Lax,
            Path = RenewalPath,

            // The window between leaving for the provider and returning. Long enough for a person
            // to type a password and answer a second factor, short enough to be uninteresting.
            MaxAge = TimeSpan.FromMinutes(10),
        });
    }

    public SignInFlowState? ReadFlow(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Cookies.TryGetValue(FlowCookieName, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SignInFlowState>(_protector.Unprotect(payload));
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            // Tampered, expired key ring, or simply stale. Either way it is not flow state we can
            // trust, and the caller treats it as a failed handshake rather than guessing.
            return null;
        }
    }

    public void ClearFlow(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(FlowCookieName, new CookieOptions { Path = RenewalPath });
    }

    public void WriteRenewal(HttpResponse response, string credential, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(RenewalCookieName, credential, new CookieOptions
        {
            HttpOnly = true,
            Secure = RequireSecure,
            SameSite = SameSiteMode.Lax,
            Path = RenewalPath,
            Expires = expiresAt,
        });
    }

    public string? ReadRenewal(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies.TryGetValue(RenewalCookieName, out var value) ? value : null;
    }

    public void ClearRenewal(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(RenewalCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = RequireSecure,
            SameSite = SameSiteMode.Lax,
            Path = RenewalPath,
        });
    }

    /// <summary>
    /// Secure everywhere except local development, where the frontend and API run over plain HTTP
    /// and a Secure cookie would simply never be sent.
    /// </summary>
    private bool RequireSecure => !environment.IsDevelopment();
}

/// <summary>
/// What must survive the round trip to the provider. Held server-side in an encrypted cookie rather
/// than passed through the browser in the clear.
/// </summary>
public sealed record SignInFlowState(string CodeVerifier, string Nonce, string ReturnUrl, string? Language);
