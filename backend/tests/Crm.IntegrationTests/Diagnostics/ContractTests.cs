using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Application.Authorization;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Diagnostics;

/// <summary>
/// The conventions a future feature inherits, verified through the real pipeline:
/// validation (FR-019), the error contract (FR-017, FR-018), pagination (FR-020), and API
/// versioning (FR-016).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ContractTests(SqlServerFixture database)
{
    private const string ItemsPath = "/api/v1/diagnostics/items";
    private const string EchoPath = "/api/v1/diagnostics/echo";

    [Fact]
    public async Task Validation_failures_name_every_offending_field_with_a_stable_code()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri(EchoPath, UriKind.Relative),
            new { message = "", repeatCount = 99 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        root.TryGetProperty("code", out _).ShouldBeTrue(payload);
        root.GetProperty("code").GetString().ShouldBe("validation_failed");
        root.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();

        var errors = root.GetProperty("errors").EnumerateArray().ToList();
        var fields = errors.Select(e => e.GetProperty("field").GetString()).ToList();

        // Client-facing member paths are camelCase, matching the JSON the caller sent.
        fields.ShouldContain("message");
        fields.ShouldContain("repeatCount");
        errors.Select(e => e.GetProperty("code").GetString()).ShouldContain("required");
        errors.Select(e => e.GetProperty("code").GetString()).ShouldContain("range");
    }

    [Fact]
    public async Task A_valid_payload_reaches_the_use_case()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri(EchoPath, UriKind.Relative),
            new { message = "ping", repeatCount = 2 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("message").GetString().ShouldBe("ping ping");
        document.RootElement.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("", 1, 25)]
    [InlineData("?pageSize=10", 1, 10)]
    [InlineData("?page=6&pageSize=10", 6, 7)]
    public async Task Paging_follows_the_shared_contract(string query, int expectedPage, int expectedCount)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(ItemsPath + query, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("page").GetInt32().ShouldBe(expectedPage);
        root.GetProperty("items").GetArrayLength().ShouldBe(expectedCount);
        root.GetProperty("totalCount").GetInt64().ShouldBe(57);
        root.GetProperty("totalPages").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_page_beyond_the_end_is_an_empty_success_not_an_error()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(ItemsPath + "?page=99", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_page_size_above_the_maximum_is_rejected_rather_than_clamped()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(ItemsPath + "?pageSize=500", UriKind.Relative));

        // Silently returning 100 would let a caller believe it received everything.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(response)).ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Sorting_is_allow_listed()
    {
        using var client = CreateClient();

        var descending = await client.GetAsync(new Uri(ItemsPath + "?sort=-name", UriKind.Relative));
        var unsortable = await client.GetAsync(new Uri(ItemsPath + "?sort=secretColumn", UriKind.Relative));

        descending.StatusCode.ShouldBe(HttpStatusCode.OK);
        unsortable.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await unsortable.Content.ReadAsStringAsync());
        document.RootElement
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .ShouldBe("not_sortable");
    }

    [Fact]
    public async Task An_unknown_query_parameter_is_refused_rather_than_ignored()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(ItemsPath + "?nameContian=x", UriKind.Relative));

        // A misspelled filter that is ignored returns the whole collection and looks like success.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .ShouldBe("unknown_parameter");
    }

    [Fact]
    public async Task An_unsupported_api_version_returns_the_shared_contract()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/api/v9/diagnostics/items", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(response)).ShouldBe("unsupported_api_version");
    }

    [Fact]
    public async Task An_unhandled_exception_returns_a_generic_500_with_no_internal_detail()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/diagnostics/boom", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        var payload = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("code").GetString().ShouldBe("unexpected_error");
        document.RootElement.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();

        // The thrown message deliberately contains a fake connection string: none of it may escape.
        payload.ShouldNotContain("hunter2");
        payload.ShouldNotContain("Server=secret");
        payload.ShouldNotContain("InvalidOperationException");
        payload.ShouldNotContain("at Crm.");
    }

    private HttpClient CreateClient()
    {
        var factory = new CrmWebApplicationFactory(database.ConnectionString);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokens.Staff(Permissions.Diagnostics.Read));

        return client;
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString() ?? string.Empty;
    }
}
