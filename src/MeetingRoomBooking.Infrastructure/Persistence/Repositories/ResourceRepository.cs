using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain.Resources;

namespace MeetingRoomBooking.Infrastructure.Persistence.Repositories;

internal sealed class ResourceRepository : IResourceRepository
{
    private readonly AppDbContext _context;

    public ResourceRepository(AppDbContext context) => _context = context;

    public async Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Resources.FindAsync([id], cancellationToken);

    public void Add(Resource resource) => _context.Resources.Add(resource);
}
