using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Shouldly;

namespace Crm.UnitTests.Identity;

/// <summary>
/// Spec FR-004, FR-005, FR-026: identity is keyed on the provider's subject, and what the provider
/// owns is refreshed on every visit without disturbing what it does not.
/// </summary>
public sealed class UserProvisioningTests
{
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Branch = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_returning_user_keeps_their_identifier_while_name_and_email_are_refreshed()
    {
        var user = User.Provision(
            "provider|stable-subject",
            "layla.hassan@example.com",
            "Layla Hassan",
            (int)CallerPopulation.Staff,
            new OrganizationPlacement(Department, null, null));

        var identifier = user.Id;

        // She married and changed her name, and the provider knows both.
        user.RefreshFromProvider("layla.saeed@example.com", "Layla Saeed");

        // The identifier is what every ticket, note, and audit record already points at. Changing
        // it would orphan her history, which is exactly why the subject - not the email - is the key.
        user.Id.ShouldBe(identifier);
        user.ProviderSubject.ShouldBe("provider|stable-subject");
        user.Email.ShouldBe("layla.saeed@example.com");
        user.DisplayName.ShouldBe("Layla Saeed");
    }

    [Fact]
    public void Refreshing_from_the_provider_never_touches_placement()
    {
        // Feature 003 (spec FR-018) gave the CRM sole ownership of placement. Feature 002 let a
        // provider-asserted value overwrite it on every sign-in; once placement became a foreign key
        // to real records, a provider-asserted identifier could only ever be a constraint violation.
        // What an administrator sets now survives every subsequent sign-in.
        var user = User.Provision(
            "provider|subject",
            "agent@example.com",
            "Agent",
            (int)CallerPopulation.Staff,
            new OrganizationPlacement(Department, Branch, null));

        user.RefreshFromProvider("agent@example.com", "Agent");

        user.DepartmentId.ShouldBe(Department);
        user.BranchId.ShouldBe(Branch);
    }

    [Theory]
    [InlineData("  Layla.Hassan@Example.COM  ")]
    [InlineData("LAYLA.HASSAN@EXAMPLE.COM")]
    public void Email_is_normalized_so_two_spellings_cannot_become_two_users(string email)
    {
        var user = User.Provision(
            "provider|subject",
            email,
            "Layla",
            (int)CallerPopulation.Staff,
            OrganizationPlacement.None);

        // The uniqueness constraint that detects a collision is only as good as this normalization.
        user.Email.ShouldBe("layla.hassan@example.com");
    }

    [Fact]
    public void A_provider_that_sends_no_display_name_falls_back_to_the_address_rather_than_blank()
    {
        var user = User.Provision(
            "provider|subject",
            "agent@example.com",
            "   ",
            (int)CallerPopulation.Staff,
            OrganizationPlacement.None);

        user.DisplayName.ShouldBe("agent@example.com");
    }
}
