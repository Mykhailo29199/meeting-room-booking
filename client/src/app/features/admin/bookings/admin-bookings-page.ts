import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, filter, map, merge, of, Subject, switchMap, tap } from 'rxjs';
import { ApiError, toApiError } from '../../../core/api/api-error';
import { BookingsApi } from '../../../core/api/bookings-api';
import { BookingListItem, Resource } from '../../../core/api/models';
import { ResourcesApi } from '../../../core/api/resources-api';
import { Confirmer } from '../../../core/notify/confirm-dialog';
import { Notifier } from '../../../core/notify/notifier';
import { VIEWER_TIME_ZONE } from '../../../core/time/viewer-time-zone';
import {
  cancelAction,
  cancelConfirmation,
  cancellationMessage,
} from '../../../shared/bookings/booking-cancellation';
import {
  BOOKING_STATE_LABELS,
  bookingState,
  describeBooking,
} from '../../../shared/bookings/booking-display';

/** The API returns at most this many bookings (earliest first). */
export const BOOKING_LIST_LIMIT = 500;

/**
 * Everyone's bookings, for admins: who booked what, filtered by resource,
 * optionally including past ones. The filters live in the URL
 * (`?resourceId=…&past=true`), so a view can be bookmarked. Admins can
 * cancel any booking, or end one under way.
 */
@Component({
  selector: 'app-admin-bookings-page',
  imports: [
    MatButtonModule,
    MatFormFieldModule,
    MatProgressBarModule,
    MatSelectModule,
    MatSlideToggleModule,
    RouterLink,
  ],
  templateUrl: './admin-bookings-page.html',
  styleUrl: './admin-bookings-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminBookingsPage {
  private readonly bookingsApi = inject(BookingsApi);
  private readonly resourcesApi = inject(ResourcesApi);
  private readonly confirmer = inject(Confirmer);
  private readonly notifier = inject(Notifier);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly viewerTimeZone = inject(VIEWER_TIME_ZONE);

  /** The `resourceId` query parameter; missing = every resource. */
  readonly resourceId = input<string>();
  /** The `past` query parameter; `true` also lists bookings that have ended. */
  readonly past = input<string>();

  protected readonly includePast = computed(() => this.past() === 'true');
  /** For the filter; includes removed resources, which admins receive too. */
  protected readonly resources = signal<Resource[]>([]);
  private readonly bookings = signal<BookingListItem[] | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  /** The booking being cancelled, whose button is disabled meanwhile. */
  protected readonly cancelling = signal<string | null>(null);
  private readonly reloads = new Subject<void>();

  private readonly filters = computed(() => ({
    resourceId: this.resourceId() || undefined,
    includePast: this.includePast(),
  }));

  protected readonly items = computed(() => {
    const now = Date.now();
    return this.bookings()?.map((booking) => {
      const state = bookingState(booking, now);
      const display = describeBooking(booking, this.viewerTimeZone);
      const action = cancelAction(booking, now);
      return {
        booking,
        ...display,
        owner: ownerOf(booking),
        stateLabel: BOOKING_STATE_LABELS[state],
        state,
        action,
        actionLabel: `${action}: ${booking.resourceName}, ${display.date}, ${display.time}, ${ownerOf(booking)}`,
      };
    });
  });

  protected readonly atLimit = computed(() => (this.bookings()?.length ?? 0) >= BOOKING_LIST_LIMIT);

  constructor() {
    this.resourcesApi
      .list()
      .pipe(takeUntilDestroyed())
      // Without the list the filter only offers "All resources"; the bookings still load.
      .subscribe({ next: (resources) => this.resources.set(resources), error: () => undefined });

    merge(toObservable(this.filters), this.reloads.pipe(map(() => this.filters())))
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((filters) =>
          this.bookingsApi.all(filters).pipe(
            map((bookings) => ({ bookings, error: null })),
            catchError((error: unknown) => of({ bookings: null, error: toApiError(error) })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ bookings, error }) => {
        this.loading.set(false);
        this.bookings.set(bookings);
        this.error.set(error);
      });
  }

  protected reload(): void {
    this.reloads.next();
  }

  protected filterByResource(resourceId: string): void {
    this.navigate({ resourceId: resourceId || null });
  }

  protected showPast(show: boolean): void {
    this.navigate({ past: show ? 'true' : null });
  }

  protected cancel(booking: BookingListItem): void {
    this.confirmer
      .confirm(cancelConfirmation(booking, this.viewerTimeZone, Date.now(), ownerOf(booking)))
      .pipe(
        filter((confirmed) => confirmed),
        tap(() => this.cancelling.set(booking.id)),
        switchMap(() => this.bookingsApi.cancel(booking.id)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.cancelling.set(null);
          this.notifier.show(cancellationMessage(result, booking));
          this.reload();
        },
        error: (error: unknown) => {
          this.cancelling.set(null);
          const problem = toApiError(error);
          this.notifier.show(problem.message);
          if (!problem.isUnexpected) {
            this.reload();
          }
        },
      });
  }

  private navigate(queryParams: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }
}

/** Who booked it; the account may have been deleted since. */
export function ownerOf(booking: BookingListItem): string {
  if (!booking.userDisplayName) {
    return 'a deleted account';
  }
  return booking.userEmail
    ? `${booking.userDisplayName} (${booking.userEmail})`
    : booking.userDisplayName;
}
