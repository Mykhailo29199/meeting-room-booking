import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { AuthApi } from '../api/auth-api';
import { AuthResult, CurrentUser, LoginRequest, RegisterRequest, Roles } from '../api/models';
import { LOGIN_URL } from './auth-urls';

/** localStorage key of the signed-in session. */
export const SESSION_STORAGE_KEY = 'meetingRoomBooking.session';

/** setTimeout's largest delay (a 32-bit int, about 24.8 days). */
const MAX_TIMER_DELAY_MS = 2_147_483_647;

/**
 * Who is signed in, and their access token.
 *
 * The session (the API's `AuthResult`) is kept in localStorage so it survives
 * a reload. Trade-off, chosen on purpose: scripts injected into the page could
 * read it; an httpOnly cookie could not be read, but needs a cookie-based
 * backend. The token lasts 60 minutes and cannot be refreshed, so the session
 * ends exactly when the token expires and the user signs in again.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly session = signal<AuthResult | null>(null);
  private expiryTimer: ReturnType<typeof setTimeout> | undefined;

  /** The signed-in user, or null. */
  readonly user = computed<CurrentUser | null>(() => {
    const session = this.session();
    return session
      ? {
          userId: session.userId,
          email: session.email,
          displayName: session.displayName,
          roles: session.roles,
        }
      : null;
  });

  readonly isSignedIn = computed(() => this.session() !== null);

  /** Only shapes the UI; the API checks the role on every admin request. */
  readonly isAdmin = computed(() => this.session()?.roles.includes(Roles.Admin) ?? false);

  constructor() {
    const stored = readStoredSession();
    if (stored && !isExpired(stored)) {
      this.start(stored);
    } else {
      removeStoredSession();
    }
  }

  /** The bearer token for API requests, or null when signed out. */
  accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  login(request: LoginRequest): Observable<AuthResult> {
    return this.api.login(request).pipe(tap((result) => this.start(result)));
  }

  /** Creates an account (role User) and signs it in. */
  register(request: RegisterRequest): Observable<AuthResult> {
    return this.api.register(request).pipe(tap((result) => this.start(result)));
  }

  signOut(): void {
    clearTimeout(this.expiryTimer);
    this.session.set(null);
    removeStoredSession();
  }

  /**
   * Ends a session the server no longer accepts (expired or rejected token)
   * and sends the user to sign in, then back to where they were.
   */
  endSession(): void {
    const returnUrl = this.router.url;
    this.signOut();
    const keepReturnUrl = returnUrl !== '/' && !returnUrl.startsWith(LOGIN_URL);
    void this.router.navigate([LOGIN_URL], {
      queryParams: keepReturnUrl ? { returnUrl } : {},
    });
  }

  private start(session: AuthResult): void {
    clearTimeout(this.expiryTimer);
    this.session.set(session);
    storeSession(session);
    const delay = Math.min(Date.parse(session.expiresAtUtc) - Date.now(), MAX_TIMER_DELAY_MS);
    this.expiryTimer = setTimeout(() => this.endSession(), delay);
  }
}

function isExpired(session: AuthResult): boolean {
  return Date.parse(session.expiresAtUtc) <= Date.now();
}

// Storage may be unavailable (privacy settings) or hold anything. Neither may
// break the app: the user just has to sign in.

function readStoredSession(): AuthResult | null {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(SESSION_STORAGE_KEY) ?? 'null');
    return isSession(value) ? value : null;
  } catch {
    return null;
  }
}

function storeSession(session: AuthResult): void {
  try {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
  } catch {
    // Signed in until the page is closed.
  }
}

function removeStoredSession(): void {
  try {
    localStorage.removeItem(SESSION_STORAGE_KEY);
  } catch {
    // Nothing was stored.
  }
}

function isSession(value: unknown): value is AuthResult {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const session = value as Record<string, unknown>;
  return (
    typeof session['accessToken'] === 'string' &&
    typeof session['expiresAtUtc'] === 'string' &&
    !Number.isNaN(Date.parse(session['expiresAtUtc'])) &&
    typeof session['userId'] === 'string' &&
    typeof session['email'] === 'string' &&
    typeof session['displayName'] === 'string' &&
    Array.isArray(session['roles'])
  );
}
