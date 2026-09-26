import { BookingListItem, CancellationResult, TimeZoneId } from '../../core/api/models';
import { Confirmation } from '../../core/notify/confirm-dialog';
import { formatTime } from '../../core/time/resource-time';
import { bookingState, describeBooking } from './booking-display';

// Cancelling is one server call (`DELETE /api/bookings/{id}`) that frees
// every slot that has not started: before the start that is the whole
// booking, while it is under way it ends it now. These texts say which.

/** What to ask before cancelling; `bookedBy` names the owner when it is someone else's. */
export function cancelConfirmation(
  booking: BookingListItem,
  viewerTimeZoneId: TimeZoneId,
  nowMs: number,
  bookedBy?: string,
): Confirmation {
  const { date, time, zone } = describeBooking(booking, viewerTimeZoneId);
  const what = `${booking.resourceName}, ${date}, ${time} (${zone}).${bookedBy ? ` Booked by ${bookedBy}.` : ''}`;
  return bookingState(booking, nowMs) === 'upcoming'
    ? {
        title: 'Cancel this booking?',
        message: `${what} The time becomes free for others.`,
        confirm: 'Cancel booking',
        dismiss: 'Keep it',
      }
    : {
        title: 'End this booking now?',
        message: `${what} The slots that have not started become free for others.`,
        confirm: 'End now',
        dismiss: 'Keep it',
      };
}

/** What the cancellation did, in the resource's time. */
export function cancellationMessage(result: CancellationResult, booking: BookingListItem): string {
  if (result.cancelledCompletely || !result.remainingBooking) {
    return 'Booking cancelled.';
  }
  const end = formatTime(result.remainingBooking.endUtc, booking.resourceTimeZoneId);
  return `Booking ended early: it now ends at ${end}.`;
}

/** The action a booking's button offers, or null once it is over. */
export function cancelAction(booking: BookingListItem, nowMs: number): string | null {
  switch (bookingState(booking, nowMs)) {
    case 'upcoming':
      return 'Cancel';
    case 'in-progress':
      return 'End now';
    case 'past':
      return null;
  }
}
