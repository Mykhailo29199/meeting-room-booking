import { Slot, UtcDateTime } from '../../core/api/models';
import { slotStatus } from './slot-status';

// Choosing a time range to book. A range is one or more consecutive free
// slots, identified by the first slot's `startUtc` and the last slot's
// `endUtc` exactly as the schedule sent them: those two values are what the
// booking request sends back. These rules only guide the user; the server
// decides, and a slot taken meanwhile is a 409.

/** The chosen range: from the start of its first slot to the end of its last. */
export interface SlotRange {
  startUtc: UtcDateTime;
  endUtc: UtcDateTime;
}

function isFree(slot: Slot): boolean {
  return slotStatus(slot) === 'free';
}

function sameInstant(a: UtcDateTime, b: UtcDateTime): boolean {
  return Date.parse(a) === Date.parse(b);
}

/** Slots a booking can start at: free ones that have not started. */
export function selectableStarts(slots: readonly Slot[]): Slot[] {
  return slots.filter(isFree);
}

/**
 * The slots a booking starting at `startUtc` can extend over: that slot and
 * each following one up to the first that is not free or not adjacent (the
 * schedule may skip closed hours). Empty if `startUtc` is not a free slot.
 * A booking may end at the end of any of them.
 */
export function reachableSlots(slots: readonly Slot[], startUtc: UtcDateTime): Slot[] {
  const first = slots.findIndex((slot) => sameInstant(slot.startUtc, startUtc));
  if (first < 0 || !isFree(slots[first])) {
    return [];
  }
  const run = [slots[first]];
  for (let i = first + 1; i < slots.length; i++) {
    if (!isFree(slots[i]) || !sameInstant(slots[i].startUtc, slots[i - 1].endUtc)) {
      break;
    }
    run.push(slots[i]);
  }
  return run;
}

/**
 * Chooses the start. The end is kept if the range is still all free,
 * otherwise the range becomes the start slot alone. A start that is not a
 * free slot changes nothing.
 */
export function selectStart(
  slots: readonly Slot[],
  current: SlotRange | null,
  startUtc: UtcDateTime,
): SlotRange | null {
  const run = reachableSlots(slots, startUtc);
  if (run.length === 0) {
    return current;
  }
  const keepEnd = current !== null && run.some((slot) => sameInstant(slot.endUtc, current.endUtc));
  return { startUtc: run[0].startUtc, endUtc: keepEnd ? current.endUtc : run[0].endUtc };
}

/** Chooses the end; only an end reachable from the start through free slots is taken. */
export function selectEnd(
  slots: readonly Slot[],
  current: SlotRange | null,
  endUtc: UtcDateTime,
): SlotRange | null {
  if (!current) {
    return null;
  }
  const last = reachableSlots(slots, current.startUtc).find((slot) =>
    sameInstant(slot.endUtc, endUtc),
  );
  return last ? { startUtc: current.startUtc, endUtc: last.endUtc } : current;
}

/**
 * A click on a slot in the list: a free slot after the start and reachable
 * from it becomes the range's last slot; clicking the only chosen slot again
 * clears the choice; any other free slot starts a new range. Clicks on slots
 * that are not free change nothing.
 */
export function clickSlot(
  slots: readonly Slot[],
  current: SlotRange | null,
  startUtc: UtcDateTime,
): SlotRange | null {
  const clicked = slots.find((slot) => sameInstant(slot.startUtc, startUtc));
  if (!clicked || !isFree(clicked)) {
    return current;
  }
  if (current) {
    if (
      sameInstant(current.startUtc, clicked.startUtc) &&
      sameInstant(current.endUtc, clicked.endUtc)
    ) {
      return null;
    }
    const later = reachableSlots(slots, current.startUtc).slice(1);
    if (later.some((slot) => sameInstant(slot.startUtc, clicked.startUtc))) {
      return { startUtc: current.startUtc, endUtc: clicked.endUtc };
    }
  }
  return { startUtc: clicked.startUtc, endUtc: clicked.endUtc };
}

/**
 * The range if all its slots are still free in a newer schedule (after a
 * reload or a live update), otherwise null: never keep a choice that can no
 * longer be booked.
 */
export function keepIfAvailable(
  slots: readonly Slot[],
  current: SlotRange | null,
): SlotRange | null {
  if (!current) {
    return null;
  }
  const run = reachableSlots(slots, current.startUtc);
  return run.some((slot) => sameInstant(slot.endUtc, current.endUtc)) ? current : null;
}

/** True if the slot lies inside the range. */
export function isInRange(slot: Slot, range: SlotRange | null): boolean {
  if (!range) {
    return false;
  }
  const start = Date.parse(slot.startUtc);
  return start >= Date.parse(range.startUtc) && start < Date.parse(range.endUtc);
}

/** Whole minutes between two instants. */
export function minutesBetween(startUtc: UtcDateTime, endUtc: UtcDateTime): number {
  return Math.round((Date.parse(endUtc) - Date.parse(startUtc)) / 60_000);
}

/** E.g. `15 min`, `1 h`, `1 h 45 min`. */
export function formatDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) {
    return `${rest} min`;
  }
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}
