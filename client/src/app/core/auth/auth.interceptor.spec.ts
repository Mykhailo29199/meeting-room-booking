import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api/api-base-url';
import { authInterceptor, isApiUrl } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let token: string | null;
  let endSession: ReturnType<typeof vi.fn>;
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    token = 'token-1';
    endSession = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
        { provide: AuthService, useValue: { accessToken: () => token, endSession } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('sends the token to the API', () => {
    http.get('/api/resources').subscribe();

    expect(backend.expectOne('/api/resources').request.headers.get('Authorization')).toBe(
      'Bearer token-1',
    );
  });

  it('never sends the token to another site', () => {
    http.get('https://example.test/api/resources').subscribe();

    expect(
      backend.expectOne('https://example.test/api/resources').request.headers.has('Authorization'),
    ).toBe(false);
  });

  it('sends no token when signed out', () => {
    token = null;
    http.get('/api/resources').subscribe();

    expect(backend.expectOne('/api/resources').request.headers.has('Authorization')).toBe(false);
  });

  it('ends the session when the API rejects the token, and still reports the error', () => {
    let failed = false;
    http.get('/api/resources').subscribe({ error: () => (failed = true) });

    backend.expectOne('/api/resources').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(endSession).toHaveBeenCalledOnce();
    expect(failed).toBe(true);
  });

  it('keeps a session that replaced the rejected token', () => {
    http.get('/api/resources').subscribe({ error: () => undefined });
    token = 'token-2';

    backend.expectOne('/api/resources').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(endSession).not.toHaveBeenCalled();
  });

  it('leaves a wrong password on the login form', () => {
    token = null;
    http.post('/api/auth/login', {}).subscribe({ error: () => undefined });

    backend.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(endSession).not.toHaveBeenCalled();
  });

  it('keeps the session on other errors', () => {
    http.get('/api/bookings').subscribe({ error: () => undefined });

    backend.expectOne('/api/bookings').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(endSession).not.toHaveBeenCalled();
  });
});

describe('isApiUrl', () => {
  it.each([
    ['/api/resources', '', true],
    ['/apix/resources', '', false],
    ['https://example.test/api/resources', '', false],
    ['//example.test/api/resources', '', false],
    ['https://api.example.test/api/resources', 'https://api.example.test', true],
    ['https://api.example.test.evil.test/api/x', 'https://api.example.test', false],
  ])('%s with base "%s" is %s', (url, base, expected) => {
    expect(isApiUrl(url, base)).toBe(expected);
  });
});
