namespace MeetingRoomBooking.Application.Persistence;

/// <summary>A taken slot and whose booking it belongs to — read model for schedules.</summary>
public sealed record BookedSlot(DateTime SlotStartUtc, Guid BookingId, string UserId);
