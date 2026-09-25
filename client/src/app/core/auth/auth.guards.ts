import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { HOME_URL, LOGIN_URL } from './auth-urls';
import { AuthService } from './auth.service';

// Guards only shape navigation: they keep people off pages that would fail
// anyway. They are not security; the API authorizes every request.

/** Signed-in users only; others go to sign in and come back afterwards. */
export const authGuard: CanActivateFn = (_route, state) =>
  inject(AuthService).isSignedIn() ||
  inject(Router).createUrlTree([LOGIN_URL], { queryParams: { returnUrl: state.url } });

/** Admins only; signed-out users go to sign in, other users go home. */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isSignedIn()) {
    return router.createUrlTree([LOGIN_URL], { queryParams: { returnUrl: state.url } });
  }
  return auth.isAdmin() || router.createUrlTree([HOME_URL]);
};

/** Signed-out users only (login, register); signed-in users go home. */
export const guestGuard: CanActivateFn = () =>
  !inject(AuthService).isSignedIn() || inject(Router).createUrlTree([HOME_URL]);
