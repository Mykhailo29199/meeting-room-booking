import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, map, merge, of, Subject, switchMap, tap } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { BookingsApi } from '../../core/api/bookings-api';
import { Guid, LocalDate, ResourceSchedule, SlotChange, UtcDateTime } from '../../core/api/models';
import { ResourcesApi } from '../../core/api/resources-api';
import { Notifier } from '../../core/notify/notifier';
import { ScheduleHubService } from '../../core/realtime/schedule-hub.service';
import {
  addDays,
  formatLongDate,
  fromPickerDate,
  isLocalDate,
  toPickerDate,
} from '../../core/time/local-date';
import { formatTime, localDateOf, viewerTimeHint, zoneLabel } from '../../core/time/resource-time';
import { VIEWER_TIME_ZONE } from '../../core/time/viewer-time-zone';
import {
  clickSlot,
  formatDuration,
  isInRange,
  keepIfAvailable,
  minutesBetween,
  reachableSlots,
  selectableStarts,
  selectEnd,
  selectStart,
  SlotRange,
} from './slot-selection';
import { applySlotChanges } from './slot-changes';
import { SlotStatus, slotStatus, slotStatusLabel } from './slot-status';

const STATUS_ICONS: Record<SlotStatus, string> = {
  free: 'event_available',
  booked: 'event_busy',
  mine: 'person',
  past: 'history',
};

