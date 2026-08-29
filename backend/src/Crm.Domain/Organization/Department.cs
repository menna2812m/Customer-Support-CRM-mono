namespace Crm.Domain.Organization;

/// <summary>
/// A functional division of the business. Contains teams (spec FR-002).
/// </summary>
public sealed class Department : OrganizationUnit
{
    private readonly List<Team> _teams = [];

    private Department() { }

    /// <summary>
    /// The teams belonging to this department. Read-only to callers: a team joins a department by
    /// being created in it or moved to it, never by being added to this list.
    /// </summary>
    public IReadOnlyCollection<Team> Teams => _teams.AsReadOnly();

    public static Department Create(string nameAr, string nameEn, string code)
    {
        var department = new Department();
        department.Initialize(nameAr, nameEn, code);

        return department;
    }
}
