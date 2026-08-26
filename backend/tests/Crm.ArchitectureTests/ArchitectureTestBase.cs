using System.Reflection;
using Crm.Application.Common;
using Crm.Domain.Common;
using Crm.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace Crm.ArchitectureTests;

/// <summary>
/// Shared assembly handles for the architecture rules.
///
/// These rules exist because the constitutional layering guarantees are violated silently and
/// cheaply. A build failure is the only enforcement that survives a busy week.
/// </summary>
public abstract class ArchitectureTestBase
{
    protected static Assembly DomainAssembly { get; } = typeof(Entity).Assembly;

    protected static Assembly ApplicationAssembly { get; } = typeof(ErrorCodes).Assembly;

    protected static Assembly InfrastructureAssembly { get; } = typeof(CrmDbContext).Assembly;

    protected static Assembly ApiAssembly { get; } = typeof(Program).Assembly;

    /// <summary>Renders a rule failure as the list of offending types, so the message is actionable.</summary>
    protected static string Describe(NetArchTest.Rules.TestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccessful
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                (result.FailingTypeNames ?? []).Select(name => "  - " + name));
    }
}
