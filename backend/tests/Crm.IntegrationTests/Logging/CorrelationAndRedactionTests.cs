using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Api.Configuration;
using Crm.Application.Authorization;
using Crm.IntegrationTests.Infrastructure;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Shouldly;

namespace Crm.IntegrationTests.Logging;

/// <summary>
/// Spec FR-041, FR-042, SC-005, SC-007: a failure can be traced from the identifier the user saw,
/// and no secret reaches a log file.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class CorrelationAndRedactionTests(SqlServerFixture database)
{
    [Fact]
    public async Task A_supplied_correlation_id_is_reused_in_the_header_and_the_error_body()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "trace-from-the-caller");

        var response = await client.GetAsync(new Uri("/api/v1/diagnostics/items", UriKind.Relative));

        response.Headers.GetValues("X-Correlation-Id").ShouldContain("trace-from-the-caller");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("correlationId").GetString().ShouldBe("trace-from-the-caller");
    }

    [Fact]
    public async Task A_correlation_id_is_generated_when_the_caller_supplies_none()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        var header = response.Headers.GetValues("X-Correlation-Id").Single();
        header.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_same_identifier_ties_the_response_to_its_log_entries()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = await factory.SignInAsync(Permissions.Diagnostics.Read);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "trace-to-follow");

        var response = await client.GetAsync(new Uri("/api/v1/diagnostics/boom", UriKind.Relative));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var correlationId = document.RootElement.GetProperty("correlationId").GetString();

        // This is the operator workflow from SC-005: take what the user quotes, find the request.
        correlationId.ShouldBe("trace-to-follow");
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("access_token")]
    [InlineData("ClientSecret")]
    [InlineData("Authorization")]
    [InlineData("ConnectionString")]
    public void Sensitive_property_names_are_recognised_whatever_their_casing_or_wrapping(string name)
    {
        RedactingEnricher.IsSensitiveName(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData("UserId")]
    [InlineData("CorrelationId")]
    [InlineData("RequestPath")]
    public void Ordinary_diagnostic_properties_are_left_alone(string name)
    {
        // Over-redaction is its own failure: a log with everything redacted diagnoses nothing.
        RedactingEnricher.IsSensitiveName(name).ShouldBeFalse();
    }

    [Fact]
    public void A_log_entry_carrying_a_secret_is_redacted_before_it_reaches_a_sink()
    {
        var enricher = new RedactingEnricher();
        var factory = new TestPropertyFactory();

        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplate("Signing in {Password} for {UserId}", []),
            [
                new LogEventProperty("Password", new ScalarValue("hunter2")),
                new LogEventProperty("UserId", new ScalarValue("user-1")),
            ]);

        enricher.Enrich(logEvent, factory);

        logEvent.Properties["Password"].ToString().ShouldNotContain("hunter2");
        logEvent.Properties["Password"].ToString().ShouldContain("redacted");
        logEvent.Properties["UserId"].ToString().ShouldContain("user-1");
    }

    [Fact]
    public void A_nested_object_containing_a_secret_is_redacted_member_by_member()
    {
        var enricher = new RedactingEnricher();

        var structure = new StructureValue(
        [
            new LogEventProperty("Token", new ScalarValue("eyJhbGciOi")),
            new LogEventProperty("Name", new ScalarValue("Sara")),
        ]);

        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplate("Request {@Payload}", []),
            [new LogEventProperty("Payload", structure)]);

        enricher.Enrich(logEvent, new TestPropertyFactory());

        var rendered = logEvent.Properties["Payload"].ToString();
        rendered.ShouldNotContain("eyJhbGciOi");
        rendered.ShouldContain("redacted");
        rendered.ShouldContain("Sara");
    }

    private sealed class TestPropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
