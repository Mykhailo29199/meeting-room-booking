namespace MeetingRoomBooking.Application.Common;

/// <summary>
/// The two roles of the task: a <see cref="User"/> views resources and
/// schedules and books slots; an <see cref="Admin"/> can additionally manage
/// resources and see all bookings.
/// </summary>
public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}
