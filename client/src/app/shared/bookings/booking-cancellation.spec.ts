import { BookingListItem } from '../../core/api/models';
import { cancelAction, cancelConfirmation, cancellationMessage } from './booking-cancellation';

// 1 October 2026: Berlin is UTC+2.
const booking: BookingListItem = {
  id: 'b1',
  resourceId: 'r1',
  resourceName: 'Room A',
  resourceTimeZoneId: 'Europe/Berlin',
  userId: 'u1',
  userDisplayName: 'Ann',
  userEmail: 'ann@example.test',
  startUtc: '2026-10-01T08:00:00Z',
  endUtc: '2026-10-01T09:00:00Z',
  createdAtUtc: '2026-09-01T08:00:00Z',
};

const before = Date.parse('2026-10-01T07:00:00Z');
const during = Date.parse('2026-10-01T08:20:00Z');
const after = Date.parse('2026-10-01T09:00:00Z');

describe('cancelAction', () => {
  it('offers Cancel before the start, End now while under way, nothing after', () => {
    expect(cancelAction(booking, before)).toBe('Cancel');
    expect(cancelAction(booking, during)).toBe('End now');
    expect(cancelAction(booking, after)).toBeNull();
  });
});

describe('cancelConfirmation', () => {
  it('asks to cancel an upcoming booking, naming it in its resource’s time', () => {
    expect(cancelConfirmation(booking, 'Europe/Berlin', before)).toEqual({
      title: 'Cancel this booking?',
      message:
        'Room A, Thursday, 1 October 2026, 10:00–11:00 (Berlin time (UTC+2)). ' +
        'The time becomes free for others.',
      confirm: 'Cancel booking',
      dismiss: 'Keep it',
    });
  });

  it('asks to end a booking under way', () => {
    const question = cancelConfirmation(booking, 'Europe/Berlin', during);

    expect(question.title).toBe('End this booking now?');
    expect(question.message).toContain('The slots that have not started become free for others.');
    expect(question.confirm).toBe('End now');
  });

  it("says whose booking it is when it is someone else's", () => {
    expect(cancelConfirmation(booking, 'Europe/Berlin', before, 'Ann').message).toContain(
      '(Berlin time (UTC+2)). Booked by Ann. The time',
    );
  });
});

describe('cancellationMessage', () => {
  it('reports a booking cancelled completely', () => {
    expect(
      cancellationMessage({ cancelledCompletely: true, remainingBooking: null }, booking),
    ).toBe('Booking cancelled.');
  });

  it("reports when a booking under way now ends, in the resource's time", () => {
    const remainingBooking = { ...booking, endUtc: '2026-10-01T08:30:00Z' };

    expect(cancellationMessage({ cancelledCompletely: false, remainingBooking }, booking)).toBe(
      'Booking ended early: it now ends at 10:30.',
    );
  });
});
