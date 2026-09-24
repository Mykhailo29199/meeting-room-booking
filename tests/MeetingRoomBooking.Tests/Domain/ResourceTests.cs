using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Domain.Resources;

namespace MeetingRoomBooking.Tests.Domain;

public class ResourceTests
{
    // Europe/Berlin: a stable IANA id on every OS, with daylight saving
    // (CET = UTC+1 in winter, CEST = UTC+2 in summer).
    private const string Berlin = "Europe/Berlin";
    private static readonly TimeOnly Eight = new(8, 0);
    private static readonly TimeOnly Twenty = new(20, 0);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void New_resource_is_active_and_keeps_its_details()
    {
        var resource = new Resource("  Sunflower  ", 6, Berlin, Eight, Twenty);

        Assert.True(resource.IsActive);
        Assert.Equal("Sunflower", resource.Name);
        Assert.Equal(6, resource.Capacity);
        Assert.Equal(Berlin, resource.TimeZoneId);
        Assert.NotEqual(Guid.Empty, resource.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string name)
    {
        Assert.Throws<DomainException>(() => new Resource(name, 6, Berlin, Eight, Twenty));
    }

    [Fact]
    public void Name_longer_than_the_limit_is_rejected()
    {
        var name = new string('x', Resource.MaxNameLength + 1);
        Assert.Throws<DomainException>(() => new Resource(name, 6, Berlin, Eight, Twenty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Capacity_must_be_positive(int capacity)
    {
        Assert.Throws<DomainException>(() => new Resource("Sunflower", capacity, Berlin, Eight, Twenty));
    }

    [Theory]
    [InlineData(8, 10, 20, 0)] // opens 08:10
    [InlineData(8, 0, 19, 50)] // closes 19:50
    public void Opening_hours_must_be_on_slot_boundaries(int openH, int openM, int closeH, int closeM)
    {
        Assert.Throws<DomainException>(() =>
            new Resource("Sunflower", 6, Berlin, new TimeOnly(openH, openM), new TimeOnly(closeH, closeM)));
    }

    [Theory]
    [InlineData(20, 8)]
    [InlineData(8, 8)]
    public void Resource_must_close_after_it_opens(int openH, int closeH)
    {
        Assert.Throws<DomainException>(() =>
            new Resource("Sunflower", 6, Berlin, new TimeOnly(openH, 0), new TimeOnly(closeH, 0)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Nowhere/Atlantis")]
    public void Unknown_time_zone_is_rejected(string timeZoneId)
    {
        Assert.Throws<DomainException>(() => new Resource("Sunflower", 6, timeZoneId, Eight, Twenty));
    }

    [Fact]
    public void Windows_time_zone_id_is_stored_as_its_IANA_equivalent()
    {
        var resource = new Resource("Sunflower", 6, "W. Europe Standard Time", Eight, Twenty);

        Assert.Equal(Berlin, resource.TimeZoneId);
    }

    [Fact]
    public void Failed_update_leaves_the_resource_unchanged()
    {
        var resource = new Resource("Sunflower", 6, Berlin, Eight, Twenty);

        Assert.Throws<DomainException>(() => resource.Update("Tulip", 0, Berlin, Eight, Twenty));

        Assert.Equal("Sunflower", resource.Name);
        Assert.Equal(6, resource.Capacity);
    }

    [Fact]
    public void Deactivate_and_activate_toggle_availability()
    {
        var resource = new Resource("Sunflower", 6, Berlin, Eight, Twenty);

        resource.Deactivate();
        Assert.False(resource.IsActive);

        resource.Activate();
        Assert.True(resource.IsActive);
    }

    [Fact]
    public void Slot_starts_are_UTC_and_follow_local_opening_hours()
    {
        // 09:00–10:00 Berlin summer time (UTC+2) is 07:00–08:00 UTC.
        var resource = new Resource("Sunflower", 6, Berlin, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var slots = resource.GetSlotStarts(new DateOnly(2026, 10, 1));

        Assert.Equal(
            [Utc(2026, 10, 1, 7, 0), Utc(2026, 10, 1, 7, 15), Utc(2026, 10, 1, 7, 30), Utc(2026, 10, 1, 7, 45)],
            slots);
        Assert.All(slots, s => Assert.Equal(DateTimeKind.Utc, s.Kind));
    }

    [Fact]
    public void Same_local_hours_in_different_time_zones_are_different_UTC_slots()
    {
        var berlin = new Resource("Berlin room", 6, Berlin, Eight, Twenty);
        var newYork = new Resource("New York room", 6, "America/New_York", Eight, Twenty);
        var date = new DateOnly(2026, 10, 1);

        Assert.Equal(Utc(2026, 10, 1, 6, 0), berlin.GetSlotStarts(date)[0]);   // UTC+2
        Assert.Equal(Utc(2026, 10, 1, 12, 0), newYork.GetSlotStarts(date)[0]); // UTC-4
    }

    [Fact]
    public void Ordinary_day_08_to_20_has_48_slots()
    {
        var resource = new Resource("Sunflower", 6, Berlin, Eight, Twenty);

        Assert.Equal(48, resource.GetSlotStarts(new DateOnly(2026, 10, 1)).Count);
    }

    [Theory]
    [InlineData(2026, 10, 1, 95)]  // ordinary day: 23h45m
    [InlineData(2027, 3, 28, 91)]  // clocks go forward 02:00 -> 03:00: one hour fewer
    [InlineData(2026, 10, 25, 99)] // clocks go back 03:00 -> 02:00: one hour more
    public void Slot_count_follows_real_elapsed_time_on_daylight_saving_days(int year, int month, int day, int expected)
    {
        var resource = new Resource("Always open", 2, Berlin, new TimeOnly(0, 0), new TimeOnly(23, 45));

        Assert.Equal(expected, resource.GetSlotStarts(new DateOnly(year, month, day)).Count);
    }

    [Fact]
    public void Opening_time_skipped_by_daylight_saving_moves_to_the_next_valid_time()
    {
        // 02:30 does not exist in Berlin on 2027-03-28; opening starts at 03:00 CEST = 01:00 UTC.
        var resource = new Resource("Early bird", 2, Berlin, new TimeOnly(2, 30), new TimeOnly(4, 0));

        var slots = resource.GetSlotStarts(new DateOnly(2027, 3, 28));

        Assert.Equal(Utc(2027, 3, 28, 1, 0), slots[0]);
        Assert.Equal(4, slots.Count); // 03:00–04:00 local
    }
}
