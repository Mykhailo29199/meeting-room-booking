import { HttpErrorResponse } from '@angular/common/http';

/** Shown when the server could not be reached or failed itself: nothing the user did wrong. */
export const GENERIC_ERROR_MESSAGE = 'Something went wrong. Please try again.';

/**
 * A failed API call, reduced to what the UI needs. The API answers errors
 * with RFC 9457 problem details whose `detail` is written for the user;
 * validation errors add `errors: { Field: [messages] }`.
 */
export interface ApiError {
  /** HTTP status; 0 when no response arrived (offline, server down, CORS). */
  status: number;
  /** True for a network failure or a 5xx: show {@link GENERIC_ERROR_MESSAGE}, suggest retrying. */
  isUnexpected: boolean;
  /** The message to show: the server's `detail` for 4xx, the generic one otherwise. */
  message: string;
  /**
   * Per-field messages from a 400, keyed by camelCase field name (`email`,
   * `startUtc`) to match the request JSON and form control names. Empty if none.
   */
  fieldErrors: Record<string, string[]>;
}

/** Turns anything an HTTP call can fail with into an {@link ApiError}. */
export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return unexpected(0);
  }
  // 0 = no response at all; 5xx = a server bug, whose body must not be shown.
  if (error.status === 0 || error.status >= 500) {
    return unexpected(error.status);
  }

  const problem = isObject(error.error) ? error.error : {};
  return {
    status: error.status,
    isUnexpected: false,
    message: text(problem['detail']) ?? text(problem['title']) ?? fallbackMessage(error.status),
    fieldErrors: fieldErrors(problem['errors']),
  };
}

function unexpected(status: number): ApiError {
  return { status, isUnexpected: true, message: GENERIC_ERROR_MESSAGE, fieldErrors: {} };
}

/** Used only if a 4xx arrives without problem details (e.g. from a proxy). */
function fallbackMessage(status: number): string {
  switch (status) {
    case 401:
      return 'Please sign in again.';
    case 403:
      return 'You are not allowed to do this.';
    case 404:
      return 'This no longer exists.';
    case 409:
      return 'This was just changed by someone else.';
    default:
      return 'The request could not be completed.';
  }
}

function fieldErrors(errors: unknown): Record<string, string[]> {
  const result: Record<string, string[]> = {};
  if (!isObject(errors)) {
    return result;
  }
  for (const [key, value] of Object.entries(errors)) {
    const messages = Array.isArray(value) ? value.filter((m) => typeof m === 'string') : [];
    if (messages.length > 0) {
      const field = toFieldName(key);
      result[field] = [...(result[field] ?? []), ...messages];
    }
  }
  return result;
}

/**
 * The server names fields after its C# properties (`DisplayName`); ASP.NET's
 * own JSON binding errors use a path (`$.startUtc`). Both become `displayName`
 * / `startUtc`.
 */
function toFieldName(key: string): string {
  const name = key.startsWith('$.') ? key.slice(2) : key;
  return name.charAt(0).toLowerCase() + name.slice(1);
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() !== '' ? value : undefined;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
