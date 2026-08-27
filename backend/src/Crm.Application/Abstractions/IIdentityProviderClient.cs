namespace Crm.Application.Abstractions;

/// <summary>
/// The identity provider, behind one adapter (Constitution XV). Nothing above this interface knows
/// which provider is configured, which is what makes the choice configuration rather than code
/// (spec FR-001).
/// </summary>
public interface IIdentityProviderClient
{
    /// <summary>
    /// Builds the address to send the browser to, and returns the flow state that must come back
    /// with the callback for the exchange to be trusted.
    /// </summary>
    Task<AuthorizationRequest> CreateAuthorizationRequestAsync(
        string redirectUri,
        string? uiLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges the authorization code and validates the identity token. Throws
    /// <see cref="IdentityProviderException"/> when the provider is unreachable or rejects the
    /// exchange - the caller turns that into a provider problem rather than a user error.
    /// </summary>
    Task<ProviderIdentity> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        string expectedNonce,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's end-session address, when it publishes one. Null means the provider offers no
    /// sign-out, and the user is told only their CRM session ended.
    /// </summary>
    Task<Uri?> GetEndSessionUriAsync(Uri returnTo, CancellationToken cancellationToken = default);
}

/// <param name="AuthorizationUri">Where to send the browser.</param>
/// <param name="CodeVerifier">PKCE verifier, kept by the CRM and never sent to the browser in the clear.</param>
/// <param name="Nonce">Replay protection, echoed back in the identity token.</param>
public sealed record AuthorizationRequest(Uri AuthorizationUri, string CodeVerifier, string Nonce);

/// <summary>
/// What the provider asserted about a person, after validation. Placement claims are optional -
/// a provider that carries no organizational data is normal (spec FR-026).
/// </summary>
public sealed record ProviderIdentity(
    string Subject,
    string Email,
    string DisplayName,
    Guid? DepartmentId,
    Guid? BranchId,
    Guid? TeamId);

/// <summary>
/// The provider could not be reached, or refused the exchange. Distinct from a user error, because
/// telling someone their credentials are wrong when the provider is simply down wastes their time
/// (spec FR-009).
/// </summary>
public sealed class IdentityProviderException : Exception
{
    public IdentityProviderException(string message)
        : base(message) { }

    public IdentityProviderException(string message, Exception innerException)
        : base(message, innerException) { }

    public IdentityProviderException() { }
}
