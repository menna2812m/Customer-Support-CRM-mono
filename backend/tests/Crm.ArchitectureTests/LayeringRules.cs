using NetArchTest.Rules;
using Shouldly;

namespace Crm.ArchitectureTests;

/// <summary>
/// Constitution I as executable rules. These violations are silent, cheap to introduce, and
/// expensive to unwind later, so they are enforced by the build rather than by review.
/// </summary>
public sealed class LayeringRules : ArchitectureTestBase
{
    [Fact]
    public void Domain_depends_on_nothing_in_the_solution_and_no_infrastructure()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Crm.Application",
                "Crm.Infrastructure",
                "Crm.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions.DependencyInjection")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "The Domain layer must stay free of every other layer and of infrastructure concerns:"
                + Environment.NewLine
                + Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_the_api()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Crm.Infrastructure",
                "Crm.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore.Mvc")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application holds use cases and abstractions; persistence and HTTP belong outside it:"
                + Environment.NewLine
                + Describe(result));
    }

    [Fact]
    public void Api_does_not_reference_persistence_or_a_database_driver()
    {
        // Composition lives in Crm.Infrastructure.DependencyInjection, so the API never needs a
        // vendor type. Constitution XV: domain logic must not depend on vendor SDKs.
        var result = Types
            .InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Data.SqlClient",
                "Crm.Infrastructure.Persistence")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Vendor and persistence types belong to Crm.Infrastructure only:"
                + Environment.NewLine
                + Describe(result));
    }
}
