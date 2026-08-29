using Crm.Domain.Organization;
using Shouldly;

namespace Crm.UnitTests.Organization;

/// <summary>
/// The rules the entities enforce on their own, without a store or a database. Spec FR-005
/// (both names required), FR-006 (a code never changes), FR-016 (no move into an inactive
/// department).
/// </summary>
public sealed class OrganizationUnitTests
{
    [Fact]
    public void Names_and_codes_are_trimmed_on_write()
    {
        var department = Department.Create("  الدعم الفني  ", "  Technical Support  ", "  TS  ");

        department.NameAr.ShouldBe("الدعم الفني");
        department.NameEn.ShouldBe("Technical Support");
        department.Code.ShouldBe("TS");
    }

    [Theory]
    [InlineData("", "English")]
    [InlineData("   ", "English")]
    [InlineData("عربي", "")]
    [InlineData("عربي", "   ")]
    public void Both_names_are_required(string nameAr, string nameEn)
    {
        // A unit created in one language and "completed later" in the other is the half-translated
        // state Constitution VII exists to prevent, so it is refused at construction.
        Should.Throw<ArgumentException>(() => Branch.Create(nameAr, nameEn, "CODE"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_code_is_required(string code)
    {
        Should.Throw<ArgumentException>(() => Branch.Create("عربي", "English", code));
    }

    [Fact]
    public void A_code_cannot_be_changed_after_creation()
    {
        // Enforced structurally rather than by a rule somebody must remember: the property has no
        // accessible setter, and Rename deliberately takes only the two names.
        typeof(OrganizationUnit)
            .GetProperty(nameof(OrganizationUnit.Code))!
            .SetMethod!.IsPrivate.ShouldBeTrue();

        var branch = Branch.Create("عربي", "English", "RUH");
        branch.Rename("جدة", "Jeddah");

        branch.Code.ShouldBe("RUH");
    }

    [Fact]
    public void Renaming_changes_both_names_together()
    {
        var branch = Branch.Create("الرياض", "Riyadh", "RUH");

        branch.Rename("جدة", "Jeddah");

        branch.NameAr.ShouldBe("جدة");
        branch.NameEn.ShouldBe("Jeddah");
    }

    [Fact]
    public void A_unit_is_active_when_created_and_can_be_retired_and_restored()
    {
        var branch = Branch.Create("الرياض", "Riyadh", "RUH");
        branch.IsActive.ShouldBeTrue();

        branch.Deactivate();
        branch.IsActive.ShouldBeFalse();

        branch.Activate();
        branch.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void A_team_belongs_to_the_department_it_was_created_in()
    {
        var department = Department.Create("الدعم الفني", "Technical Support", "TS");

        var team = Team.Create(department, "المستوى الأول", "Tier 1", "TS-T1");

        team.DepartmentId.ShouldBe(department.Id);
    }

    [Fact]
    public void A_team_cannot_be_created_without_a_department()
    {
        Should.Throw<ArgumentNullException>(
            () => Team.Create(null!, "المستوى الأول", "Tier 1", "TS-T1"));
    }

    [Fact]
    public void Moving_a_team_changes_the_department_it_belongs_to()
    {
        var origin = Department.Create("الدعم الفني", "Technical Support", "TS");
        var destination = Department.Create("الفوترة", "Billing", "BIL");
        var team = Team.Create(origin, "المستوى الثاني", "Tier 2", "TS-T2");

        team.MoveTo(destination);

        team.DepartmentId.ShouldBe(destination.Id);
    }

    [Fact]
    public void Moving_a_team_into_an_inactive_department_is_refused()
    {
        var origin = Department.Create("الدعم الفني", "Technical Support", "TS");
        var destination = Department.Create("الفوترة", "Billing", "BIL");
        destination.Deactivate();
        var team = Team.Create(origin, "المستوى الثاني", "Tier 2", "TS-T2");

        Should.Throw<InvalidOperationException>(() => team.MoveTo(destination));

        team.DepartmentId.ShouldBe(origin.Id);
    }

    [Fact]
    public void Moving_a_team_to_the_department_it_is_already_in_changes_nothing()
    {
        // Accepted rather than refused: re-submitting a move is not a mistake worth an error.
        var department = Department.Create("الدعم الفني", "Technical Support", "TS");
        var team = Team.Create(department, "المستوى الأول", "Tier 1", "TS-T1");

        team.MoveTo(department);

        team.DepartmentId.ShouldBe(department.Id);
    }

    [Fact]
    public void An_inactive_department_still_accepts_a_move_to_itself()
    {
        // The no-op check comes first deliberately. A team already in a department that was later
        // deactivated must not become impossible to re-submit.
        var department = Department.Create("الفوترة", "Billing", "BIL");
        var team = Team.Create(department, "التحصيل", "Collections", "BIL-C");
        department.Deactivate();

        Should.NotThrow(() => team.MoveTo(department));
    }
}
