using Microsoft.Extensions.Configuration;

namespace Crm.Infrastructure.Configuration;

/// <summary>
/// Layers an <see cref="ISecretsSource"/> into the configuration pipeline. Registered last so
/// host-side secrets win over settings files and environment variables.
/// </summary>
public static class SecretsConfigurationExtensions
{
    public static IConfigurationBuilder AddCrmSecrets(
        this IConfigurationBuilder builder,
        ISecretsSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new SecretsConfigurationSource(source ?? new DpapiFileSecretsSource()));
    }
}

internal sealed class SecretsConfigurationSource(ISecretsSource source) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new SecretsConfigurationProvider(source);
}

internal sealed class SecretsConfigurationProvider(ISecretsSource source) : ConfigurationProvider
{
    public override void Load()
    {
        Data = new Dictionary<string, string?>(source.Load(), StringComparer.OrdinalIgnoreCase);
    }
}
