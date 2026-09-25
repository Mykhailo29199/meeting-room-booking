import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthResult } from '../api/models';
import { AuthService, SESSION_STORAGE_KEY } from './auth.service';

const now = new Date('2026-10-01T08:00:00Z');

function session(overrides: Partial<AuthResult> = {}): AuthResult {
  return {
    accessToken: 'token-1',
    expiresAtUtc: '2026-10-01T09:00:00Z', // an hour after `now`
    userId: 'user-1',
    email: 'ann@example.test',
    displayName: 'Ann',
    roles: ['User'],
    ...overrides,
  };
}

describe('AuthService', () => {
  beforeEach(() => {
    vi.useFakeTimers({ now });
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  it('keeps the session after signing in, also in storage', () => {
    const auth = TestBed.inject(AuthService);

    auth.login({ email: 'ann@example.test', password: 'secret' }).subscribe();
    TestBed.inject(HttpTestingController).expectOne('/api/auth/login').flush(session());

    expect(auth.isSignedIn()).toBe(true);
    expect(auth.accessToken()).toBe('token-1');
    expect(auth.user()).toEqual({
      userId: 'user-1',
      email: 'ann@example.test',
      displayName: 'Ann',
      roles: ['User'],
    });
    expect(auth.isAdmin()).toBe(false);
    expect(JSON.parse(localStorage.getItem(SESSION_STORAGE_KEY)!)).toEqual(session());
  });

  it('signs in after registering', () => {
    const auth = TestBed.inject(AuthService);

    auth.register({ email: 'ann@example.test', password: 'secret', displayName: 'Ann' }).subscribe();
    TestBed.inject(HttpTestingController).expectOne('/api/auth/register').flush(session());

    expect(auth.isSignedIn()).toBe(true);
  });

  it('restores a stored session on start', () => {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session({ roles: ['Admin'] })));

    const auth = TestBed.inject(AuthService);

    expect(auth.accessToken()).toBe('token-1');
    expect(auth.isAdmin()).toBe(true);
  });

  it('drops an expired stored session', () => {
    localStorage.setItem(
      SESSION_STORAGE_KEY,
      JSON.stringify(session({ expiresAtUtc: '2026-10-01T08:00:00Z' })),
    );

    const auth = TestBed.inject(AuthService);

    expect(auth.isSignedIn()).toBe(false);
    expect(localStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
  });

  it('ignores storage that does not hold a session', () => {
    localStorage.setItem(SESSION_STORAGE_KEY, '{"accessToken": 42');

    expect(TestBed.inject(AuthService).isSignedIn()).toBe(false);
  });

  it('forgets the session on sign-out', () => {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session()));
    const auth = TestBed.inject(AuthService);

    auth.signOut();

    expect(auth.isSignedIn()).toBe(false);
    expect(auth.accessToken()).toBeNull();
    expect(localStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
  });

  it('ends the session when the token expires and asks to sign in again', () => {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session()));
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/resources/42?date=2026-10-01');
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const auth = TestBed.inject(AuthService);

    vi.advanceTimersByTime(60 * 60 * 1000 - 1);
    expect(auth.isSignedIn()).toBe(true);

    vi.advanceTimersByTime(1);
    expect(auth.isSignedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/resources/42?date=2026-10-01' },
    });
  });

  it('does not end a new session when the old one would have expired', () => {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session()));
    const auth = TestBed.inject(AuthService);

    auth.login({ email: 'ann@example.test', password: 'secret' }).subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/auth/login')
      .flush(session({ accessToken: 'token-2', expiresAtUtc: '2026-10-01T10:00:00Z' }));
    vi.advanceTimersByTime(60 * 60 * 1000);

    expect(auth.accessToken()).toBe('token-2');
  });
});
