using MeetingRoomBooking.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Infrastructure;

/// <summary>
/// A throw-away database on a real SQL Server, built by applying the
/// production migrations — so these tests also prove the migrations create
/// the schema the concurrency design relies on.
///
/// Opt-in: runs only when the environment variable
/// <see cref="ConnectionStringVariable"/> holds a connection string to a SQL
/// Server the tests may create and drop databases on, e.g.
/// <c>Server=localhost;Trusted_Connection=True;TrustServerCertificate=True</c>.
/// Any database name in it is replaced with a unique one per test class.
/// </summary>
internal sealed class SqlServerTestDatabase : ITestDatabase, IAsyncDisposable
{
    public const string ConnectionStringVariable = "MEETINGROOMBOOKING_TEST_SQLSERVER";

    private readonly string _connectionString;

    private SqlServerTestDatabase(string connectionString) => _connectionString = connectionString;

    public static string? ServerConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public static async Task<SqlServerTestDatabase> CreateAsync()
    {
        var builder = new SqlConnectionStringBuilder(ServerConnectionString)
        {
            InitialCatalog = $"MeetingRoomBookingTests_{Guid.NewGuid():N}"
        };
        var database = new SqlServerTestDatabase(builder.ConnectionString);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        // Behave like Azure SQL, where READ_COMMITTED_SNAPSHOT is on by default:
        // plain reads see the last committed version instead of waiting for
        // locks. That is the setting under which a missing lock actually
        // produces a race, so tests must not rely on SQL Server's on-premises
        // default (reads that block) hiding it.
#pragma warning disable EF1002 // The database name is generated above, not user input.
        await context.Database.ExecuteSqlRawAsync(
            $"ALTER DATABASE [{builder.InitialCatalog}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
#pragma warning restore EF1002
        return database;
    }

    public UnitOfWork NewUnitOfWork(AppDbContext context) => CreateUnitOfWork(context);

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options);

    public static UnitOfWork CreateUnitOfWork(AppDbContext context) =>
        new(context, new SqlServerUniqueConstraintViolationDetector());

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}

/// <summary>
/// A test that needs a real SQL Server. Reported as skipped — not passed, not
/// failed — when <see cref="SqlServerTestDatabase.ConnectionStringVariable"/>
/// is not set, so the suite still runs anywhere with a plain `dotnet test`.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(SqlServerTestDatabase.ServerConnectionString))
        {
            Skip = $"Set {SqlServerTestDatabase.ConnectionStringVariable} to a SQL Server connection string to run.";
        }
    }
}
