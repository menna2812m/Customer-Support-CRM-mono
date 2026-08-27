using Crm.Application.Authorization;
using Crm.Application.Identity;
using Shouldly;

namespace Crm.UnitTests.Identity;

/// <summary>
/// The rule that decides what a session may do (spec FR-021).
///
/// Worth testing away from the database, because the failure modes are about the rule rather than
/// about storage: roles that should add up but do not, a duplicate that changes the answer, and a
/// name nobody declares that nonetheless ends up in a credential.
/// </summary>
public sealed class EffectivePermissionTests
{
    [Fact]
    public void Roles_add_up_rather_than_overriding_each_other()
    {
        var resolved = EffectivePermissions.Resolve(
        [
            // As if from two roles: day-to-day work, and reporting.
            Permissions.Tickets.View,
            Permissions.Tickets.Create,
            Permissions.Reports.View,
        ]);

        resolved.Permissions.ShouldBe(
            new[] { Permissions.Tickets.View, Permissions.Tickets.Create, Permissions.Reports.View },
            ignoreOrder: true);
    }

    [Fact]
    public void A_permission_granted_by_two_roles_is_held_once()
    {
        var resolved = EffectivePermissions.Resolve(
        [
            Permissions.Customers.View,
            Permissions.Customers.View,
            Permissions.Tickets.View,
        ]);

        // Not a cosmetic detail: each permission becomes a claim, and duplicates would inflate
        // every credential the user is ever issued.
        resolved.Permissions.Count.ShouldBe(2);
    }

    [Fact]
    public void A_name_the_catalog_does_not_declare_grants_nothing()
    {
        var resolved = EffectivePermissions.Resolve([Permissions.Tickets.View, "tickets.delete"]);

        resolved.Permissions.ShouldBe([Permissions.Tickets.View]);

        // Reported rather than dropped in silence - a renamed permission is otherwise invisible
        // until somebody cannot do their job and nobody can say why.
        resolved.Unknown.ShouldBe(["tickets.delete"]);
    }

    [Fact]
    public void A_user_with_no_roles_holds_nothing()
    {
        var resolved = EffectivePermissions.Resolve([]);

        // Distinct from a failure: authenticated, recognised, and granted nothing is the state the
        // no-access screen exists to explain.
        resolved.Permissions.ShouldBeEmpty();
        resolved.Unknown.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_grant_is_ignored_rather_than_treated_as_a_permission(string blank)
    {
        var resolved = EffectivePermissions.Resolve([Permissions.Tickets.View, blank]);

        resolved.Permissions.ShouldBe([Permissions.Tickets.View]);
        resolved.Unknown.ShouldBeEmpty();
    }

    [Fact]
    public void Comparison_is_case_sensitive_so_a_near_miss_is_not_quietly_accepted()
    {
        var resolved = EffectivePermissions.Resolve(["Tickets.View"]);

        // The catalog declares lowercase names, and the authorization handler compares them
        // exactly. Accepting a different casing here would grant something the handler then
        // refuses, which is worse than refusing consistently.
        resolved.Permissions.ShouldBeEmpty();
        resolved.Unknown.ShouldBe(["Tickets.View"]);
    }
}
