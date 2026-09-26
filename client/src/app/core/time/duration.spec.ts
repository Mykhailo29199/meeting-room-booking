import { formatDuration, minutesBetween } from './duration';

describe('minutesBetween', () => {
  it('counts whole minutes between two instants', () => {
    expect(minutesBetween('2026-10-01T08:00:00Z', '2026-10-01T09:15:00Z')).toBe(75);
  });
});

describe('formatDuration', () => {
  it.each([
    [15, '15 min'],
    [60, '1 h'],
    [105, '1 h 45 min'],
    [600, '10 h'],
  ])('%i minutes is %s', (minutes, text) => {
    expect(formatDuration(minutes)).toBe(text);
  });
});
