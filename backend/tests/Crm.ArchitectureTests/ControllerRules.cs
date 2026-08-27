using NetArchTest.Rules;
using Shouldly;

namespace Crm.ArchitectureTests;

/// <summary>
/// Constitution I and III at the HTTP boundary: controllers do HTTP, not business rules, and they
/// never hand a persistence entity to a caller.
/// </summary>
public sealed class ControllerRules : ArchitectureTestBase
{
    [Fact]
    public void Controllers_do_not_touch_persistence()
    {
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Crm.Infrastructure.Persistence",
                "Microsoft.Data.SqlClient")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "A controller reaching into persistence means business logic is drifting into the HTTP "
                + "layer. Put the work in an Application use case:"
                + Environment.NewLine
                + Describe(result));
    }

    [Fact]
    public void Domain_entities_are_not_exposed_by_the_api()
    {
        // Public contracts are explicit DTOs (Constitution III). Entities carry invariants,
        // navigation properties, and audit fields that no caller should see or send.
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn("Crm.Domain.Common")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Controllers must exchange DTOs, never entity types:"
                + Environment.NewLine
                + Describe(result));
    }
}
