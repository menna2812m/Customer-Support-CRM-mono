using Crm.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Crm.UnitTests.Configuration;

/// <summary>
/// Spec FR-007 and FR-013: a misconfigured deployment stops at startup with a message naming every
/// problem, and automatic migration cannot be switched on outside Development.
/// </summary>
public sealed class ConfigurationValidationTests
{
    [Fact]
    public void Missing_required_settings_are_all_named_in_one_message()
    {
        var services = BuildProvider(new Dictionary<string, string?>
        {
            // Deliberately empty: no connection string, no CORS origins, no log path.
            ["Observability:LogFilePath"] = string.Empty,
        });

        var failure = Should.Throw<OptionsValidationException>(
            () => services.ValidateCrmConfiguration(new TestEnvironment("Production")));

        var message = string.Join(Environment.NewLine, failure.Failures);

        // One message, every problem: a developer should not have to fix, restart, fix, restart.
        message.ShouldContain(DatabaseOptions.SectionName);
        message.ShouldContain(CorsOptions.SectionName);
        message.ShouldContain(ObservabilityOptions.SectionName);
    }

    [Fact]
    public void Valid_settings_start_cleanly()
    {
        var services = BuildProvider(ValidSettings());

        Should.NotThrow(() => services.ValidateCrmConfiguration(new TestEnvironment("Production")));
    }

    [Fact]
    public void Automatic_migration_is_rejected_outside_development()
    {
        var settings = ValidSettings();
        settings["Database:AutoMigrateOnStartup"] = "true";

        var services = BuildProvider(settings);

        var failure = Should.Throw<OptionsValidationException>(
            () => services.ValidateCrmConfiguration(new TestEnvironment("Staging")));

        string.Join(" ", failure.Failures).ShouldContain(nameof(DatabaseOptions.AutoMigrateOnStartup));
    }

    [Fact]
    public void Automatic_migration_is_allowed_in_development()
    {
        var settings = ValidSettings();
        settings["Database:AutoMigrateOnStartup"] = "true";

        var services = BuildProvider(settings);

        Should.NotThrow(() => services.ValidateCrmConfiguration(new TestEnvironment("Development")));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Database:ConnectionString"] = "Server=localhost;Database=Crm;Trusted_Connection=True",
        ["Cors:AllowedOrigins:0"] = "https://crm.example",
        ["Observability:LogFilePath"] = "logs/crm-.log",
        ["Observability:CorrelationHeader"] = "X-Correlation-Id",
    };

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCrmOptions();

        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Crm.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
