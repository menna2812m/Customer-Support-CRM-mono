using Crm.Application.Abstractions;
using Crm.Application.Common;
using Crm.Application.Identity.Claiming;
using Microsoft.Extensions.Logging;

namespace Crm.Application.Identity;

/// <summary>
/// Turns a validated provider assertion into a CRM identity (spec FR-004 to FR-007, FR-015 to
/// FR-020, FR-023, FR-024).
///
/// Everything here is deliberately conservative. A person is recognised by the provider and the
/// subject together and by nothing else; an address held by somebody established is a refusal
/// rather than a merge; and nothing the provider asserts about permissions or population is read at
/// all - the CRM decides those.
///
/// Feature 004 changed this from create-if-absent to match-then-create. The change is small in code
/// and large in consequence, so the decision itself lives in <see cref="ClaimDecision"/>, where
/// every branch can be read at once, and this class only carries it out.
/// </summary>
public sealed class StaffSignIn(
    IIdentityStore identityStore,
    IAuthenticationEventLog events,
    ClaimAudit claims,
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

        var resolution = await ResolveAsync(provider, email, cancellationToken);

        if (resolution.Refusal is { } refused)
        {
            return SignInOutcome.Refused(refused);
        }

        var person = resolution.User!;

        if (!person.IsActive)
        {
            // A record prepared for somebody and then deactivated before they ever arrived is still
            // claimed above and still refused here. Deactivation is an administrator's decision, and
            // a sign-in does not overturn it.
            await events.RecordRefusalAsync(
                "inactive",
                provider.Subject,
                person.Id,
                cancellationToken);

            return SignInOutcome.Refused(SignInRefusal.Inactive);
        }

        await EnsureRolesAsync(person, settings, provider, cancellationToken);

        var permissions = await identityStore.GetEffectivePermissionsAsync(person.Id, cancellationToken);

        if (permissions.Count == 0)
        {
            // Authenticated, recognised, and granted nothing. Distinct from a failed sign-in,
            // because the person needs to be told what to ask for (spec FR-006).
            await events.RecordRefusalAsync("no_access", provider.Subject, person.Id, cancellationToken);

            return SignInOutcome.Refused(SignInRefusal.NoAccess);
        }

        await events.RecordSuccessAsync(person.Id, provider.Subject, cancellationToken);

        return SignInOutcome.Success(person, permissions);
    }

    /// <summary>
    /// Finds whom this sign-in belongs to - the person already bound to it, the person prepared for
    /// it, or a new one - or refuses (spec FR-015 to FR-019).
    /// </summary>
    /// <remarks>
    /// The decision is <see cref="ClaimDecision"/>'s and the consequences are this method's. Keeping
    /// them apart is what lets the matrix be tested exhaustively without a database, and what stops
    /// a later edit from adding a branch to one and not the other.
    /// </remarks>
    private async Task<Resolution> ResolveAsync(
        ProviderIdentity provider,
        string email,
        CancellationToken cancellationToken)
    {
        var subjectMatch = await identityStore.FindBySubjectAsync(
            provider.Issuer,
            provider.Subject,
            cancellationToken);

        // The address is read only when the subject did not match. FR-015 puts the subject first,
        // and a returning person's address has nothing left to decide.
        IReadOnlyList<UserRecord> candidates = subjectMatch is null
            ? await identityStore.FindAllByEmailAsync(email, cancellationToken)
            : [];

        var verdict = ClaimDecision.Decide(
            subjectMatch?.Id,
            [.. candidates.Select(candidate => new ClaimCandidate(candidate.Id, candidate.HasBoundIdentity))],
            provider.EmailVerified);

        switch (verdict.Outcome)
        {
            case ClaimOutcome.Returning:
                // Only what the provider owns is refreshed. Placement is left alone, because the CRM
                // owns it and what an administrator set survives every sign-in (spec FR-018).
                await identityStore.RefreshAsync(
                    subjectMatch!.Id,
                    email,
                    provider.DisplayName,
                    cancellationToken);

                return Resolution.Of(subjectMatch with { Email = email, DisplayName = Named(provider, email) });

            case ClaimOutcome.Claim:
                var claimed = await identityStore.ClaimAsync(
                    verdict.PersonId!.Value,
                    provider.Issuer,
                    provider.Subject,
                    email,
                    provider.DisplayName,
                    cancellationToken);

                await claims.RecordClaimedAsync(claimed.Id, email, cancellationToken);

                return Resolution.Of(claimed);

            case ClaimOutcome.CreateNew:
                // Placement is null at provisioning: the CRM owns it, and an administrator sets it
                // afterwards through the people screens (spec FR-018).
                var created = await identityStore.ProvisionAsync(
                    provider.Issuer,
                    provider.Subject,
                    email,
                    provider.DisplayName,
                    cancellationToken);

                return Resolution.Of(created);

            case ClaimOutcome.RefuseCollision:
                // An employee left and their address was reissued, or an account was duplicated.
                // Linking would hand the arriving person the previous holder's roles and history;
                // duplicating would split one person across two records. Only a human can tell
                // which, so the CRM asks (spec FR-018).
                logger.LogWarning(
                    "Sign-in refused: subject {Subject} presented an address already held by user {UserId}.",
                    provider.Subject,
                    verdict.PersonId);

                await events.RecordCollisionAsync(provider.Subject, verdict.PersonId!.Value, cancellationToken);
                await claims.RecordCollisionAsync(verdict.PersonId.Value, email, cancellationToken);

                return Resolution.Refusing(SignInRefusal.IdentityCollision);

            case ClaimOutcome.RefuseUnverified:
                // A record prepared for this address matched, and the provider did not confirm that
                // the person arriving actually holds the address. Creating an ordinary person beside
                // it is not available - the address is taken, and the filtered unique index means it
                // cannot be written twice - so this refuses and says why, which is also what tells
                // the administrator their preparation went unused (spec FR-017).
                logger.LogWarning(
                    "Sign-in refused: subject {Subject} matched a prepared record on an unverified address.",
                    provider.Subject);

                await events.RecordRefusalAsync(
                    "email_not_verified",
                    provider.Subject,
                    verdict.PersonId,
                    cancellationToken);

                await claims.RecordRefusedClaimAsync(
                    verdict.PersonId,
                    email,
                    ErrorCodes.IdentityEmailNotVerified,
                    cancellationToken);

                return Resolution.Refusing(SignInRefusal.EmailNotVerified);

            case ClaimOutcome.RefuseAmbiguous:
                logger.LogWarning(
                    "Sign-in refused: subject {Subject} matched more than one prepared record on one address.",
                    provider.Subject);

                await events.RecordRefusalAsync(
                    "email_ambiguous",
                    provider.Subject,
                    userId: null,
                    cancellationToken);

                await claims.RecordRefusedClaimAsync(
                    personId: null,
                    email,
                    ErrorCodes.IdentityEmailAmbiguous,
                    cancellationToken);

                return Resolution.Refusing(SignInRefusal.EmailAmbiguous);

            default:
                throw new InvalidOperationException($"Unhandled claim outcome '{verdict.Outcome}'.");
        }
    }

    /// <summary>
    /// Grants the bootstrap administrator role, or the configured default role, to a user who holds
    /// nothing yet. Neither ever re-grants access that was deliberately removed, because both apply
    /// only when the user has no assignment at all.
    /// </summary>
    /// <remarks>
    /// A claimed record usually holds roles already - that is the point of preparing one - so this
    /// leaves it alone. A record prepared with no roles falls through to the default, exactly as a
    /// person arriving unannounced does.
    /// </remarks>
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

    /// <summary>The same fallback the entity applies, so the in-memory copy agrees with the row.</summary>
    private static string Named(ProviderIdentity provider, string email) =>
        string.IsNullOrWhiteSpace(provider.DisplayName) ? email : provider.DisplayName;

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Whom the sign-in belongs to, or why it is refused. Exactly one of the two is set.</summary>
    private readonly record struct Resolution(UserRecord? User, SignInRefusal? Refusal)
    {
        public static Resolution Of(UserRecord user) => new(user, null);

        public static Resolution Refusing(SignInRefusal refusal) => new(null, refusal);
    }
}

/// <summary>Why a sign-in was refused. Each maps to a distinct, translatable error code.</summary>
public enum SignInRefusal
{
    NoAccess,
    Inactive,
    IdentityCollision,

    /// <summary>A prepared record matched and the provider did not confirm the address (FR-017).</summary>
    EmailNotVerified,

    /// <summary>More than one prepared record matched the address (FR-017).</summary>
    EmailAmbiguous,
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
