import { Slot } from '../../core/api/models';
import { slotStatus, slotStatusLabel } from './slot-status';

function slot(state: Partial<Slot>): Slot {
  return {
    startUtc: '2026-10-01T08:00:00Z',
    endUtc: '2026-10-01T08:15:00Z',
    isBooked: false,
    isMine: false,
    isPast: false,
    bookingId: null,
    ...state,
  };
}

describe('slot status', () => {
  it.each<[Partial<Slot>, string, string]>([
    [{}, 'free', 'Free'],
    [{ isBooked: true }, 'booked', 'Booked'],
    [{ isBooked: true, isMine: true, bookingId: 'b1' }, 'mine', 'Your booking'],
    [{ isPast: true }, 'past', 'Past'],
    [{ isPast: true, isBooked: true }, 'past', 'Past · booked'],
    [{ isPast: true, isBooked: true, isMine: true }, 'past', 'Past · your booking'],
  ])('%o is %s ("%s")', (state, status, label) => {
    expect(slotStatus(slot(state))).toBe(status);
    expect(slotStatusLabel(slot(state))).toBe(label);
  });
});
