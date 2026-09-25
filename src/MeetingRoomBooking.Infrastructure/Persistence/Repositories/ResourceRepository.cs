using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain.Resources;
using MeetingRoomBooking.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Infrastructure.Persistence.Repositories;

internal sealed class ResourceRepository : IResourceRepository
{
    private readonly AppDbContext _context;

    public ResourceRepository(AppDbContext context) => _context = context;

    public async Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Resources.FindAsync([id], cancellationToken);

    // An UPDATE that changes nothing still takes an exclusive row lock, held
    // until the transaction ends — on SQL Server/Azure SQL (also under
    // READ_COMMITTED_SNAPSHOT, where plain reads would not block) and on
    // SQLite. Deliberately not a SELECT with provider-specific lock hints.
    public async Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Resources
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.IsActive, r => r.IsActive), cancellationToken) == 1;

    // Tracked on purpose: the version is a shadow property, which EF Core only
    // keeps for tracked entities. Resource lists are small.
    public async Task<IReadOnlyList<Resource>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        await _context.Resources
            .Where(r => includeInactive || r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

    public void Add(Resource resource) => _context.Resources.Add(resource);

    public Guid GetVersion(Resource resource) =>
        (Guid)_context.Entry(resource).Property(ResourceConfiguration.VersionProperty).CurrentValue!;

    // The "original" value is what EF Core puts in the UPDATE's WHERE clause.
    public void SetExpectedVersion(Resource resource, Guid version) =>
        _context.Entry(resource).Property(ResourceConfiguration.VersionProperty).OriginalValue = version;
}
