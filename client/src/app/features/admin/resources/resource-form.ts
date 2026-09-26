import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { CreateResourceRequest, LocalTime, Resource } from '../../../core/api/models';

// The resource form's rules mirror the server's (the domain `Resource`), so
// most mistakes are caught before sending; the server still decides and its
// message is shown when it disagrees.

export const MAX_NAME_LENGTH = 100;

/** What the form holds: times as `HH:mm`, as `<input type="time">` gives them. */
export interface ResourceFormValue {
  name: string;
  capacity: number | null;
  timeZoneId: string;
  opensAt: string;
  closesAt: string;
}

/** Every IANA time zone the browser knows, for the time zone field. */
export function knownTimeZones(): string[] {
  return Intl.supportedValuesOf('timeZone');
}

/** The form's starting values: an existing resource, or defaults for a new one. */
export function formValueOf(
  resource: Resource | null,
  defaultTimeZoneId: string,
): ResourceFormValue {
  if (!resource) {
    return {
      name: '',
      capacity: null,
      timeZoneId: defaultTimeZoneId,
      opensAt: '08:00',
      closesAt: '18:00',
    };
  }
  return {
    name: resource.name,
    capacity: resource.capacity,
    timeZoneId: resource.timeZoneId,
    opensAt: resource.opensAt.slice(0, 5),
    closesAt: resource.closesAt.slice(0, 5),
  };
}

/** The request body; local times get the seconds the API's `TimeOnly` expects. */
export function toResourceRequest(value: ResourceFormValue): CreateResourceRequest {
  return {
    name: value.name.trim(),
    capacity: value.capacity ?? 0,
    timeZoneId: value.timeZoneId,
    opensAt: toLocalTime(value.opensAt),
    closesAt: toLocalTime(value.closesAt),
  };
}

function toLocalTime(time: string): LocalTime {
  return `${time}:00`;
}

/** Minutes since midnight of `HH:mm`, or null if it is not a time. */
function minutesOf(time: string): number | null {
  const match = /^(\d{2}):(\d{2})$/.exec(time);
  if (!match) {
    return null;
  }
  const [hours, minutes] = [Number(match[1]), Number(match[2])];
  return hours < 24 && minutes < 60 ? hours * 60 + minutes : null;
}

/** `{ quarterHour: true }` unless the time is on the 15-minute grid, like every slot. */
export const onQuarterHour: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const minutes = minutesOf(control.value as string);
  return minutes === null || minutes % 15 === 0 ? null : { quarterHour: true };
};

/** `{ timeZone: true }` unless the value is one of the known zones. */
export function knownTimeZone(zones: readonly string[]): ValidatorFn {
  const known = new Set(zones);
  return (control) =>
    !control.value || known.has(control.value as string) ? null : { timeZone: true };
}

/** On the form: `{ closesBeforeOpening: true }` unless it closes after it opens. */
export const closesAfterOpening: ValidatorFn = (form: AbstractControl): ValidationErrors | null => {
  const opens = minutesOf(form.get('opensAt')?.value as string);
  const closes = minutesOf(form.get('closesAt')?.value as string);
  return opens === null || closes === null || closes > opens ? null : { closesBeforeOpening: true };
};

/** Zones whose id contains the typed text, for the autocomplete (at most `limit`). */
export function filterTimeZones(zones: readonly string[], text: string, limit = 50): string[] {
  const needle = text.trim().toLowerCase().replaceAll(' ', '_');
  return (needle ? zones.filter((zone) => zone.toLowerCase().includes(needle)) : zones).slice(
    0,
    limit,
  );
}
