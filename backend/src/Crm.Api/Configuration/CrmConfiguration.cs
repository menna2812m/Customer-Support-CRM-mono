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
