using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Crm.IntegrationTests.Infrastructure;
using Crm.IntegrationTests.Infrastructure.FakeOidc;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// User Story 2, driven end to end through the real handshake: what a first sign-in does with the
/// records it finds (spec FR-015 to FR-020, SC-004).
/// </summary>
/// <remarks>
/// The matrix is unit-tested in <c>ClaimDecisionTests</c>, where every branch is cheap. These tests
/// exist for the half a pure function cannot answer: whether the decision is actually reached, and
/// whether acting on it leaves the database in the state the decision described. A claim that
/// decided correctly and then wrote a second row would pass the unit tests and lose somebody's
/// preparation.
/// </remarks>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ClaimingTests(SqlServerFixture database)
{
    [Fact]
    public async Task A_verified_first_sign_in_claims_the_record_prepared_for_that_address()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var email = Address();
        var prepared = await harness.PreparePersonAsync(email, roleName: "Administrator", withBranch: true);

        var account = harness.Provider.AddAccount(email: email, emailVerified: true);

        var result = await harness.SignInAsync(account);

        result.Succeeded.ShouldBeTrue();

        // SC-004: the same person, not a second one beside them.
        (await harness.CountByEmailAsync(email)).ShouldBe(1);

        var person = await harness.GetPersonAsync(prepared.Id);
        person.ShouldNotBeNull();
        person.ProviderSubject.ShouldBe(account.Subject);
        person.Provider.ShouldBe(FakeOidcProvider.Issuer);

        // What was arranged in advance survives arriving, which is the entire point of arranging it.
        person.BranchId.ShouldBe(prepared.BranchId);
        (await harness.GetRoleNamesAsync(prepared.Id)).ShouldContain("Administrator");
    }

    [Fact]
    public async Task A_claim_does_not_hand_the_arriving_person_the_default_role_over_what_was_prepared()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var email = Address();
        var prepared = await harness.PreparePersonAsync(email, roleName: "Administrator");

        await harness.SignInAsync(harness.Provider.AddAccount(email: email));

        // The default role applies to somebody who holds nothing. Adding it here would quietly widen
        // a deliberate decision an administrator made in advance.
        (await harness.GetRoleNamesAsync(prepared.Id)).ShouldBe(["Administrator"]);
    }

    [Fact]
    public async Task An_unverified_address_claims_nothing_and_refuses_the_sign_in()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var email = Address();
        var prepared = await harness.PreparePersonAsync(email);

        var account = harness.Provider.AddAccount(email: email, emailVerified: false);

        var result = await harness.SignInAsync(account);

        // FR-017. The address belongs to the prepared record, so there is nowhere to put an ordinary
        // new person even if the product wanted one - and the refusal is what tells the
        // administrator their preparation went unused.
        result.Error.ShouldBe("identity_email_not_verified");
        result.RenewalCookie.ShouldBeNull();

        var person = await harness.GetPersonAsync(prepared.Id);
        person.ShouldNotBeNull();

        // Nothing partially claimed: the record is exactly as it was left.
        person.HasBoundIdentity.ShouldBeFalse();
        (await harness.CountByEmailAsync(email)).ShouldBe(1);

        var events = await harness.GetEventsAsync(account.Subject);
        events.ShouldHaveSingleItem().Action.ShouldBe(AuthenticationActions.SignInRefused);
    }

    [Fact]
    public async Task An_address_held_by_an_established_account_refuses_the_sign_in_outright()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var email = Address();
        var establishedId = await harness.SeedUserAsync("established|subject", email);

        // Somebody else authenticating with the same address. FR-018: an email is never grounds for
        // moving an account to a different identity.
        var account = harness.Provider.AddAccount(subject: "arriving|subject", email: email);

        var result = await harness.SignInAsync(account);

        result.Error.ShouldBe("identity_collision");
        result.RenewalCookie.ShouldBeNull();

        var established = await harness.GetPersonAsync(establishedId);
        established.ShouldNotBeNull();

        // Neither rebound nor duplicated. Both would be an account takeover with extra steps.
        established.ProviderSubject.ShouldBe("established|subject");
        (await harness.CountByEmailAsync(email)).ShouldBe(1);
    }

    [Fact]
    public async Task An_address_nobody_holds_creates_an_ordinary_person_as_before()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var account = harness.Provider.AddAccount(email: Address());

        var result = await harness.SignInAsync(account);

        result.Succeeded.ShouldBeTrue();

        // The path every user traversed before this feature existed, unchanged by it.
        (await harness.GetUserIdAsync(account.Subject)).ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_second_sign_in_is_recognised_by_subject_and_claims_nothing_again()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var email = Address();
        var prepared = await harness.PreparePersonAsync(email);
        var account = harness.Provider.AddAccount(email: email);

        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        // FR-015: subject before address. The second visit must not enter the claim path at all -
        // and INV-5 means it would throw rather than rebind if it did.
        var second = await harness.SignInAsync(account);

        second.Succeeded.ShouldBeTrue();

        (await harness.CountByEmailAsync(email)).ShouldBe(1);
        (await harness.GetPersonAsync(prepared.Id))!.ProviderSubject.ShouldBe(account.Subject);
    }

    [Fact]
    public async Task A_prepared_person_who_was_deactivated_is_claimed_and_still_refused()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var email = Address();
        var prepared = await harness.PreparePersonAsync(email, roleName: "Agent");

        await harness.WithServicesAsync(async services =>
        {
            var people = services.GetRequiredService<IPeopleStore>();

            return await people.SetActivationAsync(Guid.NewGuid(), prepared.Id, isActive: false);
        });

        var result = await harness.SignInAsync(harness.Provider.AddAccount(email: email));

        // The spec's own edge case: deactivation is an administrator's decision, and arriving does
        // not overturn it. The record is still claimed, so the person is not duplicated.
        result.Error.ShouldBe("no_access");

        var person = await harness.GetPersonAsync(prepared.Id);
        person.ShouldNotBeNull();
        person.HasBoundIdentity.ShouldBeTrue();
        person.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task The_same_subject_from_a_different_provider_is_a_different_person()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        const string sharedSubject = "shared|subject";

        // Issued by an entirely different directory, which happens to mint the same subject string.
        await harness.SeedUserAsync(
            sharedSubject,
            Address(),
            provider: SignInHarness.TestProvider);

        var account = harness.Provider.AddAccount(subject: sharedSubject, email: Address());

        var result = await harness.SignInAsync(account);

        // FR-015a. Matching on the subject alone is how one person silently becomes another the day
        // a second identity provider is configured.
        result.Succeeded.ShouldBeTrue();

        var arrived = await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IIdentityStore>();

            return await store.FindBySubjectAsync(FakeOidcProvider.Issuer, sharedSubject);
        });

        arrived.ShouldNotBeNull();
        arrived.Email.ShouldBe(account.Email);
    }

    [Fact]
    public async Task Somebody_bound_before_the_provider_was_recorded_still_signs_in()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var email = Address();

        // Exactly what every row written before the IdentityAdministration migration looks like.
        var legacyId = await harness.SeedLegacyUserAsync("legacy|subject", email);

        var result = await harness.SignInAsync(
            harness.Provider.AddAccount(subject: "legacy|subject", email: email));

        // Without the fallback this refuses with identity_collision: the composite lookup cannot
        // match a NULL provider, so the address lookup finds the person's own bound record and reads
        // it as somebody else's. Every existing user would be locked out by their own row, and told
        // an administrator must resolve it - including the administrator.
        result.Succeeded.ShouldBeTrue();

        var person = await harness.GetPersonAsync(legacyId);
        person.ShouldNotBeNull();

        // Healed in passing, once. The next visit matches on the pair like everybody else.
        person.Provider.ShouldBe(FakeOidcProvider.Issuer);
        person.ProviderSubject.ShouldBe("legacy|subject");

        (await harness.CountByEmailAsync(email)).ShouldBe(1);
    }

    [Fact]
    public async Task An_exact_provider_match_is_preferred_over_a_row_that_records_none()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        const string sharedSubject = "contested|subject";

        // A legacy row and a properly bound row carrying the same subject. The bound one is the
        // person arriving; adopting the legacy row instead would hand them somebody else's account.
        await harness.SeedLegacyUserAsync(sharedSubject, Address());

        var boundEmail = Address();
        var boundId = await harness.SeedUserAsync(sharedSubject, boundEmail);

        var result = await harness.SignInAsync(
            harness.Provider.AddAccount(subject: sharedSubject, email: boundEmail));

        result.Succeeded.ShouldBeTrue();

        var arrived = await harness.WithServicesAsync(async services =>
            await services.GetRequiredService<IIdentityStore>()
                .FindBySubjectAsync(FakeOidcProvider.Issuer, sharedSubject));

        arrived!.Id.ShouldBe(boundId);
    }

    private static string Address() => $"{Guid.CreateVersion7():n}@prepared.local";
}
