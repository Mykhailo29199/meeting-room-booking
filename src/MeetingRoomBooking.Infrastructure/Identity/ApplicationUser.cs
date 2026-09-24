using Microsoft.AspNetCore.Identity;

namespace MeetingRoomBooking.Infrastructure.Identity;

/// <summary>
/// An account. ASP.NET Core Identity stores it (with a salted password hash,
/// never the password) in the same database as bookings; Booking.UserId holds
/// its Id.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public const int MaxDisplayNameLength = 100;

    /// <summary>Name shown in the UI, e.g. "Anna Kovalenko".</summary>
    public string DisplayName { get; set; } = string.Empty;
}
