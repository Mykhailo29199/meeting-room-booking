import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, map, merge, of, Subject, switchMap, tap } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { Guid, LocalDate, ResourceSchedule } from '../../core/api/models';
import { ResourcesApi } from '../../core/api/resources-api';
import {
  addDays,
  formatLongDate,
  fromPickerDate,
  isLocalDate,
  toPickerDate,
} from '../../core/time/local-date';
import { formatTime, localDateOf, viewerTimeHint, zoneLabel } from '../../core/time/resource-time';
import { VIEWER_TIME_ZONE } from '../../core/time/viewer-time-zone';
import { SlotStatus, slotStatus, slotStatusLabel } from './slot-status';

const STATUS_ICONS: Record<SlotStatus, string> = {
  free: 'event_available',
  booked: 'event_busy',
  mine: 'person',
  past: 'history',
};

/**
 * One day of a resource's 15-minute slots, in the resource's own time zone.
 * The day is in the URL (`?date=YYYY-MM-DD`), so it can be bookmarked; without
 * it the API returns today in the resource's zone.
 */
@Component({
  selector: 'app-schedule-page',
  imports: [
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    RouterLink,
  ],
  providers: [provideNativeDateAdapter()],
  templateUrl: './schedule-page.html',
  styleUrl: './schedule-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SchedulePage {
  private readonly api = inject(ResourcesApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly viewerTimeZone = inject(VIEWER_TIME_ZONE);

  /** The route's `:id`. */
  readonly id = input.required<Guid>();
  /** The `date` query parameter; missing or not a real day = today in the resource's zone. */
  readonly date = input<string>();

  private readonly schedule = signal<ResourceSchedule | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  private readonly retries = new Subject<void>();

  private readonly request = computed(() => {
    const date = this.date();
    return { id: this.id(), date: isLocalDate(date) ? date : undefined };
  });

  protected readonly view = computed(() => {
    const schedule = this.schedule();
    if (!schedule) {
      return null;
    }
    const zone = schedule.timeZoneId;
    // The offset in the label is the one on this day (daylight saving time).
    const dayInstant = schedule.slots[0]?.startUtc ?? `${schedule.localDate}T12:00:00Z`;
    const slots = schedule.slots.map((slot) => {
      const status = slotStatus(slot);
      return {
        key: slot.startUtc,
        time: `${formatTime(slot.startUtc, zone)}–${formatTime(slot.endUtc, zone)}`,
        viewerTime: viewerTimeHint(slot.startUtc, zone, this.viewerTimeZone),
        status,
        icon: STATUS_ICONS[status],
        label: slotStatusLabel(slot),
      };
    });
    return {
      name: schedule.resourceName,
      isActive: schedule.isActive,
      longDate: formatLongDate(schedule.localDate),
      pickerDate: toPickerDate(schedule.localDate),
      previousDay: addDays(schedule.localDate, -1),
      nextDay: addDays(schedule.localDate, 1),
      isToday: schedule.localDate === localDateOf(new Date(), zone),
      zone: zoneLabel(zone, dayInstant),
      // Named only when the hints in brackets are shown.
      viewerZone: slots.some((slot) => slot.viewerTime)
        ? zoneLabel(this.viewerTimeZone, dayInstant)
        : null,
      slots,
    };
  });

  constructor() {
    merge(toObservable(this.request), this.retries.pipe(map(() => this.request())))
      .pipe(
        tap(({ id }) => {
          this.loading.set(true);
          this.error.set(null);
          // Another day of the same resource stays visible while loading.
          if (this.schedule()?.resourceId !== id) {
            this.schedule.set(null);
          }
        }),
        switchMap(({ id, date }) =>
          this.api.schedule(id, date).pipe(
            map((schedule) => ({ schedule, error: null })),
            catchError((error: unknown) => of({ schedule: null, error: toApiError(error) })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ schedule, error }) => {
        this.loading.set(false);
        this.schedule.set(schedule);
        this.error.set(error);
      });
  }

  /** Shows another day; null = today in the resource's zone. */
  protected showDay(date: LocalDate | null): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { date } });
  }

  protected pickDay(date: Date | null): void {
    if (date) {
      this.showDay(fromPickerDate(date));
    }
  }

  protected retry(): void {
    this.retries.next();
  }
}
