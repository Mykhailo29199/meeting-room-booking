import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-base-url';
import { Booking, BookingListItem, CancellationResult, CreateBookingRequest, Guid } from './models';

/** `/api/bookings`: booking, cancelling and listing bookings. */
@Injectable({ providedIn: 'root' })
export class BookingsApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/api/bookings`;

  /**
   * Books the range between two slot boundaries taken unchanged from the
   * schedule. Slot just taken by someone else → 409; rule broken → 400.
   */
  create(request: CreateBookingRequest): Observable<Booking> {
    return this.http.post<Booking>(this.url, request);
  }

  /** Cancels a booking, or ends it early if it is under way. Owner or admin. */
  cancel(id: Guid): Observable<CancellationResult> {
    return this.http.delete<CancellationResult>(`${this.url}/${id}`);
  }

  /** The signed-in user's bookings, earliest first. */
  mine(includePast = false): Observable<BookingListItem[]> {
    const params = new HttpParams().set('includePast', includePast);
    return this.http.get<BookingListItem[]>(`${this.url}/mine`, { params });
  }

  /** Everyone's bookings, earliest first. Admin only. */
  all(filter: { resourceId?: Guid; includePast?: boolean } = {}): Observable<BookingListItem[]> {
    let params = new HttpParams().set('includePast', filter.includePast ?? false);
    if (filter.resourceId) {
      params = params.set('resourceId', filter.resourceId);
    }
    return this.http.get<BookingListItem[]>(this.url, { params });
  }
}
