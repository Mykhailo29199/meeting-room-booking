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
internal sealed class SqlServerTestDatabase : IAsyncDisposable
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
        return database;
    }

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
