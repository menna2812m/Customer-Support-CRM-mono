using System.Net.Http.Json;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;

namespace Crm.IntegrationTests.Organization;

/// <summary>
/// Shorthand for the organization endpoints, so each test reads as the rule it is checking rather
/// than as a sequence of HTTP calls.
/// </summary>
internal static class OrganizationHarness
{
    internal const string Departments = "/api/v1/organization/departments";
    internal const string Branches = "/api/v1/organization/branches";
    internal const string Teams = "/api/v1/organization/teams";

    /// <summary>
    /// A harness whose first sign-in becomes an administrator - the only seeded role holding
    /// <c>organization.manage</c>.
    /// </summary>
    internal static (SignInHarness Harness, string Email) Administrator(string connectionString)
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        var harness = SignInHarness.Create(
            connectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        return (harness, email);
    }

    /// <summary>Signs the named account in and returns a client carrying its credential.</summary>
    internal static async Task<HttpClient> SignInAsync(SignInHarness harness, string email)
    {
        ArgumentNullException.ThrowIfNull(harness);

        var account = harness.Provider.AddAccount(email: email);
        await harness.SignInAsync(account);

        return harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());
    }

    internal static object NewUnit(string nameAr, string nameEn, string code) =>
        new { nameAr, nameEn, code };

    internal static object Rename(string nameAr, string nameEn) => new { nameAr, nameEn };

    internal static object Activation(bool isActive) => new { isActive };

    internal static object Destination(Guid departmentId) => new { departmentId };

    internal static async Task<Guid> CreateAsync(
        HttpClient client,
        string route,
        string nameAr,
        string nameEn,
        string code)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.PostAsJsonAsync(
            new Uri(route, UriKind.Relative),
            NewUnit(nameAr, nameEn, code));

        response.EnsureSuccessStatusCode();

        return await ReadIdAsync(response);
    }

    internal static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>The stable error code, which is what a client switches on.</summary>
    internal static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    internal static async Task<string?> ReadDetailAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    internal static async Task<int> ReadTotalAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("totalCount").GetInt32();
    }

    /// <summary>
    /// A short token unique to one test.
    /// </summary>
    /// <remarks>
    /// The suite shares a single SQL Server container, and this feature enforces uniqueness on names
    /// and codes across the whole table. Two tests that both create a department called "Support"
    /// therefore interfere - one of them gets a name conflict it never asked for, and only when the
    /// suite runs in full. Tagging every name and code keeps each test independent of the others.
    /// </remarks>
    /// <remarks>
    /// Deliberately <see cref="Guid.NewGuid"/> rather than a version 7 identifier. A version 7 GUID
    /// begins with a timestamp, so its first hex digits are shared by everything created in the same
    /// few hundred milliseconds - which is every test in a class. Taken from there, a "unique" tag
    /// collides precisely when tests run together, which is the only time it is needed.
    /// </remarks>
    internal static string Tag() => Guid.NewGuid().ToString("n")[..8].ToUpperInvariant();

    internal static Uri Route(string value) => new(value, UriKind.Relative);
}
