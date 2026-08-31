using Crm.Domain.Identity;
using Shouldly;

namespace Crm.UnitTests.Identity;

/// <summary>
/// Spec FR-013, FR-014, FR-020: somebody can be arranged before they arrive, and arriving keeps
/// what was arranged.
/// </summary>
/// <remarks>
/// The refusals that belong to an address already in use are not here, and cannot be: uniqueness
/// among people who are not deleted is a property of the filtered index, not of an object in
/// memory. Those are proven where they live, in <c>Identity/SchemaTests</c> and
/// <c>Identity/PreProvisionEndpointTests</c>. What is testable here is the half that matters most
/// on the day somebody starts: that a claim does not quietly undo the preparation.
/// </remarks>
public sealed class PreProvisionTests
{
    private const string Provider = "https://idp.example/realms/crm";

    [Fact]
    public void A_prepared_person_can_be_placed_before_they_have_an_identity()
    {
        var branch = Guid.NewGuid();
        var team = Guid.NewGuid();
        var department = Guid.NewGuid();

        var person = User.PreProvision("noor@example.com", "Noor Abdullah", population: 1);

        person.Place(branch, departmentId: null, new TeamPlacement(team, department));

        // The whole point of preparing somebody: on their first day the CRM already knows where
        // they sit, rather than an administrator being paged to say so.
        person.HasBoundIdentity.ShouldBeFalse();
        person.BranchId.ShouldBe(branch);
        person.TeamId.ShouldBe(team);
        person.DepartmentId.ShouldBe(department);
    }

    [Fact]
    public void Claiming_a_prepared_person_keeps_the_placement_arranged_for_them()
    {
        var branch = Guid.NewGuid();
        var department = Guid.NewGuid();

        var person = User.PreProvision("noor@example.com", "Noor Abdullah", population: 1);
        person.Place(branch, department, team: null);

        person.BindIdentity(Provider, "subject-77");
        person.RefreshFromProvider("noor@example.com", "Noor Abdullah");

        // FR-020. A claim that reset placement would be worse than no preparation at all: the
        // administrator would believe the arrangement held, and nothing would say otherwise.
        person.BranchId.ShouldBe(branch);
        person.DepartmentId.ShouldBe(department);
    }

    [Fact]
    public void A_claim_records_the_provider_alongside_the_subject()
    {
        var person = User.PreProvision("noor@example.com", "Noor Abdullah", population: 1);

        person.BindIdentity(Provider, "subject-77");

        // FR-015a: the subject alone is not an identity, because a second provider may issue the
        // same string to somebody else entirely.
        person.Provider.ShouldBe(Provider);
        person.ProviderSubject.ShouldBe("subject-77");
    }

    [Fact]
    public void A_prepared_person_takes_their_address_as_a_name_when_none_was_given()
    {
        // The list has to show something, and an empty cell beside an address nobody recognises is
        // how a prepared person becomes invisible on the screen meant to surface them.
        var person = User.PreProvision("Noor@Example.com", displayName: "  ", population: 1);

        person.DisplayName.ShouldBe("noor@example.com");
    }
}
