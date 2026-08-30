using Crm.Application.Identity.People;
using Shouldly;

namespace Crm.UnitTests.Identity;

/// <summary>
/// Spec FR-028 and FR-029: the system keeps at least one active administrator, and nobody demotes
/// themselves.
/// </summary>
/// <remarks>
/// The guard is a decision over facts rather than a query, so it is tested without a database or a
/// mock. The facts it needs - whether the target holds the role, and how many other active
/// administrators remain - are gathered by the store inside the serializable transaction that makes
/// the answer still true by the time it is acted on (research decision 5). Separating the two is
/// what lets the rule be tested exhaustively and the isolation be tested once.
/// </remarks>
public sealed class AdministratorGuardTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Someone = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Removing_the_last_active_administrator_is_refused()
    {
        var change = new AdministratorChange(Actor, Someone, TargetHoldsAdministrator: true, OtherActiveAdministrators: 0);

        AdministratorGuard.Check(change).ShouldBe(AdministratorGuardResult.LastAdministrator);
    }

    [Fact]
    public void Removing_an_administrator_while_another_remains_is_allowed()
    {
        var change = new AdministratorChange(Actor, Someone, TargetHoldsAdministrator: true, OtherActiveAdministrators: 1);

        AdministratorGuard.Check(change).ShouldBe(AdministratorGuardResult.Allowed);
    }

    [Fact]
    public void Acting_on_your_own_account_is_refused_even_when_others_remain()
    {
        var change = new AdministratorChange(Actor, Actor, TargetHoldsAdministrator: true, OtherActiveAdministrators: 5);

        // The last-administrator rule would permit this. Somebody removing their own access by
        // mistake is the common failure, and it is unrecoverable through the interface.
        AdministratorGuard.Check(change).ShouldBe(AdministratorGuardResult.SelfDemotion);
    }

    [Fact]
    public void The_last_administrator_acting_on_themselves_is_told_the_stronger_reason()
    {
        var change = new AdministratorChange(Actor, Actor, TargetHoldsAdministrator: true, OtherActiveAdministrators: 0);

        // Both rules refuse. "Another administrator must do this" is useless advice when there is
        // no other administrator, so the reason that is actually true is the one reported.
        AdministratorGuard.Check(change).ShouldBe(AdministratorGuardResult.LastAdministrator);
    }

    [Fact]
    public void Acting_on_somebody_who_is_not_an_administrator_is_allowed()
    {
        var change = new AdministratorChange(Actor, Someone, TargetHoldsAdministrator: false, OtherActiveAdministrators: 0);

        // Nothing to protect: removing a non-administrator cannot reduce the administrator count.
        AdministratorGuard.Check(change).ShouldBe(AdministratorGuardResult.Allowed);
    }

    [Fact]
    public void Acting_on_your_own_account_is_refused_even_when_you_are_not_an_administrator()
    {
        var change = new AdministratorChange(Actor, Actor, TargetHoldsAdministrator: false, OtherActiveAdministrators: 3);

        // Deactivating or deleting your own account locks you out whether or not you administer
        // anything, so the self rule does not depend on the role.
        AdministratorGuard.Check(change).ShouldBe(AdministratorGuardResult.SelfDemotion);
    }
}
