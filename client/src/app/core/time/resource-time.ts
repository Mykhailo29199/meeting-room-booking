import { LocalDate, LocalTime, TimeZoneId, UtcDateTime } from '../api/models';

// Every time the API sends is UTC; a resource's times are shown in the
// resource's own time zone, whatever zone the viewer's browser is in. These
// helpers take the zone as an argument and never use the browser's zone.

interface WallClock {
  date: LocalDate;
  time: string;
}

const clockFormats = new Map<TimeZoneId, Intl.DateTimeFormat>();
const offsetFormats = new Map<TimeZoneId, Intl.DateTimeFormat>();

/** Wall-clock date and time (`HH:mm`, 24-hour) of an instant in a time zone. */
function wallClock(instant: UtcDateTime | Date, timeZoneId: TimeZoneId): WallClock {
  let format = clockFormats.get(timeZoneId);
  if (!format) {
    format = new Intl.DateTimeFormat('en-GB', {
      timeZone: timeZoneId,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    });
    clockFormats.set(timeZoneId, format);
  }
  const parts: Partial<Record<Intl.DateTimeFormatPartTypes, string>> = {};
  for (const part of format.formatToParts(new Date(instant))) {
    parts[part.type] = part.value;
  }
  return {
    date: `${parts.year}-${parts.month}-${parts.day}`,
    time: `${parts.hour}:${parts.minute}`,
  };
}

/** `HH:mm` of an instant in the given time zone, e.g. `10:00`. */
export function formatTime(instant: UtcDateTime | Date, timeZoneId: TimeZoneId): string {
  return wallClock(instant, timeZoneId).time;
}

/** The calendar day an instant falls on in the given time zone. */
export function localDateOf(instant: UtcDateTime | Date, timeZoneId: TimeZoneId): LocalDate {
  return wallClock(instant, timeZoneId).date;
}

/** A local time from the API (`08:00:00`) as `08:00`. */
export function formatLocalTime(time: LocalTime): string {
  return time.slice(0, 5);
}

/** The zone's offset from UTC at an instant, e.g. `UTC+2`, `UTC-4`, `UTC+5:30`, `UTC`. */
export function zoneOffset(timeZoneId: TimeZoneId, at: UtcDateTime | Date): string {
  let format = offsetFormats.get(timeZoneId);
  if (!format) {
    format = new Intl.DateTimeFormat('en-US', {
      timeZone: timeZoneId,
      timeZoneName: 'shortOffset',
    });
    offsetFormats.set(timeZoneId, format);
  }
  const name = format.formatToParts(new Date(at)).find((part) => part.type === 'timeZoneName');
  // `shortOffset` gives `GMT+2`, and `GMT` or `GMT+0` (depending on the ICU
  // version) for no offset; users know the same thing as UTC.
  const offset = (name?.value ?? 'GMT').replace('GMT', 'UTC');
  return offset === 'UTC+0' || offset === 'UTC-0' ? 'UTC' : offset;
}

/**
 * How a schedule's times are labelled, e.g. `Berlin time (UTC+2)`. The offset
 * is taken at `at` because it changes with daylight saving time. Zones that
 * are not a place (`UTC`, `Etc/GMT+3`) are labelled by their offset alone.
 */
export function zoneLabel(timeZoneId: TimeZoneId, at: UtcDateTime | Date): string {
  const offset = zoneOffset(timeZoneId, at);
  if (!timeZoneId.includes('/') || timeZoneId.startsWith('Etc/')) {
    return offset;
  }
  // `America/Argentina/Buenos_Aires` → `Buenos Aires`.
  const place = timeZoneId.slice(timeZoneId.lastIndexOf('/') + 1).replaceAll('_', ' ');
  return `${place} time (${offset})`;
}

/**
 * The viewer's own time for an instant shown in a resource's zone, e.g.
 * `11:00 your time` or `01:00 next day, your time`; null when the viewer's
 * clock shows the same time there, so no hint is needed.
 */
export function viewerTimeHint(
  instant: UtcDateTime | Date,
  resourceTimeZoneId: TimeZoneId,
  viewerTimeZoneId: TimeZoneId,
): string | null {
  const resource = wallClock(instant, resourceTimeZoneId);
  const viewer = wallClock(instant, viewerTimeZoneId);
  if (viewer.date === resource.date) {
    return viewer.time === resource.time ? null : `${viewer.time} your time`;
  }
  // ISO dates compare correctly as strings.
  const day = viewer.date > resource.date ? 'next day' : 'previous day';
  return `${viewer.time} ${day}, your time`;
}
