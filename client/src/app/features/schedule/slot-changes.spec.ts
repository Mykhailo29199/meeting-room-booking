import { Slot, SlotChange } from '../../core/api/models';
import { applySlotChanges } from './slot-changes';

function slot(time: string, state: Partial<Slot> = {}): Slot {
  const start = new Date(`2026-10-01T${time}:00Z`);
  return {
    startUtc: start.toISOString().replace('.000', ''),
    endUtc: new Date(start.getTime() + 15 * 60_000).toISOString().replace('.000', ''),
    isBooked: false,
    isMine: false,
    isPast: false,
    bookingId: null,
    ...state,
  };
}

function change(time: string, isBooked: boolean): SlotChange {
  const s = slot(time);
  return { startUtc: s.startUtc, endUtc: s.endUtc, isBooked };
}

describe('applySlotChanges', () => {
  const slots = [
    slot('08:00'),
    slot('08:15', { isBooked: true }),
    slot('08:30', { isBooked: true, isMine: true, bookingId: 'b1' }),
    slot('08:45'),
  ];

  it("marks newly booked slots as someone else's", () => {
    const result = applySlotChanges(slots, [change('08:00', true)]);

    expect(result[0]).toEqual({ ...slots[0], isBooked: true, isMine: false, bookingId: null });
  });

  it('frees released slots, including the viewer’s own', () => {
    const result = applySlotChanges(slots, [change('08:15', false), change('08:30', false)]);

    expect(result[1]).toEqual({ ...slots[1], isBooked: false });
    expect(result[2]).toEqual({ ...slots[2], isBooked: false, isMine: false, bookingId: null });
  });

  it('keeps the viewer’s own booking when a late event says it is booked', () => {
    const result = applySlotChanges(slots, [change('08:30', true)]);

    expect(result[2]).toBe(slots[2]);
  });

  it('leaves other slots and slots of other days untouched', () => {
    const result = applySlotChanges(slots, [
      change('08:00', true),
      { ...change('08:00', true), startUtc: '2026-10-02T08:00:00Z' },
    ]);

    expect(result.slice(1)).toEqual(slots.slice(1));
    expect(result[3]).toBe(slots[3]);
  });

  it('matches the same instant written differently', () => {
    const result = applySlotChanges(slots, [
      { startUtc: '2026-10-01T08:45:00.000Z', endUtc: '2026-10-01T09:00:00.000Z', isBooked: true },
    ]);

    expect(result[3].isBooked).toBe(true);
  });
});
