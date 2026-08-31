using Crm.Application.Identity.Claiming;
using Shouldly;

namespace Crm.UnitTests.Identity;

/// <summary>
/// Spec FR-015 to FR-019: every row of the claim matrix in data-model.md.
/// </summary>
/// <remarks>
/// A matrix rather than a handful of cases, because a wrongly claimed record looks exactly like a
/// correctly claimed one. Nothing here reaches a database: the decision is a pure function over
/// what the sign-in path found, which is the whole reason it was extracted from that path.
/// </remarks>
public sealed class ClaimDecisionTests
{
    private static readonly Guid Returning = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Prepared = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SomebodyElse = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void A_matched_subject_is_a_returning_person_and_the_email_is_not_consulted()
    {
        // The ordering FR-015 requires: subject first, always. Everything the email could say is
        // present here and none of it may change the answer.
        var verdict = ClaimDecision.Decide(
            subjectMatch: Returning,
            emailMatches: [new ClaimCandidate(SomebodyElse, HasBoundIdentity: true)],
            emailVerified: false);

        verdict.Outcome.ShouldBe(ClaimOutcome.Returning);
        verdict.PersonId.ShouldBe(Returning);
    }

    [Fact]
    public void One_unclaimed_record_and_a_verified_address_is_claimed()
    {
        var verdict = ClaimDecision.Decide(
            subjectMatch: null,
            emailMatches: [new ClaimCandidate(Prepared, HasBoundIdentity: false)],
            emailVerified: true);

        verdict.Outcome.ShouldBe(ClaimOutcome.Claim);

        // The prepared record, not a new one. FR-020: what was arranged in advance survives.
        verdict.PersonId.ShouldBe(Prepared);
    }

    [Fact]
    public void One_unclaimed_record_without_the_verified_assertion_is_refused()
    {
        // FR-017, as amended: refused rather than creating an ordinary person beside the record.
        // The address belongs to the prepared row and the filtered unique index means no second
        // live person can hold it, so refusing is the only reachable answer - and the one that
        // tells the administrator their preparation went unused.
        var verdict = ClaimDecision.Decide(
            subjectMatch: null,
            emailMatches: [new ClaimCandidate(Prepared, HasBoundIdentity: false)],
            emailVerified: false);

        verdict.Outcome.ShouldBe(ClaimOutcome.RefuseUnverified);

        // Named, so the audit entry can say which record went unclaimed.
        verdict.PersonId.ShouldBe(Prepared);
    }

    [Fact]
    public void More_than_one_unclaimed_record_is_ambiguous_even_when_the_address_is_verified()
    {
        // Unreachable through today's schema - one live person per address - and kept anyway. The
        // decision is a pure function over what it is handed, and the branch is what makes "pick
        // the first one" impossible to write by accident later.
        var verdict = ClaimDecision.Decide(
            subjectMatch: null,
            emailMatches:
            [
                new ClaimCandidate(Prepared, HasBoundIdentity: false),
                new ClaimCandidate(SomebodyElse, HasBoundIdentity: false),
            ],
            emailVerified: true);

        verdict.Outcome.ShouldBe(ClaimOutcome.RefuseAmbiguous);

        // No record is named, because naming one would suggest a choice was made between them.
        verdict.PersonId.ShouldBeNull();
    }

    [Fact]
    public void An_address_held_by_somebody_already_bound_refuses_the_sign_in()
    {
        // FR-018. Re-binding an established account from an email address is an account takeover
        // with extra steps, so this refuses rather than claiming, merging, or creating.
        var verdict = ClaimDecision.Decide(
            subjectMatch: null,
            emailMatches: [new ClaimCandidate(SomebodyElse, HasBoundIdentity: true)],
            emailVerified: true);

        verdict.Outcome.ShouldBe(ClaimOutcome.RefuseCollision);
        verdict.PersonId.ShouldBe(SomebodyElse);
    }

    [Fact]
    public void A_bound_record_beside_an_unclaimed_one_is_a_collision_rather_than_a_claim()
    {
        // Which refusal wins matters. Claiming the unclaimed row here would bind an identity while
        // an established account holding the same address sat beside it unresolved, which is the
        // ambiguity FR-018 wants a human to settle.
        var verdict = ClaimDecision.Decide(
            subjectMatch: null,
            emailMatches:
            [
                new ClaimCandidate(Prepared, HasBoundIdentity: false),
                new ClaimCandidate(SomebodyElse, HasBoundIdentity: true),
            ],
            emailVerified: true);

        verdict.Outcome.ShouldBe(ClaimOutcome.RefuseCollision);
        verdict.PersonId.ShouldBe(SomebodyElse);
    }

    [Fact]
    public void An_address_nobody_holds_creates_an_ordinary_person()
    {
        var verdict = ClaimDecision.Decide(
            subjectMatch: null,
            emailMatches: [],
            emailVerified: true);

        verdict.Outcome.ShouldBe(ClaimOutcome.CreateNew);
        verdict.PersonId.ShouldBeNull();
    }

    [Fact]
    public void An_unverified_address_that_nobody_holds_still_creates_an_ordinary_person()
    {
        // The verified assertion gates claiming somebody else's preparation, nothing else. Making
        // it a condition of signing in at all would lock out every deployment whose provider does
        // not send the claim.
        var verdict = ClaimDecision.Decide(
            subjectMatch: null,
            emailMatches: [],
            emailVerified: false);

        verdict.Outcome.ShouldBe(ClaimOutcome.CreateNew);
    }
}