/**
 * One day of a resource's 15-minute slots, in the resource's own time zone,
 * where the user chooses a range and books it. The day is in the URL
 * (`?date=YYYY-MM-DD`), so it can be bookmarked; without it the API returns
 * today in the resource's zone.
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
    MatSelectModule,
    RouterLink,
  ],
  providers: [provideNativeDateAdapter()],
  templateUrl: './schedule-page.html',
  styleUrl: './schedule-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SchedulePage {
  private readonly api = inject(ResourcesApi);
  private readonly bookings = inject(BookingsApi);
  private readonly notifier = inject(Notifier);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly hub = inject(ScheduleHubService);
  private readonly viewerTimeZone = inject(VIEWER_TIME_ZONE);

  /** The route's `:id`. */
  readonly id = input.required<Guid>();
  /** The `date` query parameter; missing or not a real day = today in the resource's zone. */
  readonly date = input<string>();

  private readonly schedule = signal<ResourceSchedule | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  private readonly reloads = new Subject<void>();

  /** The range chosen for booking, as `startUtc`/`endUtc` taken from the schedule. */
  private readonly selection = signal<SlotRange | null>(null);
  protected readonly booking = signal(false);

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
    const selection = this.selection();
    // The offset in the label is the one on this day (daylight saving time).
    const dayInstant = schedule.slots[0]?.startUtc ?? `${schedule.localDate}T12:00:00Z`;
    const slots = schedule.slots.map((slot) => {
      const status = slotStatus(slot);
      const selected = isInRange(slot, selection);
      return {
        key: slot.startUtc,
        time: `${formatTime(slot.startUtc, zone)}–${formatTime(slot.endUtc, zone)}`,
        viewerTime: viewerTimeHint(slot.startUtc, zone, this.viewerTimeZone),
        status,
        selectable: schedule.isActive && status === 'free',
        selected,
        icon: selected ? 'check_circle' : STATUS_ICONS[status],
        label: selected ? 'Selected' : slotStatusLabel(slot),
      };
    });
    const starts = schedule.isActive ? selectableStarts(schedule.slots) : [];
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
      // Null when nothing on this day can be booked.
      form:
        starts.length === 0
          ? null
          : {
              start: selection?.startUtc ?? null,
              end: selection?.endUtc ?? null,
              startOptions: starts.map((slot) => ({
                value: slot.startUtc,
                label: formatTime(slot.startUtc, zone),
              })),
              // Only ends reachable from the start through free slots.
              endOptions: selection
                ? reachableSlots(schedule.slots, selection.startUtc).map((slot) => ({
                    value: slot.endUtc,
                    label: `${formatTime(slot.endUtc, zone)} (${formatDuration(
                      minutesBetween(selection.startUtc, slot.endUtc),
                    )})`,
                  }))
                : [],
              summary: selection ? describe(selection, zone) : null,
            },
    };
  });

  constructor() {
    // Live updates for the resource on screen (task item 7). Watching stops
    // when the page shows another resource or is left.
    effect((onCleanup) => {
      const id = this.id();
      this.hub.watch(id);
      onCleanup(() => this.hub.unwatch(id));
    });
    toObservable(this.id)
      .pipe(
        switchMap((id) =>
          merge(
            this.hub.slotsChanged(id).pipe(map((changes) => ({ changes }))),
            // (Re)joined after a gap in which events may have been missed.
            this.hub.joined(id).pipe(map(() => ({ changes: null }))),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ changes }) => (changes ? this.applyChanges(changes) : this.reload()));

    merge(toObservable(this.request), this.reloads.pipe(map(() => this.request())))
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
        // A choice survives a reload only if every slot of it is still free
        // (a different day has none of its slots).
        this.selection.update((selection) =>
          schedule ? keepIfAvailable(schedule.slots, selection) : null,
        );
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

  protected reload(): void {
    this.reloads.next();
  }

  protected clickSlot(startUtc: UtcDateTime): void {
    this.updateSelection((slots, current) => clickSlot(slots, current, startUtc));
  }

  protected chooseStart(startUtc: UtcDateTime): void {
    this.updateSelection((slots, current) => selectStart(slots, current, startUtc));
  }

  protected chooseEnd(endUtc: UtcDateTime): void {
    this.updateSelection((slots, current) => selectEnd(slots, current, endUtc));
  }

  protected clearSelection(): void {
    this.selection.set(null);
  }

  /**
   * Books the chosen range, sending back the schedule's own UTC values. The
   * server decides: a slot taken meanwhile is a 409, and the schedule is
   * reloaded so the user sees why.
   */
  protected book(): void {
    const schedule = this.schedule();
    const range = this.selection();
    if (!schedule || !range || this.booking()) {
      return;
    }
    const description = describe(range, schedule.timeZoneId);
    this.booking.set(true);
    this.bookings
      .create({ resourceId: schedule.resourceId, startUtc: range.startUtc, endUtc: range.endUtc })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.booking.set(false);
          this.selection.set(null);
          this.notifier.show(`Booked ${description}.`);
          // The reload marks the new slots as the user's own.
          this.reload();
        },
        error: (error: unknown) => {
          this.booking.set(false);
          const problem = toApiError(error);
          this.notifier.show(bookingErrorMessage(problem));
          if (problem.status === 409) {
            // Taken by someone else: show who holds what now, and start over.
            this.selection.set(null);
            this.reload();
          } else if (!problem.isUnexpected) {
            // A rule the schedule on screen did not show (e.g. the slot has
            // started meanwhile): reload; a choice that is still free is kept.
            this.reload();
          }
          // Network failure or 5xx: keep everything so the user can try again.
        },
      });
  }

  /**
   * Applies slots booked or freed by others. If the user's choice is no
   * longer free, it is cleared with an explanation before they press Book.
   */
  private applyChanges(changes: SlotChange[]): void {
    const schedule = this.schedule();
    if (!schedule) {
      return;
    }
    const slots = applySlotChanges(schedule.slots, changes);
    this.schedule.set({ ...schedule, slots });
    // While the user's own booking is being sent, its event can arrive before
    // the response; the response decides what happens to the choice.
    const selection = this.selection();
    if (selection && !this.booking() && !keepIfAvailable(slots, selection)) {
      this.selection.set(null);
      this.notifier.show(
        'The time you chose has just been booked by someone else. Please choose another.',
      );
    }
  }

  private updateSelection(
    change: (slots: ResourceSchedule['slots'], current: SlotRange | null) => SlotRange | null,
  ): void {
    const schedule = this.schedule();
    if (schedule?.isActive) {
      this.selection.update((current) => change(schedule.slots, current));
    }
  }
}

/** E.g. `10:00–11:00, 1 h`, in the resource's zone. */
function describe(range: SlotRange, timeZoneId: string): string {
  const duration = formatDuration(minutesBetween(range.startUtc, range.endUtc));
  return `${formatTime(range.startUtc, timeZoneId)}–${formatTime(range.endUtc, timeZoneId)}, ${duration}`;
}

/** The server's field messages if it sent any (a 400), otherwise its message. */
function bookingErrorMessage(problem: ApiError): string {
  const fieldMessages = Object.values(problem.fieldErrors).flat();
  return fieldMessages.length > 0 ? fieldMessages.join(' ') : problem.message;
}
