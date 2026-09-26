import { FormControl, FormGroup } from '@angular/forms';
import { Resource } from '../../../core/api/models';
import {
  closesAfterOpening,
  filterTimeZones,
  formValueOf,
  knownTimeZone,
  onQuarterHour,
  toResourceRequest,
} from './resource-form';

const resource: Resource = {
  id: 'r1',
  name: 'Room A',
  capacity: 8,
  timeZoneId: 'Europe/Berlin',
  opensAt: '08:00:00',
  closesAt: '18:30:00',
  isActive: true,
  version: 'v1',
};

describe('formValueOf', () => {
  it("starts a new resource in the admin's zone with office hours", () => {
    expect(formValueOf(null, 'Europe/Kiev')).toEqual({
      name: '',
      capacity: null,
      timeZoneId: 'Europe/Kiev',
      opensAt: '08:00',
      closesAt: '18:00',
    });
  });

  it('fills in an existing resource, with times as the time input shows them', () => {
    expect(formValueOf(resource, 'Europe/Kiev')).toEqual({
      name: 'Room A',
      capacity: 8,
      timeZoneId: 'Europe/Berlin',
      opensAt: '08:00',
      closesAt: '18:30',
    });
  });
});

describe('toResourceRequest', () => {
  it('trims the name and gives times the seconds the API expects', () => {
    expect(
      toResourceRequest({
        name: '  Room A ',
        capacity: 8,
        timeZoneId: 'Europe/Berlin',
        opensAt: '08:00',
        closesAt: '18:30',
      }),
    ).toEqual({
      name: 'Room A',
      capacity: 8,
      timeZoneId: 'Europe/Berlin',
      opensAt: '08:00:00',
      closesAt: '18:30:00',
    });
  });
});

describe('onQuarterHour', () => {
  it.each(['00:00', '08:15', '17:30', '23:45', ''])('accepts %s', (time) => {
    expect(onQuarterHour(new FormControl(time))).toBeNull();
  });

  it.each(['08:10', '17:59', '09:01'])('rejects %s', (time) => {
    expect(onQuarterHour(new FormControl(time))).toEqual({ quarterHour: true });
  });
});

describe('closesAfterOpening', () => {
  const hours = (opensAt: string, closesAt: string) =>
    closesAfterOpening(
      new FormGroup({ opensAt: new FormControl(opensAt), closesAt: new FormControl(closesAt) }),
    );

  it('accepts closing after opening', () => {
    expect(hours('08:00', '08:15')).toBeNull();
  });

  it('rejects closing at or before opening', () => {
    expect(hours('08:00', '08:00')).toEqual({ closesBeforeOpening: true });
    expect(hours('18:00', '08:00')).toEqual({ closesBeforeOpening: true });
  });

  it('leaves incomplete times to the fields', () => {
    expect(hours('', '08:00')).toBeNull();
  });
});

describe('knownTimeZone', () => {
  const validator = knownTimeZone(['Europe/Berlin', 'America/New_York']);

  it('accepts a zone from the list (and leaves an empty field to "required")', () => {
    expect(validator(new FormControl('Europe/Berlin'))).toBeNull();
    expect(validator(new FormControl(''))).toBeNull();
  });

  it('rejects anything else', () => {
    expect(validator(new FormControl('Berlin'))).toEqual({ timeZone: true });
  });
});

describe('filterTimeZones', () => {
  const zones = ['America/New_York', 'Europe/Berlin', 'Europe/London', 'Asia/Tokyo'];

  it('finds zones by any part of their id, as typed', () => {
    expect(filterTimeZones(zones, 'europe')).toEqual(['Europe/Berlin', 'Europe/London']);
    expect(filterTimeZones(zones, 'new york')).toEqual(['America/New_York']);
  });

  it('offers the first ones when nothing is typed', () => {
    expect(filterTimeZones(zones, '', 2)).toEqual(['America/New_York', 'Europe/Berlin']);
  });
});
