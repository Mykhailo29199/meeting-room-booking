namespace MeetingRoomBooking.Application.Common;

/// <summary>
/// Who is making the request. Built by the API from the authenticated user
/// and passed into services explicitly, so services never read HTTP state.
/// </summary>
public sealed record UserContext(string UserId, bool IsAdmin);
