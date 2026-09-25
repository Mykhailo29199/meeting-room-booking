import { LocalDate } from '../api/models';

// A LocalDate (`YYYY-MM-DD`) is a calendar day with no time zone: the day of
// a resource's schedule in the resource's local time. Arithmetic on it runs
// in UTC, where every day has 24 hours, so daylight saving cannot shift it.

const LOCAL_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

const longDateFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});

/** True for an existing calendar day written as `YYYY-MM-DD` (e.g. from the URL). */
export function isLocalDate(value: string | null | undefined): value is LocalDate {
  const match = value ? LOCAL_DATE.exec(value) : null;
  if (!match) {
    return false;
  }
  const [year, month, day] = match.slice(1).map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  // Date.UTC rolls 2026-02-30 over to March; a real day survives unchanged.
  return (
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
  );
}

/** The day `days` days after `date` (before, if negative). */
export function addDays(date: LocalDate, days: number): LocalDate {
  const result = toUtcMidnight(date);
  result.setUTCDate(result.getUTCDate() + days);
  return result.toISOString().slice(0, 10);
}

/** E.g. `Thursday, 1 October 2026`. */
export function formatLongDate(date: LocalDate): string {
  return longDateFormat.format(toUtcMidnight(date));
}

/**
 * The value for Material's date picker, which works with `Date`s in the
 * browser's zone: midnight of that day there. Only its calendar day matters.
 */
export function toPickerDate(date: LocalDate): Date {
  const [year, month, day] = date.split('-').map(Number);
  return new Date(year, month - 1, day);
}

/** The calendar day the user picked (read in the browser's zone, as the picker set it). */
export function fromPickerDate(date: Date): LocalDate {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

function toUtcMidnight(date: LocalDate): Date {
  return new Date(`${date}T00:00:00Z`);
}
