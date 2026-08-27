using System.Net;
using System.Text.RegularExpressions;
using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Persistence;
using Crm.IntegrationTests.Infrastructure.FakeOidc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Crm.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real sign-in handshake against the in-process provider, so a test asserts on the flow
/// rather than on a substitute for it. The browser's part - following redirects and carrying
/// cookies - is performed here explicitly, which is also what makes the cookie rules observable.
/// </summary>
public sealed partial class SignInHarness : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly CookieJar _cookies = new();

    private SignInHarness(
        WebApplicationFactory<Program> factory,
        FakeOidcProvider provider,
        AdjustableTimeProvider clock)
    {
        _factory = factory;
        Provider = provider;
        Clock = clock;
    }

    public FakeOidcProvider Provider { get; }

    /// <summary>The application's clock, so a test can reach an expiry instead of waiting for one.</summary>
    public AdjustableTimeProvider Clock { get; }

    public IServiceProvider Services => _factory.Services;

    public static SignInHarness Create(
        string connectionString,
        IDictionary<string, string?>? overrides = null)
    {
        var provider = new FakeOidcProvider();
        var clock = new AdjustableTimeProvider();

        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authentication:Staff:Enabled"] = "true",
            ["Authentication:Staff:Authority"] = FakeOidcProvider.Issuer,
            ["Authentication:Staff:ClientId"] = FakeOidcProvider.ClientId,
            ["Authentication:Staff:ClientSecret"] = FakeOidcProvider.ClientSecret,
            ["Authentication:Staff:ApplicationBaseUrl"] = "http://localhost:4200",
            ["Identity:DefaultRole"] = "Agent",
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var factory = new CrmWebApplicationFactory(connectionString, overrides: settings);

        // WithWebHostBuilder returns a delegating factory rather than the subclass, so the harness
        // holds the base type and uses only what the base offers.
        var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Point the provider client and the discovery manager at the in-process provider.
            services
                .AddHttpClient(Crm.Infrastructure.DependencyInjection.ProviderHttpClient)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeOidcHandler(provider));

            // Replaces the system clock, so the session-lifetime tests reach a real limit rather
            // than testing a shortened one.
            services.AddSingleton<TimeProvider>(clock);

            // Gives a test an attributable source address, which the in-memory server otherwise
            // lacks - see TestClientAddressFilter.
            services.AddSingleton<IStartupFilter, TestClientAddressFilter>();
        }));

        return new SignInHarness(configured, provider, clock);
    }

    /// <summary>
    /// Completes a full sign-in for the given account and returns a client carrying the resulting
    /// credential, plus the renewal cookie held in the jar.
    /// </summary>
    public async Task<SignInResult> SignInAsync(FakeOidcAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using var client = CreateRawClient();

        // 1. Start the flow. The response redirects to the provider and sets the flow cookie.
        var start = await client.GetAsync(new Uri("/api/v1/auth/sign-in?returnUrl=%2Ftickets%2F42", UriKind.Relative));
        _cookies.Capture(start);

        if (start.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found))
        {
            return SignInResult.Failed(start.StatusCode, null);
        }

        var authorizeUri = start.Headers.Location!;
        var query = System.Web.HttpUtility.ParseQueryString(authorizeUri.Query);

        // 2. The provider authenticates the person and issues a code against the PKCE challenge.
        var code = Provider.Authorize(
            account.Subject,
            query["code_challenge"]!,
            query["nonce"]!);

        // 3. The browser returns to the callback with the code and the flow cookie.
        using var callbackRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auth/callback?code={code}&state={query["state"]}");

        _cookies.Apply(callbackRequest);

        var callback = await client.SendAsync(callbackRequest);
        _cookies.Capture(callback);

        var error = callback.Headers.Location is { } location
            ? System.Web.HttpUtility.ParseQueryString(location.Query)["error"]
            : null;

        return new SignInResult(callback.StatusCode, error, _cookies.Renewal, callback.Headers.Location);
    }

    /// <summary>
    /// Exchanges the renewal cookie for an access credential, as the application does.
    /// </summary>
    /// <param name="renewalCookie">A specific credential to present, rather than the one held.</param>
    /// <param name="withApplicationHeader">Omit to reproduce a cross-site form post.</param>
    /// <param name="origin">Send an <c>Origin</c> header, to reproduce a cross-origin caller.</param>
    public async Task<HttpResponseMessage> RequestSessionAsync(
        string? renewalCookie = null,
        bool withApplicationHeader = true,
        string? origin = null)
    {
        using var client = CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/session");

        if (withApplicationHeader)
        {
            request.Headers.Add("X-Requested-With", "CrmWeb");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        var cookie = renewalCookie ?? _cookies.Renewal;

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"crm_renewal={cookie}");
        }

        var response = await client.SendAsync(request);
        _cookies.Capture(response);

        return response;
    }

    /// <summary>
    /// Exchanges the held renewal cookie for an access credential and returns it. The arrangement
    /// almost every test needs, kept in one place so a test body says what it is about.
    /// </summary>
    public async Task<string> IssueAccessCredentialAsync()
    {
        using var response = await RequestSessionAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    /// <summary>A client carrying the given access credential, for calling protected endpoints.</summary>
    public HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        client.DefaultRequestHeaders.Add("X-Requested-With", "CrmWeb");

        return client;
    }

    public string? RenewalCookie => _cookies.Renewal;

    public async Task<Guid> GetUserIdAsync(string subject)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var user = await context.Users.AsNoTracking().SingleAsync(entry => entry.ProviderSubject == subject);

        return user.Id;
    }

    /// <summary>
    /// The events recorded for one subject. Scoped deliberately: the suite shares a database, so an
    /// unscoped query would see every other test's events and assert on noise.
    /// </summary>
    public async Task<List<AuthenticationEvent>> GetEventsAsync(string subject)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        return await context.AuthenticationEvents
            .AsNoTracking()
            .Where(entry => entry.SubjectReference == subject)
            .ToListAsync();
    }

    /// <summary>Seeds a user directly, for tests about collisions and deactivation.</summary>
    public async Task<Guid> SeedUserAsync(string subject, string email, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var user = User.Provision(subject, email, "Seeded", (int)CallerPopulation.Staff, OrganizationPlacement.None);

        if (!isActive)
        {
            user.Deactivate();
        }

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>
    /// Runs work against the application's own services, for arranging state a request cannot
    /// reach - deactivating a user, granting a role, or reading a session row back.
    /// </summary>
    public async Task<TResult> WithServicesAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        using var scope = _factory.Services.CreateScope();

        return await work(scope.ServiceProvider);
    }

    /// <summary>The stored session, so a test can assert on revocation and the reason recorded.</summary>
    public async Task<Session?> GetSessionAsync(Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        return await context.Sessions.AsNoTracking().FirstOrDefaultAsync(entry => entry.Id == sessionId);
    }

    /// <summary>Every session held by one user, newest first.</summary>
    public async Task<List<Session>> GetSessionsForUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        return await context.Sessions
            .AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.StartedAt)
            .ToListAsync();
    }

    /// <summary>The events recorded for one user, whatever subject the attempt referenced.</summary>
    public async Task<List<AuthenticationEvent>> GetEventsForUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        return await context.AuthenticationEvents
            .AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .ToListAsync();
    }

    /// <summary>Grants a seeded role, for tests about a role change landing on the next renewal.</summary>
    public async Task GrantRoleAsync(Guid userId, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdentityStore>();

        (await store.GrantRoleAsync(userId, roleName)).ShouldBeTrue($"the '{roleName}' role should be seeded");
    }

    /// <summary>A client with no credential at all, for proving what is refused before sign-in.</summary>
    public HttpClient CreateClient() => _factory.CreateClient();

    private HttpClient CreateRawClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    /// <summary>The browser's cookie behaviour, made explicit so the tests can assert on it.</summary>
    private sealed class CookieJar
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? Renewal => _values.GetValueOrDefault("crm_renewal");

        public void Capture(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
            {
                return;
            }

            foreach (var header in headers)
            {
                var match = CookiePattern().Match(header);

                if (!match.Success)
                {
                    continue;
                }

                var name = match.Groups["name"].Value;
                var value = match.Groups["value"].Value;

                if (string.IsNullOrEmpty(value) || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                {
                    _values.Remove(name);
                    continue;
                }

                _values[name] = value;
            }
        }

        public void Apply(HttpRequestMessage request)
        {
            if (_values.Count == 0)
            {
                return;
            }

            request.Headers.Add(
                "Cookie",
                string.Join("; ", _values.Select(entry => $"{entry.Key}={entry.Value}")));
        }
    }

    [GeneratedRegex(@"^(?<name>[^=]+)=(?<value>[^;]*)")]
    private static partial Regex CookiePattern();
}

/// <param name="Error">The machine-readable code the callback redirected with, when it refused.</param>
public sealed record SignInResult(
    HttpStatusCode StatusCode,
    string? Error,
    string? RenewalCookie,
    Uri? RedirectedTo)
{
    public bool Succeeded => Error is null && RenewalCookie is not null;

    public static SignInResult Failed(HttpStatusCode status, string? error) => new(status, error, null, null);
}
