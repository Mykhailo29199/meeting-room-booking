using MeetingRoomBooking.Infrastructure.Persistence;

namespace MeetingRoomBooking.Tests.Infrastructure;

/// <summary>A real test database (SQLite or SQL Server) that tests can run the same scenario against.</summary>
internal interface ITestDatabase
{
    /// <summary>A new context on its own connection — like a separate request.</summary>
    AppDbContext CreateContext();

    /// <summary>The production UnitOfWork with this database's unique-violation rules.</summary>
    UnitOfWork NewUnitOfWork(AppDbContext context);
}
