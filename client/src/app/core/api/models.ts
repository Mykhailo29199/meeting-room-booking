/**
 * The API's DTOs as the client receives them (JSON, camelCase). Mirrors the
 * backend records in `src/MeetingRoomBooking.Application` and
 * `src/MeetingRoomBooking.Api`; change both in the same commit.
 */

/** A GUID as a string. */
export type Guid = string;

/**
 * A UTC instant in ISO 8601 with a trailing `Z`, e.g. `2026-10-01T08:00:00Z`.
 * Kept as the string the API sent: to book, it is sent back unchanged.
 */
export type UtcDateTime = string;

/** A calendar day, `YYYY-MM-DD` (C# `DateOnly`). */
export type LocalDate = string;

/** A local wall-clock time, `HH:mm:ss` (C# `TimeOnly`). */
export type LocalTime = string;

/** An IANA time zone id, e.g. `Europe/Berlin`. */
export type TimeZoneId = string;

export const Roles = {
  User: 'User',
  Admin: 'Admin',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];

// --- Auth -------------------------------------------------------------------

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

/** A signed-in user: the bearer token and who it belongs to. */
export interface AuthResult {
  accessToken: string;
  /** When the token stops being accepted; there is no refresh token. */
  expiresAtUtc: UtcDateTime;
  userId: string;
  email: string;
  displayName: string;
  roles: Role[];
}

/** `GET /api/auth/me`: whose token this is. */
export interface CurrentUser {
  userId: string;
  email: string;
  displayName: string;
  roles: Role[];
}

// --- Resources --------------------------------------------------------------

export interface Resource {
  id: Guid;
  name: string;
  capacity: number;
  timeZoneId: TimeZoneId;
  /** Local opening time in `timeZoneId`. */
  opensAt: LocalTime;
  /** Local closing time in `timeZoneId`. */
  closesAt: LocalTime;
  /** False once an admin has removed it (only admins see removed resources). */
  isActive: boolean;
  /** Changes on every edit; send it back with an update. */
  version: Guid;
}

export interface CreateResourceRequest {
  name: string;
  capacity: number;
  timeZoneId: TimeZoneId;
  opensAt: LocalTime;
  closesAt: LocalTime;
}

export interface UpdateResourceRequest extends CreateResourceRequest {
  /** The `version` the edit is based on; a stale one is rejected with 409. */
  version: Guid;
}

/** What removing a resource did to its bookings. */
export interface ResourceRemovalResult {
  /** Future bookings deleted, their slots freed. */
  cancelledBookings: number;
  /** Bookings under way cut short. */
  shortenedBookings: number;
}

// --- Schedule ---------------------------------------------------------------

/** One day of a resource's schedule, in the resource's local time. */
export interface ResourceSchedule {
  resourceId: Guid;
  resourceName: string;
  timeZoneId: TimeZoneId;
  localDate: LocalDate;
  isActive: boolean;
  slots: Slot[];
}

/** One 15-minute slot. Who booked someone else's slot is never revealed. */
export interface Slot {
  startUtc: UtcDateTime;
  endUtc: UtcDateTime;
  isBooked: boolean;
  isMine: boolean;
  isPast: boolean;
  /** Set only on the viewer's own bookings, so they can cancel them. */
  bookingId: Guid | null;
}

// --- Bookings ---------------------------------------------------------------

/** Times are the `startUtc` / `endUtc` values taken unchanged from the schedule. */
export interface CreateBookingRequest {
  resourceId: Guid;
  startUtc: UtcDateTime;
  endUtc: UtcDateTime;
}

export interface Booking {
  id: Guid;
  resourceId: Guid;
  userId: string;
  startUtc: UtcDateTime;
  endUtc: UtcDateTime;
}

/** What cancelling did. */
export interface CancellationResult {
  /** True if the booking had not started and is gone. */
  cancelledCompletely: boolean;
  /** If it was under way: what is left of it (its end moved back). */
  remainingBooking: Booking | null;
}

/** A booking as shown in lists. User fields are null if the account no longer exists. */
export interface BookingListItem {
  id: Guid;
  resourceId: Guid;
  resourceName: string;
  resourceTimeZoneId: TimeZoneId;
  userId: string;
  userDisplayName: string | null;
  userEmail: string | null;
  startUtc: UtcDateTime;
  endUtc: UtcDateTime;
  createdAtUtc: UtcDateTime;
}

// --- Real-time (hub /hubs/schedule) -----------------------------------------

/** A slot whose state changed; never says who booked it. */
export interface SlotChange {
  startUtc: UtcDateTime;
  endUtc: UtcDateTime;
  isBooked: boolean;
}

/** Payload of the hub's `SlotsChanged` event. */
export interface SlotsChangedMessage {
  resourceId: Guid;
  slots: SlotChange[];
}
