using Crm.Domain.Identity;
using Shouldly;

namespace Crm.UnitTests.Identity;

/// <summary>
/// Spec FR-013, FR-015a, FR-019 and INV-5: a person may exist before their identity does, the
/// identity is the provider together with the subject, and once bound it is never rebound.
/// </summary>
public sealed class UserIdentityTests
{
    private const string Provider = "https://idp.example/realms/crm";

    [Fact]
    public void A_prepared_person_exists_with_no_bound_identity()
    {
        var person = User.PreProvision("Layla@Example.com ", "Layla Hassan", population: 1);

        // Null rather than an empty string: absent is a state, not a value that happens to be short.
        person.ProviderSubject.ShouldBeNull();
        person.Provider.ShouldBeNull();
        person.HasBoundIdentity.ShouldBeFalse();

        // The address is the one attribute that can match this person before they have an identity,
        // so it is normalized on the way in rather than at every comparison afterwards.
        person.Email.ShouldBe("layla@example.com");
    }

    [Fact]
    public void Binding_an_identity_records_the_provider_and_the_subject_together()
    {
        var person = User.PreProvision("layla@example.com", "Layla Hassan", population: 1);

        person.BindIdentity(Provider, "subject-1");

        person.Provider.ShouldBe(Provider);
        person.ProviderSubject.ShouldBe("subject-1");
        person.HasBoundIdentity.ShouldBeTrue();
    }

    [Fact]
    public void Binding_a_person_who_already_has_an_identity_is_refused()
    {
        var person = User.PreProvision("layla@example.com", "Layla Hassan", population: 1);
        person.BindIdentity(Provider, "subject-1");

        // INV-5. Rebinding an established account is how an email address becomes an account
        // takeover, so the domain refuses it even though the sign-in path refuses it first.
        Should.Throw<InvalidOperationException>(() => person.BindIdentity(Provider, "subject-2"));

        person.ProviderSubject.ShouldBe("subject-1");
    }

    [Fact]
    public void A_person_provisioned_by_sign_in_is_bound_immediately()
    {
        var person = User.Provision(
            Provider,
            "subject-9",
            "new@example.com",
            "New Person",
            population: 1,
            OrganizationPlacement.None);

        person.HasBoundIdentity.ShouldBeTrue();
        person.Provider.ShouldBe(Provider);
    }
}

/// <summary>
/// Spec FR-010 and FR-011, INV-2: a person's department follows their team. Feature 003 owns the
/// other half of this invariant, resyncing members when a team moves.
/// </summary>
public sealed class UserPlacementTests
{
    private static User Person() => User.PreProvision("p@example.com", "Person", population: 1);

    [Fact]
    public void Placing_someone_on_a_team_takes_the_department_from_that_team()
    {
        var branch = Guid.NewGuid();
        var team = Guid.NewGuid();
        var department = Guid.NewGuid();

        var person = Person();
        person.Place(branch, departmentId: null, new TeamPlacement(team, department));

        person.BranchId.ShouldBe(branch);
        person.TeamId.ShouldBe(team);

        // Never chosen separately - derived, which is what makes INV-2 unbreakable here.
        person.DepartmentId.ShouldBe(department);
    }

    [Fact]
    public void A_department_that_disagrees_with_the_team_is_refused_rather_than_overwritten()
    {
        var team = Guid.NewGuid();
        var teamDepartment = Guid.NewGuid();
        var somewhereElse = Guid.NewGuid();

        var person = Person();

        // Quietly storing the team's department would hide a caller's bug. The application layer
        // refuses this first with identity_placement_mismatch; the domain refuses it as well.
        Should.Throw<InvalidOperationException>(
            () => person.Place(null, somewhereElse, new TeamPlacement(team, teamDepartment)));
    }

    [Fact]
    public void A_department_may_be_chosen_directly_when_there_is_no_team()
    {
        var department = Guid.NewGuid();

        var person = Person();
        person.Place(null, department, team: null);

        person.DepartmentId.ShouldBe(department);
        person.TeamId.ShouldBeNull();
    }

    [Fact]
    public void Placing_with_nothing_clears_the_placement()
    {
        var person = Person();
        person.Place(Guid.NewGuid(), null, new TeamPlacement(Guid.NewGuid(), Guid.NewGuid()));

        person.Place(null, null, null);

        person.BranchId.ShouldBeNull();
        person.DepartmentId.ShouldBeNull();
        person.TeamId.ShouldBeNull();
    }

    [Fact]
    public void Clearing_the_team_leaves_the_department_selectable_again()
    {
        var department = Guid.NewGuid();
        var person = Person();
        person.Place(null, null, new TeamPlacement(Guid.NewGuid(), Guid.NewGuid()));

        person.Place(null, department, team: null);

        person.TeamId.ShouldBeNull();
        person.DepartmentId.ShouldBe(department);
    }
}
