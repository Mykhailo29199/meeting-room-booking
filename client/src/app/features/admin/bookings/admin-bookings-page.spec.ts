import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatSlideToggleHarness } from '@angular/material/slide-toggle/testing';
import { NavigationExtras, provideRouter, Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import type { MockInstance } from 'vitest';
import { BookingsApi } from '../../../core/api/bookings-api';
import { BookingListItem, CancellationResult, Resource } from '../../../core/api/models';
import { ResourcesApi } from '../../../core/api/resources-api';
import { Confirmation, Confirmer } from '../../../core/notify/confirm-dialog';
import { Notifier } from '../../../core/notify/notifier';
import { VIEWER_TIME_ZONE } from '../../../core/time/viewer-time-zone';
import { AdminBookingsPage, BOOKING_LIST_LIMIT, ownerOf } from './admin-bookings-page';

// "Now" is 1 October 2026, 10:10 in Berlin (UTC+2).
const NOW = '2026-10-01T08:10:00Z';

function booking(overrides: Partial<BookingListItem>): BookingListItem {
  return {
    id: 'b1',
    resourceId: 'r1',
    resourceName: 'Room A',
    resourceTimeZoneId: 'Europe/Berlin',
    userId: 'u1',
    userDisplayName: 'Ann',
    userEmail: 'ann@example.test',
    startUtc: '2026-10-02T12:00:00Z',
    endUtc: '2026-10-02T13:00:00Z',
    createdAtUtc: '2026-09-01T08:00:00Z',
    ...overrides,
  };
}

function resource(id: string, name: string, isActive = true): Resource {
  return {
    id,
    name,
    capacity: 8,
    timeZoneId: 'Europe/Berlin',
    opensAt: '08:00:00',
    closesAt: '18:00:00',
    isActive,
    version: 'v1',
  };
}

describe('AdminBookingsPage', () => {
  let answers: Observable<BookingListItem[]>[];
  let all: ReturnType<typeof vi.fn>;
  let resourcesAnswer: Observable<Resource[]>;
  let cancelAnswer: Observable<CancellationResult>;
  let cancel: ReturnType<typeof vi.fn>;
  let confirmed: boolean;
  let confirm: ReturnType<typeof vi.fn>;
  let show: ReturnType<typeof vi.fn>;
  let navigate: MockInstance<Router['navigate']>;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(NOW));
    answers = [];
    all = vi.fn(() => answers.shift() ?? of([booking({})]));
    resourcesAnswer = of([resource('r1', 'Room A'), resource('r2', 'Old room', false)]);
    cancelAnswer = of({ cancelledCompletely: true, remainingBooking: null });
    cancel = vi.fn(() => cancelAnswer);
    confirmed = true;
    confirm = vi.fn((_: Confirmation) => of(confirmed));
    show = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: BookingsApi, useValue: { all, cancel } },
        { provide: ResourcesApi, useValue: { list: () => resourcesAnswer } },
        { provide: Confirmer, useValue: { confirm } },
        { provide: Notifier, useValue: { show } },
        { provide: VIEWER_TIME_ZONE, useValue: 'Europe/Berlin' },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  async function render(
    query: { resourceId?: string; past?: string } = {},
  ): Promise<ComponentFixture<AdminBookingsPage>> {
    const fixture = TestBed.createComponent(AdminBookingsPage);
    fixture.componentRef.setInput('resourceId', query.resourceId);
    fixture.componentRef.setInput('past', query.past);
    await fixture.whenStable();
    return fixture;
  }

  function page(fixture: ComponentFixture<unknown>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function rows(fixture: ComponentFixture<unknown>): HTMLElement[] {
    return [...page(fixture).querySelectorAll<HTMLElement>('li.booking')];
  }

  function lastQuery(): NavigationExtras['queryParams'] {
    return (navigate.mock.lastCall?.[1] as NavigationExtras).queryParams;
  }

  it("lists everyone's upcoming bookings with who booked them", async () => {
    answers.push(of([booking({}), booking({ id: 'b2', userDisplayName: null, userEmail: null })]));

    const fixture = await render();

    expect(all).toHaveBeenCalledWith({ resourceId: undefined, includePast: false });
    const [ann, gone] = rows(fixture);
    expect(ann.textContent).toContain('Friday, 2 October 2026, 14:00–15:00 (1 h)');
    expect(ann.textContent).toContain('Booked by Ann (ann@example.test)');
    expect(ann.querySelector('button')?.getAttribute('aria-label')).toBe(
      'Cancel: Room A, Friday, 2 October 2026, 14:00–15:00, Ann (ann@example.test)',
    );
    expect(gone.textContent).toContain('Booked by a deleted account');
  });

  it('applies the filters from the URL', async () => {
    await render({ resourceId: 'r2', past: 'true' });

    expect(all).toHaveBeenCalledWith({ resourceId: 'r2', includePast: true });
  });

  it('puts a chosen resource in the URL, and removes it for all resources', async () => {
    const fixture = await render();
    const select = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness);

    await select.open();
    const options = await Promise.all((await select.getOptions()).map((o) => o.getText()));
    await select.clickOptions({ text: 'Old room (removed)' });

    expect(options).toEqual(['All resources', 'Room A', 'Old room (removed)']);
    expect(lastQuery()).toEqual({ resourceId: 'r2' });
    expect(navigate.mock.lastCall?.[1]).toMatchObject({ queryParamsHandling: 'merge' });
  });

  it('clears the resource filter through the URL', async () => {
    const fixture = await render({ resourceId: 'r2' });
    const select = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness);

    await select.open();
    await select.clickOptions({ text: 'All resources' });

    expect(lastQuery()).toEqual({ resourceId: null });
  });

  it('puts "show past" in the URL', async () => {
    const fixture = await render();
    const toggle =
      await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSlideToggleHarness);

    await toggle.toggle();

    expect(lastQuery()).toEqual({ past: 'true' });
  });

  it("cancels anyone's booking after a confirmation that names its owner", async () => {
    const fixture = await render();

    rows(fixture)[0].querySelector('button')!.click();
    await fixture.whenStable();

    expect((confirm.mock.lastCall![0] as Confirmation).message).toContain(
      'Booked by Ann (ann@example.test).',
    );
    expect(cancel).toHaveBeenCalledExactlyOnceWith('b1');
    expect(show).toHaveBeenCalledWith('Booking cancelled.');
    expect(all).toHaveBeenCalledTimes(2);
  });

  it('changes nothing when the admin keeps the booking', async () => {
    confirmed = false;
    const fixture = await render();

    rows(fixture)[0].querySelector('button')!.click();
    await fixture.whenStable();

    expect(cancel).not.toHaveBeenCalled();
  });

  it("shows the server's reason when it cannot cancel, and reloads", async () => {
    cancelAnswer = throwError(
      () =>
        new HttpErrorResponse({ status: 404, error: { detail: 'The booking does not exist.' } }),
    );
    const fixture = await render();

    rows(fixture)[0].querySelector('button')!.click();
    await fixture.whenStable();

    expect(show).toHaveBeenCalledWith('The booking does not exist.');
    expect(all).toHaveBeenCalledTimes(2);
  });

  it('says when the list stops at the limit the API returns', async () => {
    answers.push(
      of(Array.from({ length: BOOKING_LIST_LIMIT }, (_, i) => booking({ id: `b${i}` }))),
    );

    const fixture = await render();

    expect(page(fixture).textContent).toContain(
      'Showing the first 500 bookings. Filter by resource to see the rest.',
    );
  });

  it('says when nothing matches', async () => {
    answers.push(of([]));

    const fixture = await render();

    expect(page(fixture).textContent).toContain('No upcoming bookings match.');
    expect(page(fixture).textContent).not.toContain('Showing the first');
  });

  it('still lists bookings when the resources for the filter cannot be loaded', async () => {
    resourcesAnswer = throwError(() => new HttpErrorResponse({ status: 0 }));

    const fixture = await render();

    expect(rows(fixture)).toHaveLength(1);
  });

  it('offers to try again when the bookings cannot be loaded', async () => {
    answers.push(throwError(() => new HttpErrorResponse({ status: 503 })));
    const fixture = await render();
    const alert = page(fixture).querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Something went wrong. Please try again.');

    (alert.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(rows(fixture)).toHaveLength(1);
  });
});

describe('ownerOf', () => {
  it('names the account, with its email when known', () => {
    expect(ownerOf(booking({}))).toBe('Ann (ann@example.test)');
    expect(ownerOf(booking({ userEmail: null }))).toBe('Ann');
    expect(ownerOf(booking({ userDisplayName: null, userEmail: null }))).toBe('a deleted account');
  });
});
