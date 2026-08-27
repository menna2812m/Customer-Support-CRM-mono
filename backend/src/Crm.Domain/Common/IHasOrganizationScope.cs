namespace Crm.Domain.Common;

/// <summary>
/// Marks an entity whose visibility can be limited by organizational structure.
/// Declared before any feature needs it so that Constitution V - no single-department and no
/// single-branch assumption - holds from the first entity onwards.
/// </summary>
public interface IHasOrganizationScope
{
    Guid? DepartmentId { get; }

    Guid? BranchId { get; }

    Guid? TeamId { get; }
}
