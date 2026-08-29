namespace Crm.Domain.Organization;

/// <summary>
/// A working group inside exactly one department (spec FR-002). A team cannot exist without one,
/// which is why the department is a constructor argument rather than a property to fill in later.
/// </summary>
public sealed class Team : OrganizationUnit
{
    private Team() { }

    /// <summary>The department this team belongs to. Never null (INV-1).</summary>
    public Guid DepartmentId { get; private set; }

    public static Team Create(Department department, string nameAr, string nameEn, string code)
    {
        ArgumentNullException.ThrowIfNull(department);

        var team = new Team { DepartmentId = department.Id };
        team.Initialize(nameAr, nameEn, code);

        return team;
    }

    /// <summary>
    /// Moves this team to another department (spec FR-014). Refuses an inactive destination
    /// (FR-016), and treats a move to the current department as a no-op rather than an error.
    /// </summary>
    /// <remarks>
    /// Moving the team is only half the operation. Every member's recorded department must change
    /// with it, or they are stranded in a department they left (FR-015, INV-2). The domain cannot
    /// reach the members, so the use case that calls this is responsible for reassigning them in
    /// the same transaction.
    /// </remarks>
    public void MoveTo(Department destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Id == DepartmentId)
        {
            return;
        }

        if (!destination.IsActive)
        {
            throw new InvalidOperationException(
                "A team cannot be moved into a department that is not active.");
        }

        DepartmentId = destination.Id;
    }
}
