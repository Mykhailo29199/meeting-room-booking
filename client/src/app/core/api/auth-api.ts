import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-base-url';
import { AuthResult, CurrentUser, LoginRequest, RegisterRequest } from './models';

/** `/api/auth`: accounts and sign-in. Keeping the token is `AuthService`'s job, not this one's. */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/api/auth`;

  /** Creates an account (always role User) and signs it in. */
  register(request: RegisterRequest): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${this.url}/register`, request);
  }

  /** Wrong password and unknown email both fail with the same 401. */
  login(request: LoginRequest): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${this.url}/login`, request);
  }

  /** Whose token this is. */
  me(): Observable<CurrentUser> {
    return this.http.get<CurrentUser>(`${this.url}/me`);
  }
}
