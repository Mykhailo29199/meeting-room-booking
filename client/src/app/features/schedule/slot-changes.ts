import { Slot, SlotChange } from '../../core/api/models';

/**
 * Applies live changes (`SlotsChanged`) to a schedule's slots. Changes to
 * slots that are not in the list (another day) are ignored.
 *
 * An event never says who booked, so a slot that becomes booked is shown as
 * someone else's; after the user's own booking the page reloads to learn
 * which slots are theirs. A slot that is already booked keeps what the last
 * load said, so a late event about the user's own booking cannot hide it.
 * A freed slot loses its booking entirely.
 */
export function applySlotChanges(slots: readonly Slot[], changes: readonly SlotChange[]): Slot[] {
  const changed = new Map(changes.map((change) => [Date.parse(change.startUtc), change.isBooked]));
  return slots.map((slot) => {
    const isBooked = changed.get(Date.parse(slot.startUtc));
    if (isBooked === undefined || isBooked === slot.isBooked) {
      return slot;
    }
    return { ...slot, isBooked, isMine: false, bookingId: null };
  });
}
