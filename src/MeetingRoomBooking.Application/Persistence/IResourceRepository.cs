using MeetingRoomBooking.Domain.Resources;

namespace MeetingRoomBooking.Application.Persistence;

/// <summary>Stages and reads resources. Nothing is saved until <see cref="IUnitOfWork.CompleteAsync"/>.</summary>
public interface IResourceRepository
{
    /// <summary>The resource, tracked for changes; null if it does not exist.</summary>
    Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Resource resource);
}
