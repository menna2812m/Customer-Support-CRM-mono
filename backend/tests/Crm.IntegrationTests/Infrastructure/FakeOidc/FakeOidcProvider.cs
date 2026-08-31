using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crm.IntegrationTests.Infrastructure.FakeOidc;

/// <summary>
/// A standards-compliant OIDC provider, in process.
///
/// The suite must prove the real handshake - discovery, PKCE, code exchange, signature and nonce
/// validation - not a mock of it. A provider container would triple suite startup for the same
/// assertions (research decision 9). What this cannot prove is a specific vendor's quirks, which is
/// what the pre-release check against a real provider is for.
/// </summary>
public sealed class FakeOidcProvider
{
    public const string Issuer = "https://fake-idp.tests.local";
    public const string ClientId = "crm-api-tests";
    public const string ClientSecret = "fake-client-secret";

    // Created once for the whole test run and deliberately never disposed: the key is shared by
    // every instance, so disposing it with one provider would break every later test in the run.
    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "fake-idp-key" };

    private readonly ConcurrentDictionary<string, PendingAuthorization> _codes = new(StringComparer.Ordinal);

    /// <summary>Accounts the provider will authenticate, keyed by subject.</summary>
    public ConcurrentDictionary<string, FakeOidcAccount> Accounts { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, every request fails - used to prove the provider-unavailable path.</summary>
    public bool IsUnavailable { get; set; }

    /// <param name="emailVerified">
    /// Whether the identity token asserts <c>email_verified</c>. True by default because a
    /// corporate directory normally does assert it, so the tests that turn it off are the ones
    /// saying something (spec FR-016, FR-017).
    /// </param>
    public FakeOidcAccount AddAccount(
        string? subject = null,
        string? email = null,
        string displayName = "Fake Staff",
        Guid? departmentId = null,
        bool emailVerified = true)
    {
        var account = new FakeOidcAccount(
            subject ?? $"fake|{Guid.CreateVersion7():n}",
            email ?? $"{Guid.CreateVersion7():n}@fake.local",
            displayName,
            departmentId,
            emailVerified);

        Accounts[account.Subject] = account;
        return account;
    }

    /// <summary>
    /// Handles a request to the provider. Returns null for a path the provider does not serve, so
    /// the caller can fall through.
    /// </summary>
    public async Task<HttpResponseMessage?> HandleAsync(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsUnavailable)
        {
            throw new HttpRequestException("The fake identity provider is configured as unavailable.");
        }

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        return path switch
        {
            "/.well-known/openid-configuration" => Json(Discovery()),
            "/jwks" => Json(Jwks()),
            "/token" => await TokenAsync(request),
            _ => null,
        };
    }

    /// <summary>
    /// Completes the part of the flow the browser would perform, returning the code the CRM's
    /// callback expects. The redirect itself is not exercised here; the exchange is.
    /// </summary>
    public string Authorize(string subject, string codeChallenge, string nonce)
    {
        var code = Guid.CreateVersion7().ToString("n");
        _codes[code] = new PendingAuthorization(subject, codeChallenge, nonce);

        return code;
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request)
    {
        var form = await ReadFormAsync(request);

        if (!form.TryGetValue("code", out var code) || !_codes.TryRemove(code, out var pending))
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
        }

        // PKCE is verified rather than assumed: a test that skips it would not prove the CRM sends
        // a verifier that matches the challenge it sent earlier.
        var verifier = form.GetValueOrDefault("code_verifier") ?? string.Empty;

        if (Challenge(verifier) != pending.CodeChallenge)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
        }

        if (form.GetValueOrDefault("client_secret") != ClientSecret)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
        }

        if (!Accounts.TryGetValue(pending.Subject, out var account))
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
        }

        return Json(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["token_type"] = "Bearer",
            ["expires_in"] = 300,
            ["access_token"] = "fake-provider-access-token",
            ["id_token"] = CreateIdToken(account, pending.Nonce),
        });
    }

    private static string CreateIdToken(FakeOidcAccount account, string nonce)
    {
        var claims = new List<Claim>
        {
            new("sub", account.Subject),
            new("email", account.Email),
            new("name", account.DisplayName),
            new("nonce", nonce),
        };

        if (account.EmailVerified)
        {
            // Present only when true. A provider that has not verified an address usually omits the
            // claim rather than sending false, and omission is the case the CRM must fail closed on.
            claims.Add(new Claim("email_verified", "true"));
        }

        if (account.DepartmentId is { } department)
        {
            claims.Add(new Claim("department", department.ToString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static Dictionary<string, object> Discovery() => new(StringComparer.Ordinal)
    {
        ["issuer"] = Issuer,
        ["authorization_endpoint"] = $"{Issuer}/authorize",
        ["token_endpoint"] = $"{Issuer}/token",
        ["jwks_uri"] = $"{Issuer}/jwks",
        ["end_session_endpoint"] = $"{Issuer}/logout",
        ["response_types_supported"] = new[] { "code" },
        ["subject_types_supported"] = new[] { "public" },
        ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
    };

    private static Dictionary<string, object> Jwks()
    {
        var parameters = SigningKey.Rsa!.ExportParameters(includePrivateParameters: false);

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["keys"] = new[]
            {
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["kty"] = "RSA",
                    ["use"] = "sig",
                    ["alg"] = "RS256",
                    ["kid"] = SigningKey.KeyId!,
                    ["n"] = Base64Url(parameters.Modulus!),
                    ["e"] = Base64Url(parameters.Exponent!),
                },
            },
        };
    }

    private static async Task<Dictionary<string, string>> ReadFormAsync(HttpRequestMessage request)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();

        return body
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.Ordinal);
    }

    private static HttpResponseMessage Json(object payload) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    private static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    private sealed record PendingAuthorization(string Subject, string CodeChallenge, string Nonce);
}

/// <param name="DepartmentId">Optional: a provider that carries no organizational data is normal.</param>
public sealed record FakeOidcAccount(
    string Subject,
    string Email,
    string DisplayName,
    Guid? DepartmentId,
    bool EmailVerified = true);

/// <summary>Routes the CRM's provider calls to the in-process provider instead of the network.</summary>
public sealed class FakeOidcHandler(FakeOidcProvider provider) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await provider.HandleAsync(request);

        return response ?? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }
}
