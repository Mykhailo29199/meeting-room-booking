import {
  formatLocalTime,
  formatTime,
  localDateOf,
  viewerTimeHint,
  zoneLabel,
  zoneOffset,
} from './resource-time';

// Berlin is UTC+2 until 25 October 2026 and UTC+1 after; the tests pin the
// zones so they pass on any machine, whatever its own zone.

describe('formatTime', () => {
  it("shows the time in the resource's zone, not the browser's", () => {
    expect(formatTime('2026-10-01T08:00:00Z', 'Europe/Berlin')).toBe('10:00');
    expect(formatTime('2026-10-01T08:00:00Z', 'America/New_York')).toBe('04:00');
    expect(formatTime('2026-10-01T08:00:00Z', 'Asia/Kolkata')).toBe('13:30');
  });

  it('follows daylight saving time', () => {
    expect(formatTime('2026-11-10T08:00:00Z', 'Europe/Berlin')).toBe('09:00');
    // Clocks jump from 02:00 to 03:00 on 29 March 2026.
    expect(formatTime('2026-03-29T00:45:00Z', 'Europe/Berlin')).toBe('01:45');
    expect(formatTime('2026-03-29T01:00:00Z', 'Europe/Berlin')).toBe('03:00');
  });

  it('uses a 24-hour clock with midnight as 00:00', () => {
    expect(formatTime('2026-10-01T22:00:00Z', 'Europe/Berlin')).toBe('00:00');
    expect(formatTime('2026-10-01T13:15:00Z', 'Europe/Berlin')).toBe('15:15');
  });
});

describe('localDateOf', () => {
  it("gives the day in the resource's zone", () => {
    expect(localDateOf('2026-10-01T21:59:00Z', 'Europe/Berlin')).toBe('2026-10-01');
    expect(localDateOf('2026-10-01T22:00:00Z', 'Europe/Berlin')).toBe('2026-10-02');
    expect(localDateOf('2026-10-01T02:00:00Z', 'America/New_York')).toBe('2026-09-30');
  });
});

describe('formatLocalTime', () => {
  it('drops the seconds', () => {
    expect(formatLocalTime('08:00:00')).toBe('08:00');
    expect(formatLocalTime('17:45:00')).toBe('17:45');
  });
});

describe('zoneOffset', () => {
  it.each([
    ['Europe/Berlin', '2026-10-01T08:00:00Z', 'UTC+2'],
    ['Europe/Berlin', '2026-11-10T08:00:00Z', 'UTC+1'],
    ['America/New_York', '2026-10-01T08:00:00Z', 'UTC-4'],
    ['Asia/Kolkata', '2026-10-01T08:00:00Z', 'UTC+5:30'],
    ['UTC', '2026-10-01T08:00:00Z', 'UTC'],
  ])('%s at %s is %s', (zone, at, expected) => {
    expect(zoneOffset(zone, at)).toBe(expected);
  });
});

describe('zoneLabel', () => {
  it('names the place and the offset on that day', () => {
    expect(zoneLabel('Europe/Berlin', '2026-10-01T08:00:00Z')).toBe('Berlin time (UTC+2)');
    expect(zoneLabel('Europe/Berlin', '2026-11-10T08:00:00Z')).toBe('Berlin time (UTC+1)');
    expect(zoneLabel('America/New_York', '2026-10-01T08:00:00Z')).toBe('New York time (UTC-4)');
    expect(zoneLabel('America/Argentina/Buenos_Aires', '2026-10-01T08:00:00Z')).toBe(
      'Buenos Aires time (UTC-3)',
    );
  });

  it('labels zones that are not a place by their offset', () => {
    expect(zoneLabel('UTC', '2026-10-01T08:00:00Z')).toBe('UTC');
    expect(zoneLabel('Etc/GMT-3', '2026-10-01T08:00:00Z')).toBe('UTC+3');
  });
});

describe('viewerTimeHint', () => {
  it('is null when the viewer is in the same zone', () => {
    expect(viewerTimeHint('2026-10-01T08:00:00Z', 'Europe/Berlin', 'Europe/Berlin')).toBeNull();
  });

  it('is null when another zone shows the same time', () => {
    expect(viewerTimeHint('2026-10-01T08:00:00Z', 'Europe/Berlin', 'Europe/Paris')).toBeNull();
  });

  it("gives the viewer's time when it differs", () => {
    expect(viewerTimeHint('2026-10-01T08:00:00Z', 'Europe/Berlin', 'Europe/London')).toBe(
      '09:00 your time',
    );
  });

  it('says when the viewer is already on the next day', () => {
    // 23:30 in Berlin is 06:30 the next morning in Tokyo.
    expect(viewerTimeHint('2026-10-01T21:30:00Z', 'Europe/Berlin', 'Asia/Tokyo')).toBe(
      '06:30 next day, your time',
    );
  });

  it('says when the viewer is still on the previous day', () => {
    // 00:30 in Berlin is 18:30 the evening before in New York.
    expect(viewerTimeHint('2026-09-30T22:30:00Z', 'Europe/Berlin', 'America/New_York')).toBe(
      '18:30 previous day, your time',
    );
  });
});
