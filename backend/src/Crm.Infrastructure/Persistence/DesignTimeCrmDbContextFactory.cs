using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core tooling (<c>dotnet ef migrations</c>, <c>dotnet ef database update</c>).
///
/// Generating a migration needs a provider, not a reachable server, so the tooling does not have
/// to satisfy the application's real configuration or its startup validation. Set
/// <c>CRM_DESIGN_CONNECTION</c> when a command genuinely touches a database, such as
/// <c>database update</c>.
/// </summary>
public sealed class DesignTimeCrmDbContextFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public const string ConnectionVariable = "CRM_DESIGN_CONNECTION";

    private const string PlaceholderConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=CrmDesignTime;Trusted_Connection=True;TrustServerCertificate=True";

    public CrmDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable) ?? PlaceholderConnection;

        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlServer(
                connection,
                sql => sql.MigrationsAssembly(typeof(CrmDbContext).Assembly.FullName))
            .Options;

        return new CrmDbContext(options);
    }
}
