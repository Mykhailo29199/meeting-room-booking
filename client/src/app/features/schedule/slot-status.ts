import { Slot } from '../../core/api/models';

/** What a slot looks like in the schedule. */
export type SlotStatus = 'free' | 'booked' | 'mine' | 'past';

/** A slot that has started can no longer be booked, whoever holds it. */
export function slotStatus(slot: Slot): SlotStatus {
  if (slot.isPast) {
    return 'past';
  }
  if (slot.isMine) {
    return 'mine';
  }
  return slot.isBooked ? 'booked' : 'free';
}

/**
 * The status in words, shown next to every slot so it is never conveyed by
 * colour alone. Past slots still say whose they were.
 */
export function slotStatusLabel(slot: Slot): string {
  switch (slotStatus(slot)) {
    case 'free':
      return 'Free';
    case 'booked':
      return 'Booked';
    case 'mine':
      return 'Your booking';
    case 'past':
      return slot.isMine ? 'Past · your booking' : slot.isBooked ? 'Past · booked' : 'Past';
  }
}
