using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>. One instance per
/// request (scoped), sharing that request's <see cref="AppDbContext"/>.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly IUniqueConstraintViolationDetector _uniqueViolationDetector;

    public UnitOfWork(AppDbContext context, IUniqueConstraintViolationDetector uniqueViolationDetector)
    {
        _context = context;
        _uniqueViolationDetector = uniqueViolationDetector;
        Resources = new ResourceRepository(context);
        Bookings = new BookingRepository(context);
    }

    public IResourceRepository Resources { get; }

    public IBookingRepository Bookings { get; }

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new EfTransaction(await _context.Database.BeginTransactionAsync(cancellationToken));

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // SaveChanges runs every staged insert/update in one transaction.
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Must come first: DbUpdateConcurrencyException derives from DbUpdateException.
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (_uniqueViolationDetector.IsUniqueConstraintViolation(ex))
        {
            throw new UniqueConstraintViolationException(ex);
        }
        // Any other DbUpdateException (foreign key, lost connection, ...) is a
        // real error and propagates unchanged — never reported as a conflict.
    }
}
