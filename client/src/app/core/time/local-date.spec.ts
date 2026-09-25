import { addDays, formatLongDate, fromPickerDate, isLocalDate, toPickerDate } from './local-date';

describe('isLocalDate', () => {
  it.each(['2026-10-01', '2028-02-29', '2026-12-31'])('accepts %s', (value) => {
    expect(isLocalDate(value)).toBe(true);
  });

  it.each([
    '2026-02-30',
    '2026-13-01',
    '2026-2-3',
    '2026-10-01T00:00',
    'today',
    '',
    null,
    undefined,
  ])('rejects %s', (value) => {
    expect(isLocalDate(value)).toBe(false);
  });
});

describe('addDays', () => {
  it('moves across months and years', () => {
    expect(addDays('2026-10-01', -1)).toBe('2026-09-30');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2028-02-28', 1)).toBe('2028-02-29');
  });

  it('is not shifted by daylight saving time', () => {
    // Clocks change in Europe on 29 March and 25 October 2026.
    expect(addDays('2026-03-28', 1)).toBe('2026-03-29');
    expect(addDays('2026-03-29', 1)).toBe('2026-03-30');
    expect(addDays('2026-10-25', -1)).toBe('2026-10-24');
  });
});

describe('formatLongDate', () => {
  it('writes the weekday and the date in full', () => {
    expect(formatLongDate('2026-10-01')).toBe('Thursday, 1 October 2026');
  });
});

describe('date picker values', () => {
  it('keep the calendar day both ways', () => {
    for (const date of ['2026-10-01', '2026-03-29', '2026-10-25', '2027-01-01']) {
      expect(fromPickerDate(toPickerDate(date))).toBe(date);
    }
  });

  it("read the day the user picked in the browser's zone", () => {
    expect(fromPickerDate(new Date(2026, 9, 1))).toBe('2026-10-01');
    expect(fromPickerDate(new Date(2026, 9, 1, 23, 59))).toBe('2026-10-01');
  });
});
