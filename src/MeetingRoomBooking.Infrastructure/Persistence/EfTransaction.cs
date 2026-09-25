using MeetingRoomBooking.Application.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace MeetingRoomBooking.Infrastructure.Persistence;

/// <summary><see cref="ITransaction"/> over an EF Core database transaction.</summary>
internal sealed class EfTransaction : ITransaction
{
    private readonly IDbContextTransaction _transaction;

    public EfTransaction(IDbContextTransaction transaction) => _transaction = transaction;

    public Task CommitAsync(CancellationToken cancellationToken = default) => _transaction.CommitAsync(cancellationToken);

    // Disposing an uncommitted transaction rolls it back.
    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
