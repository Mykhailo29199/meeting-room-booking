using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Domain.Bookings;
using MeetingRoomBooking.Domain.Resources;

namespace MeetingRoomBooking.Application.Resources;

/// <summary>
/// Resource use cases. Reading is open to every signed-in user; creating,
/// editing, removing and restoring are admin actions — the API restricts
/// those endpoints to the Admin role.
/// </summary>
public sealed class ResourceService
{
    private const string ChangedMeanwhile =
        "This resource was changed by someone else in the meantime. Reload it and try again.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public ResourceService(IUnitOfWork unitOfWork, TimeProvider time)
    {
        _unitOfWork = unitOfWork;
        _time = time;
    }

    /// <summary>Users see the resources they can book; admins also see removed ones.</summary>
    public async Task<IReadOnlyList<ResourceDto>> ListAsync(UserContext user, CancellationToken cancellationToken = default)
    {
        var resources = await _unitOfWork.Resources.ListAsync(includeInactive: user.IsAdmin, cancellationToken);
        return resources.Select(ToDto).ToList();
    }

    /// <exception cref="NotFoundException">It does not exist — or it was removed and the user is not an admin.</exception>
    public async Task<ResourceDto> GetAsync(Guid id, UserContext user, CancellationToken cancellationToken = default)
    {
        var resource = await _unitOfWork.Resources.GetByIdAsync(id, cancellationToken);
        if (resource is null || (!resource.IsActive && !user.IsAdmin))
            throw new NotFoundException("The resource does not exist.");
        return ToDto(resource);
    }

    /// <exception cref="DomainException">Invalid name, capacity, time zone or opening hours.</exception>
    public async Task<ResourceDto> CreateAsync(CreateResourceRequest request, CancellationToken cancellationToken = default)
    {
        var resource = new Resource(request.Name, request.Capacity, request.TimeZoneId, request.OpensAt, request.ClosesAt);
        _unitOfWork.Resources.Add(resource);
        await _unitOfWork.CompleteAsync(cancellationToken);
        return ToDto(resource);
    }

    /// <summary>
    /// Edits a resource. <see cref="UpdateResourceRequest.Version"/> must be the
    /// version the admin loaded: if someone else saved a change since, the edit
    /// is rejected rather than silently overwriting theirs. Existing bookings
    /// are kept even if the new opening hours no longer cover them.
    /// </summary>
    /// <exception cref="NotFoundException">The resource does not exist.</exception>
    /// <exception cref="DomainException">Invalid new values.</exception>
    /// <exception cref="ConflictException">The resource was changed by someone else meanwhile.</exception>
    public async Task<ResourceDto> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken = default)
    {
        var resource = await LoadAsync(id, cancellationToken);
        _unitOfWork.Resources.SetExpectedVersion(resource, request.Version);

        resource.Update(request.Name, request.Capacity, request.TimeZoneId, request.OpensAt, request.ClosesAt);
        await SaveAsync(cancellationToken);
        return ToDto(resource);
    }

    /// <summary>
    /// "Removes" a resource without destroying history: it is deactivated —
    /// hidden from users and no longer bookable — and every slot of it that
    /// has not started yet is released (<see cref="Booking.Release"/>): future
    /// bookings are cancelled, bookings under way are cut short, past bookings
    /// stay. An admin can undo it with <see cref="RestoreAsync"/>; cancelled
    /// bookings do not come back.
    ///
    /// Race with a booking being made at the same moment: the transaction
    /// first takes the resource-row lock (<see cref="IResourceRepository.LockAsync"/>)
    /// that every booking takes too, and only then reads the bookings. A
    /// booking in flight therefore either commits first — and is released
    /// here like any other — or waits and then finds the resource inactive.
    /// No active booking of a removed resource can remain.
    /// </summary>
    /// <exception cref="NotFoundException">The resource does not exist.</exception>
    /// <exception cref="ConflictException">The resource was changed by someone else meanwhile.</exception>
    public async Task<ResourceRemovalResult> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        if (!await _unitOfWork.Resources.LockAsync(id, cancellationToken))
            throw new NotFoundException("The resource does not exist.");
        var resource = await LoadAsync(id, cancellationToken);
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        resource.Deactivate();

        int cancelled = 0, shortened = 0;
        foreach (var booking in await _unitOfWork.Bookings.GetUnfinishedByResourceAsync(id, nowUtc, cancellationToken))
        {
            if (!booking.HasSlotsToRelease(nowUtc))
                continue; // only the slot in progress is left

            if (booking.Release(nowUtc) == ReleaseOutcome.Cancelled)
            {
                _unitOfWork.Bookings.Remove(booking);
                cancelled++;
            }
            else
            {
                shortened++;
            }
        }

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ResourceRemovalResult(cancelled, shortened);
    }

    /// <summary>Makes a removed resource visible and bookable again.</summary>
    /// <exception cref="NotFoundException">The resource does not exist.</exception>
    public async Task<ResourceDto> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var resource = await LoadAsync(id, cancellationToken);
        resource.Activate();
        await SaveAsync(cancellationToken);
        return ToDto(resource);
    }

    private async Task<Resource> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await _unitOfWork.Resources.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException("The resource does not exist.");

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.CompleteAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException ex)
        {
            throw new ConflictException(ChangedMeanwhile, ex);
        }
    }

    private ResourceDto ToDto(Resource resource) => new(
        resource.Id,
        resource.Name,
        resource.Capacity,
        resource.TimeZoneId,
        resource.OpensAt,
        resource.ClosesAt,
        resource.IsActive,
        _unitOfWork.Resources.GetVersion(resource));
}
