import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { catchError, filter, map, merge, of, Subject, switchMap, tap } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { BookingsApi } from '../../core/api/bookings-api';
import { BookingListItem } from '../../core/api/models';
import { Confirmer } from '../../core/notify/confirm-dialog';
import { Notifier } from '../../core/notify/notifier';
import { VIEWER_TIME_ZONE } from '../../core/time/viewer-time-zone';
import {
  cancelAction,
  cancelConfirmation,
  cancellationMessage,
} from '../../shared/bookings/booking-cancellation';
import {
  BOOKING_STATE_LABELS,
  bookingState,
  describeBooking,
} from '../../shared/bookings/booking-display';

/**
 * The signed-in user's bookings, earliest first. An upcoming booking can be
 * cancelled; one under way can be ended now, which frees the slots that
 * have not started (the server decides what is left to free).
 */
@Component({
  selector: 'app-my-bookings-page',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, MatSlideToggleModule, RouterLink],
  templateUrl: './my-bookings-page.html',
  styleUrl: './my-bookings-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyBookingsPage {
  private readonly api = inject(BookingsApi);
  private readonly confirmer = inject(Confirmer);
  private readonly notifier = inject(Notifier);
  private readonly destroyRef = inject(DestroyRef);
  private readonly viewerTimeZone = inject(VIEWER_TIME_ZONE);

  protected readonly includePast = signal(false);
  private readonly bookings = signal<BookingListItem[] | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  /** The booking being cancelled, whose button is disabled meanwhile. */
  protected readonly cancelling = signal<string | null>(null);
  private readonly reloads = new Subject<void>();

  protected readonly items = computed(() => {
    const now = Date.now();
    return this.bookings()?.map((booking) => {
      const state = bookingState(booking, now);
      const display = describeBooking(booking, this.viewerTimeZone);
      const action = cancelAction(booking, now);
      return {
        booking,
        ...display,
        state,
        stateLabel: BOOKING_STATE_LABELS[state],
        action,
        // Buttons in a list need names that say which booking they act on.
        actionLabel: `${action}: ${booking.resourceName}, ${display.date}, ${display.time}`,
      };
    });
  });

  constructor() {
    merge(toObservable(this.includePast), this.reloads.pipe(map(() => this.includePast())))
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((includePast) =>
          this.api.mine(includePast).pipe(
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

  protected cancel(booking: BookingListItem): void {
    this.confirmer
      .confirm(cancelConfirmation(booking, this.viewerTimeZone, Date.now()))
      .pipe(
        filter((confirmed) => confirmed),
        tap(() => this.cancelling.set(booking.id)),
        switchMap(() => this.api.cancel(booking.id)),
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
          // E.g. already cancelled elsewhere (404) or nothing left to free (400).
          this.notifier.show(problem.message);
          if (!problem.isUnexpected) {
            this.reload();
          }
        },
      });
  }
}
