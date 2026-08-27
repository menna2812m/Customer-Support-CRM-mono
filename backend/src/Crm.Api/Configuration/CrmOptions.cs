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
/// Staff sign in through the corporate identity provider (spec FR-001, FR-002).
///
/// These are provider-facing settings only. What the CRM issues and validates for its own API
/// lives in <see cref="TokenOptions"/> - the provider authenticates a person once, at sign-in, and
/// the API validates a credential it minted itself from then on.
/// </summary>
public sealed class StaffAuthOptions
{
    /// <summary>Identity provider base address; standard OIDC discovery is appended to it.</summary>
    public string? Authority { get; init; }

    public string? ClientId { get; init; }

    /// <summary>Supplied through the secrets source, never from a settings file.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Claim names differ between providers, so they are configuration rather than code - the
    /// residual risk of building against generic OIDC (spec FR-002).
    /// </summary>
    public ProviderClaimNames ClaimNames { get; init; } = new();

    /// <summary>
    /// Where the browser is sent after the handshake. The application is a separate origin during
    /// development, so this is configuration rather than something read from the request.
    /// </summary>
    public string? ApplicationBaseUrl { get; init; }

    public bool Enabled { get; init; }
}

/// <summary>Which claim carries which value at the configured provider.</summary>
public sealed class ProviderClaimNames
{
    public string Subject { get; init; } = "sub";

    public string Name { get; init; } = "name";

    public string Email { get; init; } = "email";

    public string Department { get; init; } = "department";

    public string Branch { get; init; } = "branch";

    public string Team { get; init; } = "team";
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

/// <summary>
/// What the CRM issues and validates for its own API (research decision 1).
///
/// The provider authenticates a person at sign-in; from then on the API validates a credential it
/// minted itself. That is what makes immediate revocation, single-use rotation, and CRM permissions
/// in the session possible at all.
/// </summary>
public sealed class TokenOptions
{
    public const string SectionName = "Token";

    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = "https://crm.azm.sa";

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = "crm-api";

    /// <summary>
    /// Signing key, supplied through the secrets source. Never present in a settings file.
    /// </summary>
    public string? SigningKey { get; init; }

    /// <summary>
    /// Identifies the signing key in the credential header, so a key can be rotated without
    /// invalidating live sessions.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string KeyId { get; init; } = "k1";

    [Range(1, 120)]
    public int AccessCredentialMinutes { get; init; } = 15;
}

/// <summary>Session lifetime and the renewal cookie (spec FR-012, FR-016).</summary>
public sealed class CrmSessionOptions
{
    public const string SectionName = "Session";

    [Range(1, 168)]
    public int InactivityHours { get; init; } = 8;

    [Range(1, 720)]
    public int AbsoluteHours { get; init; } = 12;

    [Required(AllowEmptyStrings = false)]
    public string CookieName { get; init; } = "crm_renewal";
}

/// <summary>
/// Throttling for the endpoints anybody can reach (spec FR-036).
///
/// Configuration rather than code, because the right limit depends on the deployment: a single
/// office behind one address needs a different allowance from a workforce on mobile connections.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Off only where throttling would obscure what is under test. Startup validation refuses a
    /// disabled limiter in production.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Named policies, keyed by the name an endpoint's attribute references.</summary>
    public IReadOnlyDictionary<string, RateLimitPolicyOptions> Policies { get; init; } =
        new Dictionary<string, RateLimitPolicyOptions>(StringComparer.Ordinal);
}

/// <summary>One named policy: how many requests one source may make in one window.</summary>
public sealed class RateLimitPolicyOptions
{
    [Range(1, 100_000)]
    public int PermitLimit { get; init; } = 60;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}

/// <summary>
/// How a person becomes a user with access (spec FR-023, FR-024).
///
/// Both values are deliberately explicit. A default administrator account with known credentials
/// would be a back door; a hard-coded default role would decide reach on the deployment's behalf.
/// </summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// Provider subject or email address that receives administrative permissions on first
    /// sign-in, so a fresh deployment is never locked out of itself.
    /// </summary>
    public string? BootstrapAdministrator { get; init; }

    /// <summary>
    /// Role granted to a staff member who has no assignment yet. Null is legitimate and means new
    /// staff arrive with no access - the safer choice where real data already exists.
    /// </summary>
    public string? DefaultRole { get; init; }
}
