using System.Text.Json;
using Shouldly;

namespace Crm.UnitTests.Configuration;

/// <summary>
/// Spec FR-005 and FR-008: the production settings file carries non-secret defaults only.
/// Secrets arrive at runtime from the host-side protected store, never from a file inside the
/// published folder - and a file that is committed is a file that gets copied into a ticket.
/// </summary>
public sealed class ProductionSettingsTests
{
    private static readonly string[] SecretBearingKeys =
    [
        "connectionstring",
        "password",
        "signingkey",
        "clientsecret",
        "apikey",
        "secret",
    ];

    [Fact]
    public void The_production_settings_file_contains_no_secret_value()
    {
        var settings = ReadSettings("appsettings.Production.json");

        var populated = Flatten(settings.RootElement)
            .Where(entry => SecretBearingKeys.Any(secret =>
                entry.Path.Contains(secret, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .ToList();

        populated.ShouldBeEmpty(
            "A secret-bearing key has a value in appsettings.Production.json: "
                + string.Join(", ", populated.Select(entry => entry.Path)));
    }

    [Fact]
    public void Automatic_migration_is_off_in_the_production_settings_file()
    {
        var settings = ReadSettings("appsettings.Production.json");

        settings.RootElement
            .GetProperty("Database")
            .GetProperty("AutoMigrateOnStartup")
            .GetBoolean()
            .ShouldBeFalse();
    }

    [Fact]
    public void The_production_settings_file_lists_no_origins_by_default()
    {
        var settings = ReadSettings("appsettings.Production.json");

        // Origins are environment-specific. Shipping a guess would either break the deployment or,
        // worse, allow an origin nobody intended.
        settings.RootElement
            .GetProperty("Cors")
            .GetProperty("AllowedOrigins")
            .GetArrayLength()
            .ShouldBe(0);
    }

    private static JsonDocument ReadSettings(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate the backend root from the test output directory.");

        var path = Path.Combine(directory.FullName, "src", "Crm.Api", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static IEnumerable<(string Path, string? Value)> Flatten(JsonElement element, string prefix = "")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? property.Name : $"{prefix}:{property.Name}";

                    foreach (var entry in Flatten(property.Value, path))
                    {
                        yield return entry;
                    }
                }

                break;

            case JsonValueKind.Array:
                var index = 0;

                foreach (var item in element.EnumerateArray())
                {
                    foreach (var entry in Flatten(item, $"{prefix}:{index++}"))
                    {
                        yield return entry;
                    }
                }

                break;

            default:
                yield return (prefix, element.ToString());
                break;
        }
    }
}
