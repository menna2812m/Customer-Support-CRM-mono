using Crm.Application.Authorization;
using Crm.Application.Common;
using Crm.Application.Diagnostics;
using FluentValidation.TestHelper;
using Shouldly;

namespace Crm.UnitTests.Authorization;

/// <summary>
/// Spec FR-024 and AR-005: the catalog is the single source of truth, it can express the
/// permission names the constitution names, and it is enumerable so a later feature can seed from
/// it without redefining the list.
/// </summary>
public sealed class PermissionCatalogTests
{
    [Theory]
    [InlineData("customers.view")]
    [InlineData("customers.create")]
    [InlineData("customers.update")]
    [InlineData("tickets.view")]
    [InlineData("tickets.create")]
    [InlineData("tickets.assign")]
    [InlineData("tickets.escalate")]
    [InlineData("users.manage")]
    [InlineData("reports.view")]
    public void The_catalog_expresses_every_permission_named_in_the_constitution(string permission)
    {
        Permissions.Exists(permission).ShouldBeTrue();
    }

    /// <summary>
    /// Feature 003. Reading the structure is separated from maintaining it, so that feature 004 can
    /// let most staff see where their work sits without letting them reorganize the business.
    /// </summary>
    [Theory]
    [InlineData("organization.view")]
    [InlineData("organization.manage")]
    public void The_catalog_expresses_the_organization_permissions(string permission)
    {
        Permissions.Exists(permission).ShouldBeTrue();
    }

    /// <summary>
    /// Feature 004. Reading people is separated from administering them for the same reason as the
    /// organization pair: seeing who sits where is ordinary, while granting somebody a role is not.
    /// </summary>
    [Theory]
    [InlineData("identity.view")]
    [InlineData("identity.manage")]
    public void The_catalog_expresses_the_identity_permissions(string permission)
    {
        Permissions.Exists(permission).ShouldBeTrue();
    }

    [Fact]
    public void Every_permission_follows_the_area_dot_action_convention()
    {
        foreach (var permission in Permissions.All)
        {
            permission.ShouldMatch(@"^[a-z]+\.[a-z]+$");
        }
    }

    [Fact]
    public void The_catalog_is_enumerable_and_free_of_duplicates()
    {
        // Enumerability is what lets the users-and-permissions feature seed role assignments.
        Permissions.All.Count.ShouldBeGreaterThan(9);
        Permissions.All.Distinct(StringComparer.Ordinal).Count().ShouldBe(Permissions.All.Count);
    }

    [Fact]
    public void An_unknown_permission_is_not_recognised()
    {
        Permissions.Exists("tickets.deleteEverything").ShouldBeFalse();
    }
}

/// <summary>
/// Validation-failure tests for the reference slice (spec FR-047). Each rule carries a stable
/// error code, because clients switch on the code rather than the message.
/// </summary>
public sealed class EchoRequestValidatorTests
{
    private readonly EchoRequestValidator _validator = new();

    [Fact]
    public void An_empty_message_is_required()
    {
        var result = _validator.TestValidate(new EchoRequest { Message = string.Empty });

        result.ShouldHaveValidationErrorFor(request => request.Message)
            .WithErrorCode(ErrorCodes.Field.Required);
    }

    [Fact]
    public void An_overlong_message_is_rejected_with_the_max_length_code()
    {
        var request = new EchoRequest
        {
            Message = new string('x', EchoRequestValidator.MaxMessageLength + 1),
        };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.Message)
            .WithErrorCode(ErrorCodes.Field.MaxLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void A_repeat_count_outside_the_range_is_rejected(int repeatCount)
    {
        var request = new EchoRequest { Message = "ok", RepeatCount = repeatCount };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.RepeatCount)
            .WithErrorCode(ErrorCodes.Field.Range);
    }

    [Fact]
    public void A_valid_request_passes()
    {
        var result = _validator.TestValidate(new EchoRequest { Message = "ok", RepeatCount = 3 });

        result.IsValid.ShouldBeTrue();
    }
}

/// <summary>Sorting is allow-listed per endpoint (pagination contract).</summary>
public sealed class PageRequestRulesTests
{
    private static readonly IReadOnlySet<string> Sortable =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "createdAt" };

    [Theory]
    [InlineData("name")]
    [InlineData("-createdAt")]
    [InlineData(null)]
    public void An_allowed_field_produces_no_failure(string? sort)
    {
        PageRequestRules.ValidateSort(new PageRequest { Sort = sort }, Sortable).ShouldBeEmpty();
    }

    [Fact]
    public void An_unlisted_field_is_rejected_rather_than_ignored()
    {
        var failures = PageRequestRules.ValidateSort(
            new PageRequest { Sort = "-passwordHash" },
            Sortable);

        failures.Count.ShouldBe(1);
        failures[0].Field.ShouldBe("sort");
        failures[0].Code.ShouldBe(ErrorCodes.Field.NotSortable);

        // The message names what IS allowed, so an integrator can fix it without reading source.
        failures[0].Message.ShouldContain("name");
    }
}
