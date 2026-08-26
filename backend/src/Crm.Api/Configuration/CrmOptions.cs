using System.ComponentModel.DataAnnotations;

namespace Crm.Api.Configuration;

/// <summary>Relational database settings. The connection string itself is a secret.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required(AllowEmptyStrings = false, ErrorMessage = "A database connection string is required.")]
    public string ConnectionString { get; init; } = string.Empty;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; init; } = 30;

    [Range(0, 10)]
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// Applies pending migrations at startup. Development convenience only - spec FR-013 requires
    /// this to be off everywhere else, which <see cref="CrmConfiguration"/> enforces.
    /// </summary>
    public bool AutoMigrateOnStartup { get; init; }
}

/// <summary>Cross-origin access, allow-listed per environment (spec FR-054).</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public const string PolicyName = "CrmDefault";

    [MinLength(1, ErrorMessage = "At least one allowed origin must be configured.")]
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}

/// <summary>
/// Authentication settings for the two caller populations (spec FR-023). Both schemes are
/// disabled by default: this feature delivers the seams, and the authentication feature supplies
/// real values.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    public StaffAuthOptions Staff { get; init; } = new();

    public PortalAuthOptions Portal { get; init; } = new();
}

/// <summary>
/// Staff sign in through the corporate identity provider. In production only
/// <see cref="Authority"/> and <see cref="Audience"/> are set; <see cref="SigningKey"/> exists so
/// development and the test suite can issue local tokens without standing up an identity provider.
/// </summary>
public sealed class StaffAuthOptions
{
    /// <summary>Corporate identity provider metadata address.</summary>
    public string? Authority { get; init; }

    public string? Audience { get; init; }

    /// <summary>Issuer expected in the token, used to route to this scheme.</summary>
    public string? Issuer { get; init; }

    /// <summary>Symmetric key for locally issued tokens. Never set in production.</summary>
    public string? SigningKey { get; init; }

    public bool Enabled { get; init; }
}

/// <summary>External customer portal accounts, owned and issued by the CRM.</summary>
public sealed class PortalAuthOptions
{
    /// <summary>Issuer of CRM-owned tokens for external customer portal accounts.</summary>
    public string? Issuer { get; init; }

    public string? Audience { get; init; }

    public string? SigningKey { get; init; }

    public bool Enabled { get; init; }
}

/// <summary>Logging destination and retention (spec FR-040, FR-043).</summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Rolling log file path. Production runs under IIS, so logs must land on disk rather than
    /// relying on console capture.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string LogFilePath { get; init; } = "logs/crm-.log";

    [Range(1, 365)]
    public int RetainedFileCount { get; init; } = 30;

    /// <summary>Inbound header that carries a caller-supplied correlation identifier.</summary>
    [Required(AllowEmptyStrings = false)]
    public string CorrelationHeader { get; init; } = "X-Correlation-Id";
}
