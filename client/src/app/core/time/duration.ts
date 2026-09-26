import { UtcDateTime } from '../api/models';

/** Whole minutes between two instants. */
export function minutesBetween(startUtc: UtcDateTime, endUtc: UtcDateTime): number {
  return Math.round((Date.parse(endUtc) - Date.parse(startUtc)) / 60_000);
}

/** E.g. `15 min`, `1 h`, `1 h 45 min`. */
export function formatDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) {
    return `${rest} min`;
  }
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}
