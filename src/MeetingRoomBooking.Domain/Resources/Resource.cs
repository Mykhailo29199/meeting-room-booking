namespace MeetingRoomBooking.Domain.Resources;

/// <summary>
/// A bookable resource, e.g. a meeting room.
///
/// Its opening hours define the fixed set of 15-minute slots that can be
/// booked on any day: a resource open 08:00–20:00 has 48 slots per day.
/// Opening hours are in the resource's own local time
/// (<see cref="TimeZoneId"/>), so resources in different countries each keep
/// their local hours. Every point in time handled by the domain is UTC.
/// </summary>
public class Resource
{
    public const int MaxNameLength = 100;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Capacity { get; private set; }

    /// <summary>IANA time zone of the resource's location, e.g. "Europe/Berlin".</summary>
    public string TimeZoneId { get; private set; } = string.Empty;

    /// <summary>Local time the first bookable slot of the day starts.</summary>
    public TimeOnly OpensAt { get; private set; }

    /// <summary>Local time the last bookable slot of the day ends (exclusive).</summary>
    public TimeOnly ClosesAt { get; private set; }

    /// <summary>An inactive resource keeps its existing bookings but accepts no new ones.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The resolved <see cref="TimeZoneId"/>. Not stored — derived from the id.</summary>
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    // Used by EF Core when loading from the database.
    private Resource() { }

    public Resource(string name, int capacity, string timeZoneId, TimeOnly opensAt, TimeOnly closesAt)
    {
        Id = Guid.CreateVersion7();
        Update(name, capacity, timeZoneId, opensAt, closesAt);
    }

    /// <summary>Changes the resource's details; validates everything before changing anything.</summary>
    public void Update(string name, int capacity, string timeZoneId, TimeOnly opensAt, TimeOnly closesAt)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
            throw new DomainException("Resource name is required.");
        if (trimmedName.Length > MaxNameLength)
            throw new DomainException($"Resource name must be at most {MaxNameLength} characters.");
        if (capacity <= 0)
            throw new DomainException("Resource capacity must be at least 1.");
        if (!TimeSlots.IsAligned(opensAt) || !TimeSlots.IsAligned(closesAt))
            throw new DomainException("Opening hours must start and end on a 15-minute boundary.");
        if (closesAt <= opensAt)
            throw new DomainException("The resource must close after it opens.");
        var ianaTimeZoneId = NormalizeTimeZoneId(timeZoneId);

        Name = trimmedName;
        Capacity = capacity;
        TimeZoneId = ianaTimeZoneId;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>
    /// UTC start times of every bookable slot on the resource's local
    /// <paramref name="localDate"/>, in order. Slots are stepped in UTC, so on
    /// a daylight-saving change day the count follows real elapsed time.
    /// </summary>
    public IReadOnlyList<DateTime> GetSlotStarts(DateOnly localDate)
    {
        var (openUtc, closeUtc) = GetOpeningWindowUtc(localDate);
        var slots = new List<DateTime>();
        for (var slot = openUtc; slot < closeUtc; slot += TimeSlots.Length)
        {
            slots.Add(slot);
        }
        return slots;
    }

    /// <summary>
    /// True if the UTC range [<paramref name="startUtc"/>, <paramref name="endUtc"/>)
    /// lies within the opening hours of a single local day.
    /// </summary>
    public bool IsOpenDuring(DateTime startUtc, DateTime endUtc)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, TimeZone));
        var (openUtc, closeUtc) = GetOpeningWindowUtc(localDate);
        return startUtc >= openUtc && endUtc <= closeUtc;
    }

    /// <summary>Opening hours of the local day <paramref name="localDate"/>, as UTC instants.</summary>
    private (DateTime OpenUtc, DateTime CloseUtc) GetOpeningWindowUtc(DateOnly localDate)
    {
        var timeZone = TimeZone;
        return (ToUtc(localDate.ToDateTime(OpensAt), timeZone), ToUtc(localDate.ToDateTime(ClosesAt), timeZone));
    }

    /// <summary>
    /// Converts a local wall-clock time to UTC. A local time that does not
    /// exist (skipped when clocks go forward) is moved to the first valid slot
    /// after it; an ambiguous one (repeated when clocks go back) resolves to
    /// standard time, as <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> does.
    /// </summary>
    private static DateTime ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        while (timeZone.IsInvalidTime(local))
        {
            local += TimeSlots.Length;
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    /// <summary>
    /// Validates the time zone and returns its IANA id, which browsers
    /// understand; Windows ids such as "W. Europe Standard Time" are converted.
    /// </summary>
    private static string NormalizeTimeZoneId(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new DomainException("Time zone is required.");

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException($"Unknown time zone '{timeZoneId}'. Use an IANA id such as 'Europe/Berlin'.");
        }

        if (timeZone.HasIanaId)
            return timeZone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Id, out var ianaId)
            ? ianaId
            : throw new DomainException($"Time zone '{timeZoneId}' has no IANA equivalent.");
    }
}
