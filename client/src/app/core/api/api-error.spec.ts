import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { GENERIC_ERROR_MESSAGE, toApiError } from './api-error';

function response(status: number, body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: body,
    headers: new HttpHeaders({ 'Content-Type': 'application/problem+json' }),
    url: '/api/bookings',
  });
}

describe('toApiError', () => {
  it('shows the server detail of a 409', () => {
    const error = toApiError(
      response(409, { status: 409, title: 'Conflict.', detail: 'This time was just booked.' }),
    );

    expect(error).toEqual({
      status: 409,
      isUnexpected: false,
      message: 'This time was just booked.',
      fieldErrors: {},
    });
  });

  it('puts validation errors under camelCase field names', () => {
    const error = toApiError(
      response(400, {
        title: 'Some fields are invalid.',
        detail: 'One or more fields are invalid.',
        errors: {
          DisplayName: ['Display name is required.'],
          Password: ['Too short.', 'Needs a digit.'],
          '$.startUtc': ['Not a valid date.'],
        },
      }),
    );

    expect(error.message).toBe('One or more fields are invalid.');
    expect(error.fieldErrors).toEqual({
      displayName: ['Display name is required.'],
      password: ['Too short.', 'Needs a digit.'],
      startUtc: ['Not a valid date.'],
    });
  });

  it('falls back to the title when there is no detail', () => {
    expect(toApiError(response(404, { title: 'Not found.' })).message).toBe('Not found.');
  });

  it('uses a status-based message when a 4xx has no problem details', () => {
    const error = toApiError(response(403, 'Forbidden'));

    expect(error.message).toBe('You are not allowed to do this.');
    expect(error.isUnexpected).toBe(false);
  });

  it('ignores malformed errors', () => {
    const error = toApiError(response(400, { detail: 'Bad.', errors: { Email: 'not an array' } }));

    expect(error.fieldErrors).toEqual({});
  });

  it('treats a network failure as unexpected', () => {
    const error = toApiError(new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }));

    expect(error).toEqual({
      status: 0,
      isUnexpected: true,
      message: GENERIC_ERROR_MESSAGE,
      fieldErrors: {},
    });
  });

  it('never shows the body of a server error', () => {
    const error = toApiError(response(500, { detail: 'NullReferenceException at ...' }));

    expect(error.isUnexpected).toBe(true);
    expect(error.message).toBe(GENERIC_ERROR_MESSAGE);
  });

  it('treats a non-HTTP error as unexpected', () => {
    expect(toApiError(new Error('boom')).message).toBe(GENERIC_ERROR_MESSAGE);
  });
});
