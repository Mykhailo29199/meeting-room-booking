import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-base-url';
import {
  CreateResourceRequest,
  Guid,
  LocalDate,
  Resource,
  ResourceRemovalResult,
  ResourceSchedule,
  UpdateResourceRequest,
} from './models';

/** `/api/resources`: resources and their day schedules. Writes are admin-only on the server. */
@Injectable({ providedIn: 'root' })
export class ResourcesApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/api/resources`;

  /** Bookable resources for users; admins also get removed ones (`isActive: false`). */
  list(): Observable<Resource[]> {
    return this.http.get<Resource[]>(this.url);
  }

  /** A removed resource is a 404 for users. */
  get(id: Guid): Observable<Resource> {
    return this.http.get<Resource>(`${this.url}/${id}`);
  }

  /**
   * One day's 15-minute slots.
   * @param date Day in the resource's local time; omitted = today there.
   */
  schedule(id: Guid, date?: LocalDate): Observable<ResourceSchedule> {
    const params = date ? new HttpParams().set('date', date) : undefined;
    return this.http.get<ResourceSchedule>(`${this.url}/${id}/schedule`, { params });
  }

  create(request: CreateResourceRequest): Observable<Resource> {
    return this.http.post<Resource>(this.url, request);
  }

  /** `request.version` must be the one loaded; a stale version is a 409. */
  update(id: Guid, request: UpdateResourceRequest): Observable<Resource> {
    return this.http.put<Resource>(`${this.url}/${id}`, request);
  }

  /** Deactivates it and cancels its future bookings; returns how many were affected. */
  remove(id: Guid): Observable<ResourceRemovalResult> {
    return this.http.delete<ResourceRemovalResult>(`${this.url}/${id}`);
  }

  restore(id: Guid): Observable<Resource> {
    return this.http.post<Resource>(`${this.url}/${id}/restore`, null);
  }
}
