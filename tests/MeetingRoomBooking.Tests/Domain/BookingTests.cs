using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Domain.Bookings;
using MeetingRoomBooking.Domain.Resources;

namespace MeetingRoomBooking.Tests.Domain;

public class BookingTests
{
    private static readonly TimeZoneInfo BerlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    // "Now" is the morning before the booking day, in UTC.
    private static readonly DateTime NowUtc = new(2026, 9, 30, 7, 0, 0, DateTimeKind.Utc);

    /// <summary>A Berlin resource open 08:00–20:00 local time.</summary>
    private static Resource OpenResource() =>
        new("Sunflower", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));

    /// <summary>UTC instant of a Berlin wall-clock time on the booking day (2026-10-01).</summary>
    private static DateTime Berlin(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 1).AddHours(hour).AddMinutes(minute), BerlinZone);

    [Fact]
    public void Booking_is_split_into_one_slot_per_15_minutes()
    {
        var resource = OpenResource();

        var booking = Booking.Create(resource, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc);

        Assert.Equal(
            [Berlin(10, 0), Berlin(10, 15), Berlin(10, 30), Berlin(10, 45)],
            booking.Slots.Select(s => s.SlotStartUtc));
        Assert.All(booking.Slots, s =>
        {
            Assert.Equal(booking.Id, s.BookingId);
            Assert.Equal(resource.Id, s.ResourceId);
        });
    }

    [Fact]
    public void Single_15_minute_slot_can_be_booked_and_keeps_its_details()
    {
        var resource = OpenResource();

        var booking = Booking.Create(resource, "user-1", Berlin(10, 0), Berlin(10, 15), NowUtc);

        Assert.NotEqual(Guid.Empty, booking.Id);
        Assert.Equal(resource.Id, booking.ResourceId);
        Assert.Equal("user-1", booking.UserId);
        Assert.Equal(Berlin(10, 0), booking.StartUtc);
        Assert.Equal(Berlin(10, 15), booking.EndUtc);
        Assert.Equal(NowUtc, booking.CreatedAtUtc);
        Assert.Single(booking.Slots);
    }

    [Fact]
    public void Overlapping_bookings_share_a_slot()
    {
        // The property the database constraint relies on: any overlap, not just
        // an identical start time, produces at least one identical slot.
        var resource = OpenResource();

        var wholeHour = Booking.Create(resource, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc);
        var inner = Booking.Create(resource, "user-2", Berlin(10, 15), Berlin(10, 30), NowUtc);

        Assert.Contains(inner.Slots.Single().SlotStartUtc, wholeHour.Slots.Select(s => s.SlotStartUtc));
    }

    [Fact]
    public void Booking_may_fill_the_whole_opening_hours()
    {
        var booking = Booking.Create(OpenResource(), "user-1", Berlin(8, 0), Berlin(20, 0), NowUtc);

        Assert.Equal(48, booking.Slots.Count);
    }

    [Fact]
    public void Inactive_resource_cannot_be_booked()
    {
        var resource = OpenResource();
        resource.Deactivate();

        Assert.Throws<DomainException>(() => Booking.Create(resource, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc));
    }

    [Theory]
    [InlineData(11, 0, 10, 0)] // end before start
    [InlineData(10, 0, 10, 0)] // zero length
    public void Booking_must_end_after_it_starts(int startH, int startM, int endH, int endM)
    {
        Assert.Throws<DomainException>(() =>
            Booking.Create(OpenResource(), "user-1", Berlin(startH, startM), Berlin(endH, endM), NowUtc));
    }

    [Theory]
    [InlineData(10, 5, 11, 0)]  // start off the grid
    [InlineData(10, 0, 10, 50)] // end off the grid
    public void Booking_must_be_on_slot_boundaries(int startH, int startM, int endH, int endM)
    {
        Assert.Throws<DomainException>(() =>
            Booking.Create(OpenResource(), "user-1", Berlin(startH, startM), Berlin(endH, endM), NowUtc));
    }

    [Fact]
    public void Booking_with_stray_seconds_is_rejected()
    {
        Assert.Throws<DomainException>(() =>
            Booking.Create(OpenResource(), "user-1", Berlin(10, 0).AddSeconds(1), Berlin(11, 0), NowUtc));
    }

    [Fact]
    public void Booking_cannot_start_in_the_past()
    {
        var nowUtc = Berlin(10, 5);

        Assert.Throws<DomainException>(() => Booking.Create(OpenResource(), "user-1", Berlin(10, 0), Berlin(11, 0), nowUtc));
    }

    [Theory]
    [InlineData(7, 45, 8, 30)]   // starts before opening
    [InlineData(19, 30, 20, 15)] // ends after closing
    public void Booking_must_be_within_local_opening_hours(int startH, int startM, int endH, int endM)
    {
        Assert.Throws<DomainException>(() =>
            Booking.Create(OpenResource(), "user-1", Berlin(startH, startM), Berlin(endH, endM), NowUtc));
    }

    [Fact]
    public void Opening_hours_are_checked_in_the_resource_time_zone()
    {
        // 10:00 Berlin = 04:00 New York: fine for the Berlin room, closed in New York.
        var berlinRoom = OpenResource();
        var newYorkRoom = new Resource("New York", 6, "America/New_York", new TimeOnly(8, 0), new TimeOnly(20, 0));

        Booking.Create(berlinRoom, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc);
        Assert.Throws<DomainException>(() =>
            Booking.Create(newYorkRoom, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc));
    }

    [Fact]
    public void Booking_cannot_span_two_days()
    {
        var resource = new Resource("Night owl", 2, "Europe/Berlin", new TimeOnly(0, 0), new TimeOnly(23, 45));

        Assert.Throws<DomainException>(() =>
            Booking.Create(resource, "user-1", Berlin(23, 0), Berlin(24, 15), NowUtc));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_UTC_times_are_rejected(DateTimeKind kind)
    {
        var start = DateTime.SpecifyKind(Berlin(10, 0), kind);

        Assert.Throws<ArgumentException>(() => Booking.Create(OpenResource(), "user-1", start, Berlin(11, 0), NowUtc));
    }

    [Theory]
    [InlineData(9, 45)] // before the start
    [InlineData(10, 0)] // exactly at the start: no slot has begun yet
    public void Releasing_a_booking_that_has_not_started_cancels_it(int nowH, int nowM)
    {
        var booking = Booking.Create(OpenResource(), "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc);

        var outcome = booking.Release(Berlin(nowH, nowM));

        Assert.Equal(ReleaseOutcome.Cancelled, outcome);
        Assert.Equal(4, booking.Slots.Count); // untouched: the caller deletes the whole booking
    }

    [Fact]
    public void Releasing_a_booking_under_way_frees_the_future_and_keeps_the_slot_in_progress()
    {
        // Booked 08:00–20:00, finished early and released at 14:05.
        var booking = Booking.Create(OpenResource(), "user-1", Berlin(8, 0), Berlin(20, 0), NowUtc);

        var outcome = booking.Release(Berlin(14, 5));

        Assert.Equal(ReleaseOutcome.Shortened, outcome);
        Assert.Equal(Berlin(8, 0), booking.StartUtc);
        Assert.Equal(Berlin(14, 15), booking.EndUtc);         // 14:00–14:15 is in progress, kept
        Assert.Equal(25, booking.Slots.Count);                 // 08:00 ... 14:00
        Assert.Equal(Berlin(14, 0), booking.Slots.Max(s => s.SlotStartUtc));
    }

    [Fact]
    public void Releasing_exactly_on_a_slot_boundary_frees_that_slot_too()
    {
        var booking = Booking.Create(OpenResource(), "user-1", Berlin(8, 0), Berlin(20, 0), NowUtc);

        booking.Release(Berlin(14, 0));

        Assert.Equal(Berlin(14, 0), booking.EndUtc);
        Assert.Equal(24, booking.Slots.Count); // 08:00 ... 13:45
    }

    [Theory]
    [InlineData(10, 50)] // only the last slot (10:45–11:00) is left, and it is in progress
    [InlineData(11, 0)]  // exactly at the end
    [InlineData(12, 0)]  // long over
    public void Nothing_to_release_is_rejected(int nowH, int nowM)
    {
        var booking = Booking.Create(OpenResource(), "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc);

        Assert.Throws<DomainException>(() => booking.Release(Berlin(nowH, nowM)));
        Assert.Equal(Berlin(11, 0), booking.EndUtc);
        Assert.Equal(4, booking.Slots.Count);
    }

    [Fact]
    public void User_id_is_required()
    {
        Assert.Throws<ArgumentException>(() => Booking.Create(OpenResource(), " ", Berlin(10, 0), Berlin(11, 0), NowUtc));
    }
}
