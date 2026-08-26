using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Crm.Application.Authorization;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Authorization;

/// <summary>
/// Constitution IV and spec AR-003, AR-004, SC-008: authorization is enforced by the backend, on
/// every protected endpoint, for every rejection reason - and rejections reveal nothing.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuthorizationTests(SqlServerFixture database)
{
    private const string ItemsPath = "/api/v1/diagnostics/items";

    [Fact]
    public async Task Anonymous_callers_are_rejected_as_unauthenticated()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri(ItemsPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadCodeAsync(response)).ShouldBe("unauthenticated");
    }

    [Fact]
    public async Task Authenticated_callers_without_the_permission_are_forbidden()
    {
        using var client = CreateClient(TestTokens.Staff("customers.view"));

        var response = await client.GetAsync(new Uri(ItemsPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(response)).ShouldBe("forbidden");
    }

    [Fact]
    public async Task Callers_holding_the_declared_permission_are_allowed()
    {
        using var client = CreateClient(TestTokens.Staff(Permissions.Diagnostics.Read));

        var response = await client.GetAsync(new Uri(ItemsPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_portal_caller_cannot_reach_a_staff_only_endpoint_with_the_same_permission()
    {
        // Spec AR-004: the permission name is identical; only the population differs.
        using var client = CreateClient(TestTokens.Portal(Permissions.Diagnostics.Read));

        var response = await client.GetAsync(new Uri(ItemsPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_forbidden_resource_is_indistinguishable_from_one_that_does_not_exist()
    {
        // Spec FR-026: a caller must not be able to probe for existence through the error body.
        using var forbiddenClient = CreateClient(TestTokens.Staff("customers.view"));
        using var missingClient = CreateClient(TestTokens.Staff(Permissions.Diagnostics.Read));

        var forbidden = await forbiddenClient.GetAsync(new Uri(ItemsPath, UriKind.Relative));
        var missing = await missingClient.GetAsync(
            new Uri("/api/v1/diagnostics/items/does-not-exist", UriKind.Relative));

        var forbiddenBody = await ReadWithoutVolatileFieldsAsync(forbidden);
        var missingBody = await ReadWithoutVolatileFieldsAsync(missing);

        // Both are refusals that disclose nothing about the target: no name, no reason beyond the
        // status, no hint that one of them exists.
        forbiddenBody.ShouldNotContain("diagnostics");
        missingBody.ShouldNotContain("exist");
        forbiddenBody.ShouldNotContain("permission");
    }

    private HttpClient CreateClient(string? token = null)
    {
        var factory = new CrmWebApplicationFactory(database.ConnectionString);
        var client = factory.CreateClient();

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString() ?? string.Empty;
    }

    private static async Task<string> ReadWithoutVolatileFieldsAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);

        // Correlation ids and instance paths differ by construction; compare the rest.
        return string.Join(
            "|",
            document.RootElement
                .EnumerateObject()
                .Where(property => property.Name is not ("correlationId" or "instance" or "traceId"))
                .Select(property => $"{property.Name}={property.Value}"));
    }
}
