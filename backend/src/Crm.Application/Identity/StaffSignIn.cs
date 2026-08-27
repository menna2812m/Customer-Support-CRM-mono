using Crm.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Crm.Application.Identity;

/// <summary>
/// Turns a validated provider assertion into a CRM identity (spec FR-004 to FR-007, FR-023, FR-024).
///
/// Everything here is deliberately conservative. A person is recognised only by the provider's
/// subject; an email that matches somebody else is a refusal rather than a merge; and nothing the
/// provider asserts about permissions or population is read at all - the CRM decides those.
/// </summary>
public sealed class StaffSignIn(
    IIdentityStore identityStore,
    IAuthenticationEventLog events,
    ILogger<StaffSignIn> logger)
{
    public async Task<SignInOutcome> ExecuteAsync(
        ProviderIdentity provider,
        IdentitySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(settings);

        var email = NormalizeEmail(provider.Email);
        var placement = ReadPlacement(provider);

        var existing = await identityStore.FindBySubjectAsync(provider.Subject, cancellationToken);

        if (existing is null)
        {
            var outcome = await ProvisionAsync(provider, email, placement, settings, cancellationToken);

            if (outcome is not null)
            {
                return outcome;
            }

            existing = await identityStore.FindBySubjectAsync(provider.Subject, cancellationToken)
                ?? throw new InvalidOperationException("The user was provisioned but could not be read back.");
        }
        else
        {
            // Only what the provider owns is refreshed. Placement is left alone when the provider
            // asserted none, so a directory without organizational data does not erase a value the
            // organization feature will later populate (spec FR-026).
            await identityStore.RefreshAsync(existing.Id, email, provider.DisplayName, placement, cancellationToken);
        }

        if (!existing.IsActive)
        {
            await events.RecordRefusalAsync(
                "inactive",
                provider.Subject,
                existing.Id,
                cancellationToken);

            return SignInOutcome.Refused(SignInRefusal.Inactive);
        }

        await EnsureRolesAsync(existing, settings, provider, cancellationToken);

        var permissions = await identityStore.GetEffectivePermissionsAsync(existing.Id, cancellationToken);

        if (permissions.Count == 0)
        {
            // Authenticated, recognised, and granted nothing. Distinct from a failed sign-in,
            // because the person needs to be told what to ask for (spec FR-006).
            await events.RecordRefusalAsync("no_access", provider.Subject, existing.Id, cancellationToken);

            return SignInOutcome.Refused(SignInRefusal.NoAccess);
        }

        await events.RecordSuccessAsync(existing.Id, provider.Subject, cancellationToken);

        return SignInOutcome.Success(existing, permissions);
    }

    /// <summary>
    /// Creates the user, unless the email already belongs to someone else. Returns a refusal when
    /// it does, and null when provisioning succeeded.
    /// </summary>
    private async Task<SignInOutcome?> ProvisionAsync(
        ProviderIdentity provider,
        string email,
        OrganizationScope? placement,
        IdentitySettings settings,
        CancellationToken cancellationToken)
    {
        var byEmail = await identityStore.FindByEmailAsync(email, cancellationToken);

        if (byEmail is not null)
        {
            // An employee left and their address was reissued, or an account was duplicated. Linking
            // would hand the new person the previous holder's roles and history; duplicating would
            // split one person across two records. Only a human can tell which, so the CRM asks.
            logger.LogWarning(
                "Sign-in refused: subject {Subject} presented an email already held by user {UserId}.",
                provider.Subject,
                byEmail.Id);

            await events.RecordCollisionAsync(provider.Subject, byEmail.Id, cancellationToken);

            return SignInOutcome.Refused(SignInRefusal.IdentityCollision);
        }

        await identityStore.ProvisionAsync(
            provider.Subject,
            email,
            provider.DisplayName,
            placement,
            cancellationToken);

        return null;
    }

    /// <summary>
    /// Grants the bootstrap administrator role, or the configured default role, to a user who holds
    /// nothing yet. Neither ever re-grants access that was deliberately removed, because both apply
    /// only when the user has no assignment at all.
    /// </summary>
    private async Task EnsureRolesAsync(
        UserRecord user,
        IdentitySettings settings,
        ProviderIdentity provider,
        CancellationToken cancellationToken)
    {
        if (await identityStore.HasAnyRoleAsync(user.Id, cancellationToken))
        {
            return;
        }

        if (IsBootstrapAdministrator(settings.BootstrapAdministrator, provider.Subject, user.Email))
        {
            if (await identityStore.GrantRoleAsync(user.Id, IdentitySettings.AdministratorRole, cancellationToken))
            {
                logger.LogWarning(
                    "User {UserId} was granted administrative permissions as the configured bootstrap administrator.",
                    user.Id);

                await events.RecordRoleGrantAsync(user.Id, IdentitySettings.AdministratorRole, "bootstrap", cancellationToken);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(settings.DefaultRole))
        {
            // Legitimate: the deployment chose restriction over reach. The caller receives a
            // no-access outcome rather than a silent empty session.
            return;
        }

        if (await identityStore.GrantRoleAsync(user.Id, settings.DefaultRole, cancellationToken))
        {
            await events.RecordRoleGrantAsync(user.Id, settings.DefaultRole, "default", cancellationToken);
        }
        else
        {
            // A default role naming a role that does not exist would otherwise look like "this user
            // has no access" and send someone hunting in the wrong place.
            logger.LogError(
                "The configured default role '{Role}' does not exist, so user {UserId} was granted nothing.",
                settings.DefaultRole,
                user.Id);
        }
    }

    /// <summary>
    /// Matches on the provider subject or the email address. Email is acceptable here because this
    /// is a configured value chosen by an operator, not an assertion a caller can influence.
    /// </summary>
    private static bool IsBootstrapAdministrator(string? configured, string subject, string email) =>
        !string.IsNullOrWhiteSpace(configured)
        && (string.Equals(configured, subject, StringComparison.Ordinal)
            || string.Equals(NormalizeEmail(configured), email, StringComparison.Ordinal));

    /// <summary>
    /// Reads placement claims when the provider asserted them. Returns null when it asserted none,
    /// which means "leave whatever the CRM already holds".
    /// </summary>
    private static OrganizationScope? ReadPlacement(ProviderIdentity provider) =>
        provider.DepartmentId is null && provider.BranchId is null && provider.TeamId is null
            ? null
            : new OrganizationScope(provider.DepartmentId, provider.BranchId, provider.TeamId);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>Why a sign-in was refused. Each maps to a distinct, translatable error code.</summary>
public enum SignInRefusal
{
    NoAccess,
    Inactive,
    IdentityCollision,
}

/// <summary>The result of a sign-in attempt, success or refusal.</summary>
public sealed record SignInOutcome
{
    private SignInOutcome() { }

    public bool Succeeded { get; private init; }

    public SignInRefusal? Refusal { get; private init; }

    public UserRecord? User { get; private init; }

    public IReadOnlySet<string> Permissions { get; private init; } = new HashSet<string>(StringComparer.Ordinal);

    public static SignInOutcome Success(UserRecord user, IReadOnlySet<string> permissions) =>
        new() { Succeeded = true, User = user, Permissions = permissions };

    public static SignInOutcome Refused(SignInRefusal refusal) =>
        new() { Succeeded = false, Refusal = refusal };
}

/// <summary>
/// How a person becomes a user with access. Both values are explicit configuration: a default
/// administrator with known credentials would be a back door, and a hard-coded default role would
/// decide reach on the deployment's behalf.
/// </summary>
public sealed record IdentitySettings(string? BootstrapAdministrator, string? DefaultRole)
{
    public const string AdministratorRole = "Administrator";
}
