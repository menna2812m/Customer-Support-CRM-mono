using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Crm.IntegrationTests.Infrastructure;

/// <summary>
/// Provides the database for the integration suite (spec FR-046, clarification 2026-08-26).
///
/// The suite provisions its own disposable SQL Server: one container per run, a uniquely named
/// database inside it, migrations applied, everything disposed at the end. It never touches a
/// developer database and needs no manual setup. The unique name is what lets two runs share a
/// machine without interfering.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer _container = new MsSqlBuilder(SqlServerImage)
        .WithCleanUp(true)
        .Build();

    private readonly string _databaseName = "Crm_" + Guid.CreateVersion7().ToString("n");

    /// <summary>Connection string for the run-scoped database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            // Never fall back to another database: a silent substitute would prove nothing about
            // SQL Server behaviour, and the failure would be reported as a misleading test error.
            throw new InvalidOperationException(
                "The integration test suite could not start its SQL Server container. "
                    + "A container runtime (Docker) must be installed and running. "
                    + "See docs/testing.md.",
                ex);
        }

        ConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            _container.GetConnectionString())
        {
            InitialCatalog = _databaseName,
        }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public CrmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new CrmDbContext(options);
    }
}

/// <summary>
/// One container for the whole run: starting SQL Server per test class would blow the ten-minute
/// verification budget (spec SC-009).
/// </summary>
[CollectionDefinition(DatabaseCollectionDefinition.Name)]
public sealed class DatabaseCollectionDefinition : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "database";
}
