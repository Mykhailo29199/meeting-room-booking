import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { API_BASE_URL } from '../api/api-base-url';
import { AuthService } from './auth.service';

/**
 * Adds the bearer token to requests to this app's API, and to no other URL,
 * so the token cannot leak to a third party. A 401 on such a request means
 * the server no longer accepts the token: the session ends and the user is
 * sent to sign in.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken();
  if (!token || !isApiUrl(request.url, inject(API_BASE_URL))) {
    // Includes login and register while signed out: their 401 means a wrong
    // password, which the form shows.
    return next(request);
  }

  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(
    catchError((error: unknown) => {
      // Only if the rejected token is still the current one: a late answer to
      // a request sent before the user signed in again must not end the new session.
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        auth.accessToken() === token
      ) {
        auth.endSession();
      }
      return throwError(() => error);
    }),
  );
};

/** True for URLs under `<apiBaseUrl>/api/`. */
export function isApiUrl(url: string, apiBaseUrl: string): boolean {
  return url.startsWith(`${apiBaseUrl}/api/`);
}
