using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Crm.Infrastructure.Identity;

/// <summary>
/// The identity provider, behind one adapter (spec FR-001, Constitution XV).
///
/// Standard OIDC discovery only, so the provider is configuration rather than code. Authorization
/// code flow with PKCE, executed server-side: the browser never receives a provider token, and the
/// SPA needs no OIDC library at all.
/// </summary>
public sealed class OpenIdConnectClient(
    HttpClient httpClient,
    IOptions<ProviderSettings> settings,
    IConfigurationManager<OpenIdConnectConfiguration> configurationManager) : IIdentityProviderClient
{
    public async Task<AuthorizationRequest> CreateAuthorizationRequestAsync(
        string redirectUri,
        string? uiLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var options = settings.Value;
        var configuration = await GetConfigurationAsync(cancellationToken);

        var verifier = CreateRandomValue();
        var nonce = CreateRandomValue();
        var state = CreateRandomValue();

        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["client_id"] = options.ClientId,
            ["response_type"] = "code",
            ["scope"] = options.Scope,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = CreateChallenge(verifier),
            ["code_challenge_method"] = "S256",
        };

        // Carry the interface language so the provider's own page does not switch languages
        // mid-flow (spec LR-003). Providers that ignore it simply ignore it.
        if (!string.IsNullOrWhiteSpace(uiLanguage))
        {
            query["ui_locales"] = uiLanguage;
        }

        var uri = new Uri(QueryHelpers.AddQuery(configuration.AuthorizationEndpoint, query));

        return new AuthorizationRequest(uri, verifier, nonce);
    }

    public async Task<ProviderIdentity> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        string expectedNonce,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var options = settings.Value;
        var configuration = await GetConfigurationAsync(cancellationToken);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = options.ClientId ?? string.Empty,
            ["client_secret"] = options.ClientSecret ?? string.Empty,
            ["code_verifier"] = codeVerifier,
        });

        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsync(new Uri(configuration.TokenEndpoint), content, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new IdentityProviderException("The identity provider could not be reached.", exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            // The provider's own error text is not shown to the user (spec FR-009); it goes no
            // further than this exception, which the API turns into a generic provider failure.
            throw new IdentityProviderException(
                $"The identity provider refused the code exchange ({(int)response.StatusCode}).");
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenEndpointResponse>(cancellationToken)
            ?? throw new IdentityProviderException("The identity provider returned an unreadable response.");

        if (string.IsNullOrWhiteSpace(payload.IdToken))
        {
            throw new IdentityProviderException("The identity provider returned no identity token.");
        }

        return await ValidateIdentityTokenAsync(payload.IdToken, expectedNonce, configuration);
    }

    public async Task<Uri?> GetEndSessionUriAsync(Uri returnTo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(returnTo);

        var configuration = await GetConfigurationAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(configuration.EndSessionEndpoint))
        {
            return null;
        }

        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["post_logout_redirect_uri"] = returnTo.ToString(),
            ["client_id"] = settings.Value.ClientId,
        };

        return new Uri(QueryHelpers.AddQuery(configuration.EndSessionEndpoint, query));
    }

    private async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await configurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new IdentityProviderException(
                "The identity provider's discovery document could not be read.",
                exception);
        }
    }

    private async Task<ProviderIdentity> ValidateIdentityTokenAsync(
        string idToken,
        string expectedNonce,
        OpenIdConnectConfiguration configuration)
    {
        var options = settings.Value;

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration.Issuer,
            ValidateAudience = true,
            ValidAudience = options.ClientId,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parameters);

        if (!result.IsValid)
        {
            throw new IdentityProviderException("The identity token failed validation.");
        }

        var token = (JsonWebToken)result.SecurityToken;

        // A mismatched nonce means this response is not the one we asked for - a replay, or a
        // response meant for a different sign-in attempt.
        var nonce = ReadClaim(token, "nonce");

        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new IdentityProviderException("The identity token nonce did not match the request.");
        }

        var names = options.ClaimNames;

        // Organizational placement is not read from the provider (spec FR-018). The CRM owns it.
        return new ProviderIdentity(
            token.Issuer,
            ReadClaim(token, names.Subject) ?? throw new IdentityProviderException("The identity token carried no subject."),
            ReadClaim(token, names.Email) ?? string.Empty,
            ReadClaim(token, names.Name) ?? string.Empty,
            ReadBooleanClaim(token, names.EmailVerified));
    }

    private static string? ReadClaim(JsonWebToken token, string type) =>
        token.TryGetClaim(type, out var claim) ? claim.Value : null;

    /// <summary>
    /// Reads an asserted boolean. Absent, unparseable, or spelled under a different name all read
    /// as false, because only a positive assertion may unlock a claim (spec FR-016).
    /// </summary>
    /// <remarks>
    /// The value is compared as a string rather than deserialized, because providers disagree about
    /// whether it is a JSON boolean or the text "true" - Keycloak sends the former, several others
    /// the latter, and a strict read would silently refuse every claim at half of them.
    /// </remarks>
    private static bool ReadBooleanClaim(JsonWebToken token, string type) =>
        string.Equals(ReadClaim(token, type), "true", StringComparison.OrdinalIgnoreCase);

    private static string CreateRandomValue() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string CreateChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    private sealed record TokenEndpointResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id_token")]
        public string? IdToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
    }
}

/// <summary>Provider settings, mirrored into Infrastructure (see <see cref="TokenIssuerSettings"/>).</summary>
public sealed class ProviderSettings
{
    public string? Authority { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string Scope { get; init; } = "openid profile email";

    public ProviderClaimSettings ClaimNames { get; init; } = new();
}

/// <summary>Claim names differ between providers, so they are configuration (spec FR-002).</summary>
public sealed class ProviderClaimSettings
{
    public string Subject { get; init; } = "sub";

    public string Name { get; init; } = "name";

    public string Email { get; init; } = "email";

    /// <summary>The verified-email assertion a first sign-in may claim a record on (spec FR-021).</summary>
    public string EmailVerified { get; init; } = "email_verified";

    // Placement claim names removed by feature 003 (spec FR-018).
}

/// <summary>Minimal query-string composition, kept local so Infrastructure needs no web dependency.</summary>
internal static class QueryHelpers
{
    public static string AddQuery(string uri, IDictionary<string, string?> parameters)
    {
        var builder = new StringBuilder(uri);
        var separator = uri.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        foreach (var (key, value) in parameters.Where(entry => !string.IsNullOrWhiteSpace(entry.Value)))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value!)}");
            separator = '&';
        }

        return builder.ToString();
    }
}
