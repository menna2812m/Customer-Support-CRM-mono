namespace Crm.Infrastructure.Configuration;

/// <summary>
/// Seam for the host-side protected store that supplies secrets at runtime (spec FR-008).
/// Production runs on Windows Server under IIS, so the shipped default reads a DPAPI-protected
/// file kept outside the published folder. Operations may replace the implementation - a vault,
/// a managed identity, a hardware store - without any change to calling code.
/// </summary>
public interface ISecretsSource
{
    /// <summary>
    /// Returns configuration keys and values, using the standard <c>Section:Key</c> notation.
    /// Returns an empty set when no store is configured; that is not an error, because
    /// development supplies secrets through user-secrets instead.
    /// </summary>
    IReadOnlyDictionary<string, string?> Load();
}
