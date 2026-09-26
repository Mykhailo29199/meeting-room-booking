import { bookingState, describeBooking } from './booking-display';

describe('bookingState', () => {
  const booking = { startUtc: '2026-10-01T08:00:00Z', endUtc: '2026-10-01T09:00:00Z' };

  it.each([
    ['2026-10-01T07:59:59Z', 'upcoming'],
    ['2026-10-01T08:00:00Z', 'in-progress'],
    ['2026-10-01T08:59:59Z', 'in-progress'],
    ['2026-10-01T09:00:00Z', 'past'],
  ])('at %s it is %s', (now, state) => {
    expect(bookingState(booking, Date.parse(now))).toBe(state);
  });
});

describe('describeBooking', () => {
  // 1 October 2026: Berlin is UTC+2, London UTC+1.
  const booking = {
    startUtc: '2026-10-01T08:00:00Z',
    endUtc: '2026-10-01T09:30:00Z',
    resourceTimeZoneId: 'Europe/Berlin',
  };

  it("shows the booking in the resource's zone, like its schedule", () => {
    expect(describeBooking(booking, 'Europe/Berlin')).toEqual({
      date: 'Thursday, 1 October 2026',
      localDate: '2026-10-01',
      time: '10:00–11:30',
      zone: 'Berlin time (UTC+2)',
      viewerTime: null,
      duration: '1 h 30 min',
    });
  });

  it("adds the viewer's own start time when their clock differs", () => {
    expect(describeBooking(booking, 'Europe/London').viewerTime).toBe('09:00 your time');
  });

  it("takes the day from the resource's zone", () => {
    // 23:30 in Berlin is still 30 September in UTC.
    const late = { ...booking, startUtc: '2026-09-30T21:30:00Z', endUtc: '2026-09-30T21:45:00Z' };

    expect(describeBooking(late, 'Europe/Berlin').localDate).toBe('2026-09-30');
    const early = { ...booking, startUtc: '2026-09-30T22:00:00Z', endUtc: '2026-09-30T22:15:00Z' };
    expect(describeBooking(early, 'Europe/Berlin').localDate).toBe('2026-10-01');
  });
});
