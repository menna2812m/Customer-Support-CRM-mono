using Microsoft.Extensions.Options;

namespace Crm.Api.Configuration;

/// <summary>
/// Binds and validates every settings section, and fails startup with one message that names
/// every problem rather than stopping at the first (spec FR-007).
/// </summary>
public static class CrmConfiguration
{
    public static IServiceCollection AddCrmOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations();

        services
            .AddOptions<CorsOptions>()
            .BindConfiguration(CorsOptions.SectionName)
            .ValidateDataAnnotations();

        services
            .AddOptions<AuthOptions>()
            .BindConfiguration(AuthOptions.SectionName)
            .ValidateDataAnnotations();

        services
            .AddOptions<ObservabilityOptions>()
            .BindConfiguration(ObservabilityOptions.SectionName)
            .ValidateDataAnnotations();

        services
            .AddOptions<TokenOptions>()
            .BindConfiguration(TokenOptions.SectionName)
            .ValidateDataAnnotations();

        services
            .AddOptions<CrmSessionOptions>()
            .BindConfiguration(CrmSessionOptions.SectionName)
            .ValidateDataAnnotations();

        services
            .AddOptions<IdentityOptions>()
            .BindConfiguration(IdentityOptions.SectionName)
            .ValidateDataAnnotations();

        services
            .AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateDataAnnotations();

        return services;
    }

    /// <summary>
    /// Resolves every options type once, at startup, collecting all failures.
    /// Called immediately after the host is built so a misconfigured deployment stops here with a
    /// clear message instead of failing later with something unrelated.
    /// </summary>
    public static void ValidateCrmConfiguration(this IServiceProvider services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        using var scope = services.CreateScope();
        var problems = new List<string>();

        Validate<DatabaseOptions>(scope.ServiceProvider, DatabaseOptions.SectionName, problems);
        Validate<CorsOptions>(scope.ServiceProvider, CorsOptions.SectionName, problems);
        Validate<AuthOptions>(scope.ServiceProvider, AuthOptions.SectionName, problems);
        Validate<ObservabilityOptions>(scope.ServiceProvider, ObservabilityOptions.SectionName, problems);
        Validate<TokenOptions>(scope.ServiceProvider, TokenOptions.SectionName, problems);
        Validate<CrmSessionOptions>(scope.ServiceProvider, CrmSessionOptions.SectionName, problems);
        Validate<IdentityOptions>(scope.ServiceProvider, IdentityOptions.SectionName, problems);
        Validate<RateLimitingOptions>(scope.ServiceProvider, RateLimitingOptions.SectionName, problems);

        ValidateAuthentication(scope.ServiceProvider, problems);
        ValidateRateLimiting(scope.ServiceProvider, environment, problems);

        // Spec FR-054: no environment may run an unrestricted origin policy. A wildcard here
        // would quietly undo the allowlist, so startup refuses it.
        var cors = TryGet<CorsOptions>(scope.ServiceProvider);

        if (cors is not null)
        {
            foreach (var origin in cors.AllowedOrigins.Where(o => o.Contains('*', StringComparison.Ordinal)))
            {
                problems.Add(
                    $"{CorsOptions.SectionName}:{nameof(CorsOptions.AllowedOrigins)} contains the wildcard "
                        + $"origin '{origin}'. Every environment must list its origins explicitly.");
            }
        }

        // Spec FR-013: automatic migration is a development-only convenience.
        if (!environment.IsDevelopment())
        {
            var database = TryGet<DatabaseOptions>(scope.ServiceProvider);
            if (database?.AutoMigrateOnStartup == true)
            {
                problems.Add(
                    $"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.AutoMigrateOnStartup)} "
                        + $"must be false outside Development (current environment: {environment.EnvironmentName}).");
            }
        }

        if (problems.Count == 0)
        {
            return;
        }

        throw new OptionsValidationException(
            "Configuration",
            typeof(CrmConfiguration),
            [
                $"The application cannot start because {problems.Count} configuration problem(s) were found:",
                .. problems.Select(p => "  - " + p),
            ]);
    }

    /// <summary>
    /// Authentication settings only make sense as a set, so they are checked as one: an enabled
    /// provider needs an authority, a client identity, and a secret, and the CRM cannot issue
    /// credentials without a signing key. Reporting them together means one restart, not four.
    /// </summary>
    private static void ValidateAuthentication(IServiceProvider services, List<string> problems)
    {
        var auth = TryGet<AuthOptions>(services);
        var token = TryGet<TokenOptions>(services);

        if (auth?.Staff.Enabled == true)
        {
            if (string.IsNullOrWhiteSpace(auth.Staff.Authority))
            {
                problems.Add(
                    $"{AuthOptions.SectionName}:Staff:Authority is required when the staff scheme is enabled.");
            }

            if (string.IsNullOrWhiteSpace(auth.Staff.ClientId))
            {
                problems.Add(
                    $"{AuthOptions.SectionName}:Staff:ClientId is required when the staff scheme is enabled.");
            }

            if (string.IsNullOrWhiteSpace(auth.Staff.ClientSecret))
            {
                problems.Add(
                    $"{AuthOptions.SectionName}:Staff:ClientSecret is required when the staff scheme is enabled. "
                        + "Supply it through the secrets source, never in a settings file.");
            }

            if (string.IsNullOrWhiteSpace(token?.SigningKey))
            {
                problems.Add(
                    $"{TokenOptions.SectionName}:SigningKey is required when any scheme is enabled. "
                        + "Supply it through the secrets source.");
            }
        }
    }

    /// <summary>
    /// Checks the throttling settings as a set. A policy name that matches nothing is the failure
    /// worth catching: it looks configured, and it protects nothing.
    /// </summary>
    private static void ValidateRateLimiting(
        IServiceProvider services,
        IHostEnvironment environment,
        List<string> problems)
    {
        var limits = TryGet<RateLimitingOptions>(services);

        if (limits is null)
        {
            return;
        }

        if (!limits.Enabled && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            problems.Add(
                $"{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.Enabled)} must be true outside "
                    + $"Development (current environment: {environment.EnvironmentName}). The anonymous "
                    + "authentication endpoints cannot be left unthrottled.");
        }

        foreach (var name in limits.Policies.Keys.Where(name => !CrmRateLimitPolicies.All.Contains(name)))
        {
            problems.Add(
                $"{RateLimitingOptions.SectionName}:Policies contains '{name}', which no endpoint references. "
                    + $"Known policies: {string.Join(", ", CrmRateLimitPolicies.All)}.");
        }

        foreach (var (name, policy) in limits.Policies)
        {
            if (policy.PermitLimit < 1 || policy.WindowSeconds < 1)
            {
                problems.Add(
                    $"{RateLimitingOptions.SectionName}:Policies:{name} must permit at least one request "
                        + "in a window of at least one second.");
            }
        }
    }

    private static void Validate<TOptions>(
        IServiceProvider services,
        string sectionName,
        List<string> problems)
        where TOptions : class
    {
        try
        {
            _ = services.GetRequiredService<IOptions<TOptions>>().Value;
        }
        catch (OptionsValidationException ex)
        {
            problems.AddRange(ex.Failures.Select(failure => $"{sectionName}: {failure}"));
        }
    }

    private static TOptions? TryGet<TOptions>(IServiceProvider services)
        where TOptions : class
    {
        try
        {
            return services.GetRequiredService<IOptions<TOptions>>().Value;
        }
        catch (OptionsValidationException)
        {
            return null;
        }
    }
}
