import { Slot } from '../../core/api/models';
import {
  clickSlot,
  isInRange,
  keepIfAvailable,
  reachableSlots,
  selectableStarts,
  selectEnd,
  selectStart,
  SlotRange,
} from './slot-selection';

/** A slot starting at `HH:mm` UTC on 1 October 2026, 15 minutes long. */
function slot(time: string, state: Partial<Slot> = {}): Slot {
  const start = new Date(`2026-10-01T${time}:00Z`);
  const end = new Date(start.getTime() + 15 * 60_000);
  return {
    startUtc: start.toISOString().replace('.000', ''),
    endUtc: end.toISOString().replace('.000', ''),
    isBooked: false,
    isMine: false,
    isPast: false,
    bookingId: null,
    ...state,
  };
}

const at = (time: string) => `2026-10-01T${time}:00Z`;
const range = (start: string, end: string): SlotRange => ({ startUtc: at(start), endUtc: at(end) });

// 07:45 past | 08:00 08:15 08:30 free | 08:45 booked | 09:00 09:15 free |
// (09:30 closed) | 09:45 free | 10:00 mine
const slots: Slot[] = [
  slot('07:45', { isPast: true }),
  slot('08:00'),
  slot('08:15'),
  slot('08:30'),
  slot('08:45', { isBooked: true }),
  slot('09:00'),
  slot('09:15'),
  slot('09:45'),
  slot('10:00', { isBooked: true, isMine: true, bookingId: 'b1' }),
];

const starts = (list: Slot[]) => list.map((s) => s.startUtc.slice(11, 16));

describe('selectableStarts', () => {
  it('offers only free slots that have not started', () => {
    expect(starts(selectableStarts(slots))).toEqual([
      '08:00',
      '08:15',
      '08:30',
      '09:00',
      '09:15',
      '09:45',
    ]);
  });
});

describe('reachableSlots', () => {
  it('runs until the first slot that is not free', () => {
    expect(starts(reachableSlots(slots, at('08:00')))).toEqual(['08:00', '08:15', '08:30']);
    expect(starts(reachableSlots(slots, at('08:30')))).toEqual(['08:30']);
  });

  it('stops at a gap in the schedule', () => {
    expect(starts(reachableSlots(slots, at('09:00')))).toEqual(['09:00', '09:15']);
  });

  it('is empty for a start that is not a free slot', () => {
    expect(reachableSlots(slots, at('07:45'))).toEqual([]);
    expect(reachableSlots(slots, at('08:45'))).toEqual([]);
    expect(reachableSlots(slots, at('10:00'))).toEqual([]);
    expect(reachableSlots(slots, at('12:00'))).toEqual([]);
  });

  it('matches the same instant written differently', () => {
    expect(starts(reachableSlots(slots, '2026-10-01T08:00:00.000Z'))).toEqual([
      '08:00',
      '08:15',
      '08:30',
    ]);
  });
});

describe('selectStart', () => {
  it('starts with the one slot', () => {
    expect(selectStart(slots, null, at('08:15'))).toEqual(range('08:15', '08:30'));
  });

  it('keeps the end while the range stays free', () => {
    expect(selectStart(slots, range('08:00', '08:45'), at('08:15'))).toEqual(
      range('08:15', '08:45'),
    );
  });

  it('drops an end that the new start cannot reach', () => {
    expect(selectStart(slots, range('08:00', '08:45'), at('09:00'))).toEqual(
      range('09:00', '09:15'),
    );
  });

  it('ignores a start that is not free', () => {
    expect(selectStart(slots, range('08:00', '08:15'), at('08:45'))).toEqual(
      range('08:00', '08:15'),
    );
  });
});

describe('selectEnd', () => {
  it('extends and shrinks the range', () => {
    expect(selectEnd(slots, range('08:00', '08:15'), at('08:45'))).toEqual(range('08:00', '08:45'));
    expect(selectEnd(slots, range('08:00', '08:45'), at('08:30'))).toEqual(range('08:00', '08:30'));
  });

  it('ignores an end beyond a booked slot', () => {
    expect(selectEnd(slots, range('08:00', '08:15'), at('09:15'))).toEqual(range('08:00', '08:15'));
  });

  it('needs a start first', () => {
    expect(selectEnd(slots, null, at('08:30'))).toBeNull();
  });
});

describe('clickSlot', () => {
  it('chooses a free slot', () => {
    expect(clickSlot(slots, null, at('08:00'))).toEqual(range('08:00', '08:15'));
  });

  it('extends the range to a later reachable slot, and shrinks it back', () => {
    const extended = clickSlot(slots, range('08:00', '08:15'), at('08:30'));
    expect(extended).toEqual(range('08:00', '08:45'));
    expect(clickSlot(slots, extended, at('08:15'))).toEqual(range('08:00', '08:30'));
  });

  it('starts over at a slot that cannot extend the range', () => {
    // After a booked slot, and before the start.
    expect(clickSlot(slots, range('08:00', '08:15'), at('09:00'))).toEqual(range('09:00', '09:15'));
    expect(clickSlot(slots, range('08:15', '08:30'), at('08:00'))).toEqual(range('08:00', '08:15'));
  });

  it('starts over at the first slot of a longer range', () => {
    expect(clickSlot(slots, range('08:00', '08:45'), at('08:00'))).toEqual(range('08:00', '08:15'));
  });

  it('clears the choice when its only slot is clicked again', () => {
    expect(clickSlot(slots, range('08:00', '08:15'), at('08:00'))).toBeNull();
  });

  it('ignores slots that are not free', () => {
    const current = range('08:00', '08:15');
    for (const time of ['07:45', '08:45', '10:00']) {
      expect(clickSlot(slots, current, at(time))).toBe(current);
    }
  });
});

describe('keepIfAvailable', () => {
  it('keeps a range that is still free', () => {
    const current = range('08:00', '08:45');
    expect(keepIfAvailable(slots, current)).toBe(current);
  });

  it('drops a range of which any slot was taken', () => {
    const taken = slots.map((s) => (s.startUtc === at('08:30') ? { ...s, isBooked: true } : s));
    expect(keepIfAvailable(taken, range('08:00', '08:45'))).toBeNull();
    expect(keepIfAvailable(taken, range('08:00', '08:30'))).toEqual(range('08:00', '08:30'));
  });

  it('drops a range whose slots are not in the schedule (another day)', () => {
    expect(keepIfAvailable(slots, range('12:00', '12:15'))).toBeNull();
  });

  it('keeps nothing when nothing was chosen', () => {
    expect(keepIfAvailable(slots, null)).toBeNull();
  });
});

describe('isInRange', () => {
  it('covers the slots from the start up to, not including, the end', () => {
    const current = range('08:00', '08:30');
    expect(slots.filter((s) => isInRange(s, current)).map((s) => s.startUtc)).toEqual([
      at('08:00'),
      at('08:15'),
    ]);
    expect(isInRange(slots[1], null)).toBe(false);
  });
});
