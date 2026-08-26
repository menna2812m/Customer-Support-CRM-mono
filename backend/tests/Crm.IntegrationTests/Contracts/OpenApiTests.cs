using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Contracts;

/// <summary>
/// The published contract and the running application must not drift apart (spec FR-022), and the
/// document must not be reachable outside Development (AR-002).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed partial class OpenApiTests(SqlServerFixture database)
{
    private const string DocumentPath = "/openapi/v1.json";

    [Fact]
    public async Task The_live_document_describes_exactly_the_application_paths_that_the_contract_publishes()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(DocumentPath, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var live = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .Select(path => path.Name)
            .Where(path => path.StartsWith("/api/", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var published = PublishedApiPaths();

        // Drift in either direction is a failure: an undocumented endpoint is invisible to
        // integrators, and a documented endpoint that no longer exists is worse than no document.
        live.ShouldBe(published, ignoreOrder: false);
    }

    [Fact]
    public async Task The_document_and_its_ui_are_not_reachable_outside_development()
    {
        await using var factory = new CrmWebApplicationFactory(
            database.ConnectionString,
            environmentName: "Production");

        using var client = factory.CreateClient();

        (await client.GetAsync(new Uri(DocumentPath, UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        (await client.GetAsync(new Uri("/scalar", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>Reads the application paths from the committed contract document.</summary>
    private static List<string> PublishedApiPaths()
    {
        var contract = File.ReadAllText(ContractPath());

        return PathLine()
            .Matches(contract)
            .Select(match => match.Groups[1].Value)
            .Where(path => path.StartsWith("/api/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static string ContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "specs")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate the repository root from the test output directory.");

        return Path.Combine(
            directory.FullName,
            "specs",
            "001-project-foundation",
            "contracts",
            "foundation-api.yaml");
    }

    [GeneratedRegex(@"^  (/[^\s:]+):", RegexOptions.Multiline)]
    private static partial Regex PathLine();
}
