import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { BookingsApi } from '../../core/api/bookings-api';
import { BookingListItem, CancellationResult } from '../../core/api/models';
import { Confirmation, Confirmer } from '../../core/notify/confirm-dialog';
import { Notifier } from '../../core/notify/notifier';
import { VIEWER_TIME_ZONE } from '../../core/time/viewer-time-zone';
import { MyBookingsPage } from './my-bookings-page';

// "Now" is 1 October 2026, 10:10 in Berlin (UTC+2).
const NOW = '2026-10-01T08:10:00Z';

function booking(id: string, startUtc: string, endUtc: string): BookingListItem {
  return {
    id,
    resourceId: 'r1',
    resourceName: 'Room A',
    resourceTimeZoneId: 'Europe/Berlin',
    userId: 'u1',
    userDisplayName: 'Ann',
    userEmail: 'ann@example.test',
    startUtc,
    endUtc,
    createdAtUtc: '2026-09-01T08:00:00Z',
  };
}

const past = booking('b-past', '2026-09-30T08:00:00Z', '2026-09-30T09:00:00Z');
const underWay = booking('b-now', '2026-10-01T08:00:00Z', '2026-10-01T09:00:00Z');
const upcoming = booking('b-next', '2026-10-02T12:00:00Z', '2026-10-02T13:30:00Z');

describe('MyBookingsPage', () => {
  let answers: Observable<BookingListItem[]>[];
  let mine: ReturnType<typeof vi.fn>;
  let cancelAnswer: Observable<CancellationResult>;
  let cancel: ReturnType<typeof vi.fn>;
  let confirmed: boolean;
  let confirm: ReturnType<typeof vi.fn>;
  let show: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(NOW));
    answers = [];
    mine = vi.fn(() => answers.shift() ?? of([underWay, upcoming]));
    cancelAnswer = of({ cancelledCompletely: true, remainingBooking: null });
    cancel = vi.fn(() => cancelAnswer);
    confirmed = true;
    confirm = vi.fn((_: Confirmation) => of(confirmed));
    show = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: BookingsApi, useValue: { mine, cancel } },
        { provide: Confirmer, useValue: { confirm } },
        { provide: Notifier, useValue: { show } },
        { provide: VIEWER_TIME_ZONE, useValue: 'Europe/Berlin' },
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  async function render(): Promise<ComponentFixture<MyBookingsPage>> {
    const fixture = TestBed.createComponent(MyBookingsPage);
    await fixture.whenStable();
    return fixture;
  }

  function page(fixture: ComponentFixture<unknown>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function rows(fixture: ComponentFixture<unknown>): HTMLElement[] {
    return [...page(fixture).querySelectorAll<HTMLElement>('li.booking')];
  }

  function actionOf(row: HTMLElement): HTMLButtonElement | null {
    return row.querySelector('button');
  }

  it("lists the user's bookings in the resource's time, with what can be done", async () => {
    answers.push(of([underWay, upcoming]));

    const [now, next] = rows(await render());

    expect(mine).toHaveBeenCalledWith(false);
    expect(now.textContent).toContain('Thursday, 1 October 2026, 10:00–11:00 (1 h)');
    expect(now.textContent).toContain('Berlin time (UTC+2)');
    expect(now.textContent).toContain('In progress');
    expect(actionOf(now)?.textContent?.trim()).toBe('End now');
    expect(next.textContent).toContain('Friday, 2 October 2026, 14:00–15:30 (1 h 30 min)');
    expect(next.textContent).toContain('Upcoming');
    expect(actionOf(next)?.textContent?.trim()).toBe('Cancel');
    expect(actionOf(next)?.getAttribute('aria-label')).toBe(
      'Cancel: Room A, Friday, 2 October 2026, 14:00–15:30',
    );
  });

  it("links each booking to that day's schedule", async () => {
    const [, next] = rows(await render());

    expect(next.querySelector('a')?.getAttribute('href')).toBe('/resources/r1?date=2026-10-02');
  });

  it('shows past bookings on request, without actions', async () => {
    const fixture = await render();
    answers.push(of([past]));

    (page(fixture).querySelector('mat-slide-toggle button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(mine).toHaveBeenLastCalledWith(true);
    const [row] = rows(fixture);
    expect(row.textContent).toContain('Past');
    expect(actionOf(row)).toBeNull();
  });

  it('says when there is nothing booked', async () => {
    answers.push(of([]));

    const fixture = await render();

    expect(page(fixture).textContent).toContain('You have no upcoming bookings.');
    expect(page(fixture).querySelector('a[href="/resources"]')).not.toBeNull();
  });

  it('cancels an upcoming booking after confirmation, then reloads', async () => {
    const fixture = await render();

    actionOf(rows(fixture)[1])!.click();
    await fixture.whenStable();

    const question = confirm.mock.lastCall![0] as Confirmation;
    expect(question.title).toBe('Cancel this booking?');
    expect(question.message).toContain('Room A, Friday, 2 October 2026, 14:00–15:30');
    expect(cancel).toHaveBeenCalledExactlyOnceWith('b-next');
    expect(show).toHaveBeenCalledWith('Booking cancelled.');
    expect(mine).toHaveBeenCalledTimes(2);
  });

  it('ends a booking under way and says when it now ends', async () => {
    cancelAnswer = of({
      cancelledCompletely: false,
      remainingBooking: {
        id: 'b-now',
        resourceId: 'r1',
        userId: 'u1',
        startUtc: '2026-10-01T08:00:00Z',
        endUtc: '2026-10-01T08:15:00Z',
      },
    });
    const fixture = await render();

    actionOf(rows(fixture)[0])!.click();
    await fixture.whenStable();

    expect((confirm.mock.lastCall![0] as Confirmation).title).toBe('End this booking now?');
    expect(cancel).toHaveBeenCalledExactlyOnceWith('b-now');
    expect(show).toHaveBeenCalledWith('Booking ended early: it now ends at 10:15.');
  });

  it('changes nothing when the user keeps the booking', async () => {
    confirmed = false;
    const fixture = await render();

    actionOf(rows(fixture)[1])!.click();
    await fixture.whenStable();

    expect(cancel).not.toHaveBeenCalled();
    expect(mine).toHaveBeenCalledOnce();
  });

  it("shows the server's reason when it cannot cancel, and reloads", async () => {
    cancelAnswer = throwError(
      () =>
        new HttpErrorResponse({
          status: 400,
          error: { detail: 'Nothing left to release: the booking is in its last slot.' },
        }),
    );
    const fixture = await render();

    actionOf(rows(fixture)[0])!.click();
    await fixture.whenStable();

    expect(show).toHaveBeenCalledWith('Nothing left to release: the booking is in its last slot.');
    expect(mine).toHaveBeenCalledTimes(2);
  });

  it('keeps the list after a network failure', async () => {
    cancelAnswer = throwError(() => new HttpErrorResponse({ status: 0 }));
    const fixture = await render();

    actionOf(rows(fixture)[1])!.click();
    await fixture.whenStable();

    expect(show).toHaveBeenCalledWith('Something went wrong. Please try again.');
    expect(mine).toHaveBeenCalledOnce();
    expect(actionOf(rows(fixture)[1])!.disabled).toBe(false);
  });

  it('offers to try again when the list cannot be loaded', async () => {
    answers.push(throwError(() => new HttpErrorResponse({ status: 503 })));
    const fixture = await render();
    const alert = page(fixture).querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Something went wrong. Please try again.');

    (alert.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(rows(fixture)).toHaveLength(2);
  });
});
