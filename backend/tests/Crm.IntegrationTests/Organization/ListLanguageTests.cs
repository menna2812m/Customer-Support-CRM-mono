using System.Net.Http.Headers;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Organization;

/// <summary>
/// Spec LR-002: a list is ordered by the name the reader actually sees.
///
/// The two departments here are deliberately arranged so the two orders disagree - the one that
/// sorts first in English sorts last in Arabic. An implementation that ignored the language would
/// still pass a test whose names happened to sort the same way in both, which is why the fixture
/// inverts rather than merely differs.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ListLanguageTests(SqlServerFixture database)
{
    [Fact]
    public async Task An_Arabic_reader_gets_the_list_ordered_by_the_Arabic_name()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        await CreateBothAsync(client, tag);

        var names = await ListNamesAsync(client, tag, "ar");

        // "ألف" sorts before "ياء", so the department called Zulu in English comes first.
        names.ShouldBe([$"Zulu {tag}", $"Alpha {tag}"]);
    }

    [Fact]
    public async Task An_English_reader_gets_the_list_ordered_by_the_English_name()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        await CreateBothAsync(client, tag);

        var names = await ListNamesAsync(client, tag, "en");

        names.ShouldBe([$"Alpha {tag}", $"Zulu {tag}"]);
    }

    [Fact]
    public async Task A_caller_that_states_no_language_is_served_English()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        await CreateBothAsync(client, tag);

        var names = await ListNamesAsync(client, tag, language: null);

        names.ShouldBe([$"Alpha {tag}", $"Zulu {tag}"]);
    }

    /// <summary>
    /// Creates one department whose names sort in opposite directions in the two languages, and a
    /// second that inverts it.
    /// </summary>
    private static async Task CreateBothAsync(HttpClient client, string tag)
    {
        await CreateAsync(client, Departments, $"ياء {tag}", $"Alpha {tag}", $"AL{tag}");
        await CreateAsync(client, Departments, $"ألف {tag}", $"Zulu {tag}", $"ZU{tag}");
    }

    private static async Task<string[]> ListNamesAsync(HttpClient client, string tag, string? language)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            Route($"{Departments}?search={tag}&page=1&pageSize=50"));

        if (language is not null)
        {
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        }

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return [.. document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("nameEn").GetString() ?? string.Empty)];
    }
}
