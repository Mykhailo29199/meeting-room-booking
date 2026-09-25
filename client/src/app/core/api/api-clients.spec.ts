import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from './api-base-url';
import { AuthApi } from './auth-api';
import { BookingsApi } from './bookings-api';
import { ResourcesApi } from './resources-api';

const base = 'https://api.example.test';
const id = '0199a0b2-0000-7000-8000-000000000001';

describe('API clients', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: base },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Subscribes, then returns the single request that went out. */
  function sent(call: () => { subscribe: () => unknown }) {
    call().subscribe();
    return http.expectOne(() => true).request;
  }

  describe('AuthApi', () => {
    it('registers, logs in and asks who the token belongs to', () => {
      const api = TestBed.inject(AuthApi);
      const register = { email: 'a@b.c', password: 'Secret123!', displayName: 'Ann' };

      let request = sent(() => api.register(register));
      expect([request.method, request.url, request.body]).toEqual([
        'POST',
        `${base}/api/auth/register`,
        register,
      ]);

      request = sent(() => api.login({ email: 'a@b.c', password: 'Secret123!' }));
      expect([request.method, request.url]).toEqual(['POST', `${base}/api/auth/login`]);

      request = sent(() => api.me());
      expect([request.method, request.url]).toEqual(['GET', `${base}/api/auth/me`]);
    });
  });

  describe('ResourcesApi', () => {
    it('reads resources and schedules', () => {
      const api = TestBed.inject(ResourcesApi);

      expect(sent(() => api.list()).urlWithParams).toBe(`${base}/api/resources`);
      expect(sent(() => api.get(id)).urlWithParams).toBe(`${base}/api/resources/${id}`);
      expect(sent(() => api.schedule(id, '2026-10-01')).urlWithParams).toBe(
        `${base}/api/resources/${id}/schedule?date=2026-10-01`,
      );
      // No date: the server picks today in the resource's time zone.
      expect(sent(() => api.schedule(id)).urlWithParams).toBe(
        `${base}/api/resources/${id}/schedule`,
      );
    });

    it('sends admin changes, including the version of an edit', () => {
      const api = TestBed.inject(ResourcesApi);
      const fields = {
        name: 'Room A',
        capacity: 6,
        timeZoneId: 'Europe/Berlin',
        opensAt: '08:00:00',
        closesAt: '18:00:00',
      };

      let request = sent(() => api.create(fields));
      expect([request.method, request.url, request.body]).toEqual([
        'POST',
        `${base}/api/resources`,
        fields,
      ]);

      const update = { ...fields, version: '0199a0b2-0000-7000-8000-0000000000ff' };
      request = sent(() => api.update(id, update));
      expect([request.method, request.url, request.body]).toEqual([
        'PUT',
        `${base}/api/resources/${id}`,
        update,
      ]);

      request = sent(() => api.remove(id));
      expect([request.method, request.url]).toEqual(['DELETE', `${base}/api/resources/${id}`]);

      request = sent(() => api.restore(id));
      expect([request.method, request.url]).toEqual([
        'POST',
        `${base}/api/resources/${id}/restore`,
      ]);
    });
  });

  describe('BookingsApi', () => {
    it('books with the schedule times unchanged and cancels', () => {
      const api = TestBed.inject(BookingsApi);
      const booking = {
        resourceId: id,
        startUtc: '2026-10-01T08:00:00Z',
        endUtc: '2026-10-01T09:00:00Z',
      };

      let request = sent(() => api.create(booking));
      expect([request.method, request.url, request.body]).toEqual([
        'POST',
        `${base}/api/bookings`,
        booking,
      ]);

      request = sent(() => api.cancel(id));
      expect([request.method, request.url]).toEqual(['DELETE', `${base}/api/bookings/${id}`]);
    });

    it('lists own and all bookings with their filters', () => {
      const api = TestBed.inject(BookingsApi);

      expect(sent(() => api.mine()).urlWithParams).toBe(
        `${base}/api/bookings/mine?includePast=false`,
      );
      expect(sent(() => api.mine(true)).urlWithParams).toBe(
        `${base}/api/bookings/mine?includePast=true`,
      );
      expect(sent(() => api.all()).urlWithParams).toBe(`${base}/api/bookings?includePast=false`);
      expect(sent(() => api.all({ resourceId: id, includePast: true })).urlWithParams).toBe(
        `${base}/api/bookings?includePast=true&resourceId=${id}`,
      );
    });
  });
});
