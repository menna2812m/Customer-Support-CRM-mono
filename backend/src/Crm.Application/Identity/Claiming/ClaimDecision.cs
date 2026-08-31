namespace Crm.Application.Identity.Claiming;

/// <summary>
/// What a first sign-in does with the records it found (spec FR-015 to FR-019).
/// </summary>
/// <remarks>
/// One function over the whole matrix in data-model.md, rather than a sequence of checks spread
/// through the sign-in path. The reason is the failure mode: a record claimed by the wrong person
/// looks exactly like a record claimed by the right one, and nothing downstream will ever notice.
/// A reader needs every branch in front of them at once to be sure none of them is wrong.
///
/// It is pure on purpose. It reads nothing and writes nothing, so the matrix can be tested
/// exhaustively without a database, and so the caller cannot accidentally act on half of it.
/// </remarks>
public static class ClaimDecision
{
    /// <param name="subjectMatch">The person already bound to this provider and subject, if any.</param>
    /// <param name="emailMatches">Every person, bound or not, holding the normalized address.</param>
    /// <param name="emailVerified">
    /// Whether the provider asserted the address is verified. An absent assertion is false: FR-016
    /// requires a positive claim, and treating silence as verification would let anyone who can
    /// authenticate any address inherit whatever was prepared for it.
    /// </param>
    public static ClaimVerdict Decide(
        Guid? subjectMatch,
        IReadOnlyList<ClaimCandidate> emailMatches,
        bool emailVerified)
    {
        ArgumentNullException.ThrowIfNull(emailMatches);

        // Subject first, before the email is looked at at all (FR-015). An established identity is
        // recognised by what the provider minted for it, never by an address that can be reassigned.
        if (subjectMatch is { } returning)
        {
            return new ClaimVerdict(ClaimOutcome.Returning, returning);
        }

        // A bound holder outranks every other row. Claiming an unclaimed record while an
        // established account held the same address would leave the collision FR-018 wants a human
        // to settle sitting unresolved beside a freshly bound identity.
        if (emailMatches.FirstOrDefault(candidate => candidate.HasBoundIdentity) is { } bound)
        {
            return new ClaimVerdict(ClaimOutcome.RefuseCollision, bound.PersonId);
        }

        if (emailMatches.Count == 0)
        {
            return new ClaimVerdict(ClaimOutcome.CreateNew, PersonId: null);
        }

        if (emailMatches.Count > 1)
        {
            // No record is named, because naming one would imply a choice was made between them.
            return new ClaimVerdict(ClaimOutcome.RefuseAmbiguous, PersonId: null);
        }

        var prepared = emailMatches[0];

        // Refused rather than creating an ordinary person beside the prepared record (FR-017, as
        // amended). The address belongs to that record, and `UNIQUE (Email) WHERE IsDeleted = 0`
        // means no second live person can hold it - so refusing is not a preference, it is the only
        // outcome the schema permits. It is also the informative one: the administrator who
        // prepared the address finds out their preparation was not picked up.
        return emailVerified
            ? new ClaimVerdict(ClaimOutcome.Claim, prepared.PersonId)
            : new ClaimVerdict(ClaimOutcome.RefuseUnverified, prepared.PersonId);
    }
}

/// <summary>A person the address lookup found, reduced to what the decision needs.</summary>
/// <param name="HasBoundIdentity">
/// False means prepared and not yet arrived - the only kind of record a first sign-in may claim.
/// </param>
public sealed record ClaimCandidate(Guid PersonId, bool HasBoundIdentity);

/// <summary>One row of the matrix.</summary>
public enum ClaimOutcome
{
    /// <summary>Recognised by provider and subject. Sign-in proceeds unchanged.</summary>
    Returning = 0,

    /// <summary>Bind this person's identity and keep the roles and placement prepared for them.</summary>
    Claim = 1,

    /// <summary>Nobody holds the address. Create an ordinary person, as before this feature.</summary>
    CreateNew = 2,

    /// <summary>The address belongs to an established account - <c>identity_subject_collision</c>.</summary>
    RefuseCollision = 3,

    /// <summary>A prepared record matched, unverified - <c>identity_email_not_verified</c>.</summary>
    RefuseUnverified = 4,

    /// <summary>Several prepared records matched - <c>identity_email_ambiguous</c>.</summary>
    RefuseAmbiguous = 5,
}

/// <param name="PersonId">
/// Whom the outcome concerns: the person to sign in, to claim, or to name in the audit entry. Null
/// where no single record is involved.
/// </param>
public sealed record ClaimVerdict(ClaimOutcome Outcome, Guid? PersonId);
