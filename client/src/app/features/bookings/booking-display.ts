import { BookingListItem, LocalDate, TimeZoneId, UtcDateTime } from '../../core/api/models';
import { formatDuration, minutesBetween } from '../../core/time/duration';
import { formatLongDate } from '../../core/time/local-date';
import { formatTime, localDateOf, viewerTimeHint, zoneLabel } from '../../core/time/resource-time';

/** Where a booking is in time. */
export type BookingState = 'upcoming' | 'in-progress' | 'past';

export function bookingState(
  booking: { startUtc: UtcDateTime; endUtc: UtcDateTime },
  nowMs: number,
): BookingState {
  if (nowMs < Date.parse(booking.startUtc)) {
    return 'upcoming';
  }
  return nowMs < Date.parse(booking.endUtc) ? 'in-progress' : 'past';
}

/** A booking as lists show it: in the resource's time zone, like its schedule. */
export interface BookingDisplay {
  /** The day in the resource's zone, e.g. `Thursday, 1 October 2026`. */
  date: string;
  /** For linking to that day's schedule. */
  localDate: LocalDate;
  /** E.g. `10:00–11:00`. */
  time: string;
  /** E.g. `Berlin time (UTC+2)`. */
  zone: string;
  /** The start in the viewer's own time, if their clock differs. */
  viewerTime: string | null;
  duration: string;
}

export function describeBooking(
  booking: Pick<BookingListItem, 'startUtc' | 'endUtc' | 'resourceTimeZoneId'>,
  viewerTimeZoneId: TimeZoneId,
): BookingDisplay {
  const zone = booking.resourceTimeZoneId;
  const localDate = localDateOf(booking.startUtc, zone);
  return {
    date: formatLongDate(localDate),
    localDate,
    time: `${formatTime(booking.startUtc, zone)}–${formatTime(booking.endUtc, zone)}`,
    zone: zoneLabel(zone, booking.startUtc),
    viewerTime: viewerTimeHint(booking.startUtc, zone, viewerTimeZoneId),
    duration: formatDuration(minutesBetween(booking.startUtc, booking.endUtc)),
  };
}
