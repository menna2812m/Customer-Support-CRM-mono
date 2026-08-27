using Serilog.Core;
using Serilog.Events;

namespace Crm.Api.Configuration;

/// <summary>
/// Removes sensitive values from every log entry, whatever wrote it (spec FR-042, SC-007).
///
/// This runs at the pipeline boundary on purpose. Relying on each call site to remember which
/// values are sensitive is the approach that fails: it only takes one <c>LogInformation</c> with
/// an interpolated request object to put a token in a file that is retained for a month.
/// </summary>
public sealed class RedactingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        foreach (var property in logEvent.Properties.ToList())
        {
            if (IsSensitiveName(property.Key))
            {
                logEvent.AddOrUpdateProperty(
                    propertyFactory.CreateProperty(property.Key, LoggingSetup.RedactedValue));

                continue;
            }

            if (property.Value is StructureValue structure && ContainsSensitiveMember(structure))
            {
                logEvent.AddOrUpdateProperty(
                    new LogEventProperty(property.Key, Redact(structure)));
            }
        }
    }

    /// <summary>Whether a property name is one whose value must never be logged.</summary>
    public static bool IsSensitiveName(string name) =>
        LoggingSetup.SensitiveNames.Any(sensitive =>
            name.Contains(sensitive, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsSensitiveMember(StructureValue structure) =>
        structure.Properties.Any(property => IsSensitiveName(property.Name));

    private static StructureValue Redact(StructureValue structure)
    {
        var properties = structure.Properties.Select(property =>
            IsSensitiveName(property.Name)
                ? new LogEventProperty(property.Name, new ScalarValue(LoggingSetup.RedactedValue))
                : property);

        return new StructureValue(properties, structure.TypeTag);
    }
}
