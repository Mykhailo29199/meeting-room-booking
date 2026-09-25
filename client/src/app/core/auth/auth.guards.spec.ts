import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  provideRouter,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './auth.guards';
import { AuthService } from './auth.service';

describe('route guards', () => {
  const isSignedIn = signal(false);
  const isAdmin = signal(false);

  beforeEach(() => {
    isSignedIn.set(false);
    isAdmin.set(false);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { isSignedIn, isAdmin } }],
    });
  });

  /** Runs the guard for a navigation to `url`; a redirect comes back as its URL string. */
  function run(guard: CanActivateFn, url: string): boolean | string {
    const result = TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
    if (typeof result === 'boolean') {
      return result;
    }
    if (result instanceof UrlTree) {
      return TestBed.inject(Router).serializeUrl(result);
    }
    throw new Error('These guards decide synchronously.');
  }

  describe('authGuard', () => {
    it('lets signed-in users in', () => {
      isSignedIn.set(true);
      expect(run(authGuard, '/resources')).toBe(true);
    });

    it('sends others to sign in and back', () => {
      expect(run(authGuard, '/resources/42?date=2026-10-01')).toBe(
        '/login?returnUrl=%2Fresources%2F42%3Fdate%3D2026-10-01',
      );
    });
  });

  describe('adminGuard', () => {
    it('lets admins in', () => {
      isSignedIn.set(true);
      isAdmin.set(true);
      expect(run(adminGuard, '/admin/resources')).toBe(true);
    });

    it('sends other users home', () => {
      isSignedIn.set(true);
      expect(run(adminGuard, '/admin/resources')).toBe('/resources');
    });

    it('sends signed-out visitors to sign in and back', () => {
      expect(run(adminGuard, '/admin/resources')).toBe('/login?returnUrl=%2Fadmin%2Fresources');
    });
  });

  describe('guestGuard', () => {
    it('lets signed-out visitors in', () => {
      expect(run(guestGuard, '/login')).toBe(true);
    });

    it('sends signed-in users home', () => {
      isSignedIn.set(true);
      expect(run(guestGuard, '/login')).toBe('/resources');
    });
  });
});
