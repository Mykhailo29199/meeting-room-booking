using MeetingRoomBooking.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Infrastructure;

/// <summary>
/// A real SQLite database (in memory) with the production EF Core model —
/// including the BookingSlots primary key that enforces one booking per slot.
/// Runs anywhere without a SQL Server, so a reviewer can run every test.
///
/// Each <see cref="CreateContext"/> call opens its own connection to the same
/// named in-memory database, the way separate requests use separate pooled
/// connections in production.
/// </summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _connectionString =
        $"Data Source=file:tests-{Guid.NewGuid():N}?mode=memory&cache=shared";

    // A shared in-memory database lives only while a connection to it is open.
    private readonly SqliteConnection _keepAlive;

    private SqliteTestDatabase()
    {
        _keepAlive = new SqliteConnection(_connectionString);
    }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var database = new SqliteTestDatabase();
        await database._keepAlive.OpenAsync();
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    public string ConnectionString => _connectionString;

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options);

    public static UnitOfWork CreateUnitOfWork(AppDbContext context) =>
        new(context, new SqliteUniqueConstraintViolationDetector());

    public ValueTask DisposeAsync() => _keepAlive.DisposeAsync();
}

/// <summary>SQLite rules for recognising a unique-constraint violation.</summary>
internal sealed class SqliteUniqueConstraintViolationDetector : IUniqueConstraintViolationDetector
{
    // Extended result codes: SQLITE_CONSTRAINT_PRIMARYKEY and SQLITE_CONSTRAINT_UNIQUE.
    private const int PrimaryKeyViolation = 1555;
    private const int UniqueViolation = 2067;

    public bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: PrimaryKeyViolation or UniqueViolation };
}
