import { InjectionToken } from '@angular/core';
import { TimeZoneId } from '../api/models';

/**
 * The viewer's own IANA time zone (the browser's). Used only for the "your
 * time" hints; resource times are always shown in the resource's zone. A
 * token, so tests can pick a zone instead of depending on the machine's.
 */
export const VIEWER_TIME_ZONE = new InjectionToken<TimeZoneId>('VIEWER_TIME_ZONE', {
  providedIn: 'root',
  factory: () => Intl.DateTimeFormat().resolvedOptions().timeZone,
});
