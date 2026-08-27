using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Crm.Infrastructure.Configuration;

/// <summary>
/// Default <see cref="ISecretsSource"/>: a DPAPI-protected JSON file, encrypted for the local
/// machine, living outside the published folder. The path comes from the
/// <c>CRM_SECRETS_FILE</c> environment variable so the location is an operations decision rather
/// than a code constant.
/// </summary>
public sealed class DpapiFileSecretsSource : ISecretsSource
{
    public const string PathVariable = "CRM_SECRETS_FILE";

    private readonly string? _path;

    public DpapiFileSecretsSource(string? path = null) =>
        _path = path ?? Environment.GetEnvironmentVariable(PathVariable);

    public IReadOnlyDictionary<string, string?> Load()
    {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
        {
            // No store configured. Development uses user-secrets; a non-development environment
            // missing a required setting fails fast in options validation with a named message.
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                $"The DPAPI secrets file at '{_path}' can only be read on Windows. "
                    + "Replace ISecretsSource with an implementation for this platform.");
        }

        return ReadProtectedFile(_path);
    }

    [SupportedOSPlatform("windows")]
    private static Dictionary<string, string?> ReadProtectedFile(string path)
    {
        byte[] plaintext;

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            plaintext = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException ex)
        {
            // Never echo the file contents or the exception detail into configuration errors.
            throw new InvalidOperationException(
                $"The secrets file at '{path}' could not be decrypted on this machine. "
                    + "It must be protected for the local machine account that runs the site.",
                ex);
        }

        var json = Encoding.UTF8.GetString(plaintext);
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);

        return values is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
    }
}
