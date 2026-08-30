namespace Crm.Domain.Organization;

/// <summary>
/// A geographic location the organization operates from. Branches stand alone: no branch contains a
/// department, and no department belongs to a branch (spec FR-003).
/// </summary>
/// <remarks>
/// Keeping geography orthogonal to function is what lets one Billing department serve every city
/// without being duplicated per city - the assumption Constitution V exists to prevent.
/// </remarks>
public sealed class Branch : OrganizationUnit
{
    private Branch() { }

    public static Branch Create(string nameAr, string nameEn, string code)
    {
        var branch = new Branch();
        branch.Initialize(nameAr, nameEn, code);

        return branch;
    }
}
