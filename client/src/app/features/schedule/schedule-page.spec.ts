import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NavigationExtras, provideRouter, Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import type { MockInstance } from 'vitest';
import { ResourceSchedule, Slot } from '../../core/api/models';
import { ResourcesApi } from '../../core/api/resources-api';
import { VIEWER_TIME_ZONE } from '../../core/time/viewer-time-zone';
import { SchedulePage } from './schedule-page';

// 1 October 2026: Berlin is UTC+2, London UTC+1.

function slot(startUtc: string, endUtc: string, state: Partial<Slot> = {}): Slot {
  return {
    startUtc,
    endUtc,
    isBooked: false,
    isMine: false,
    isPast: false,
    bookingId: null,
    ...state,
  };
}

function schedule(overrides: Partial<ResourceSchedule> = {}): ResourceSchedule {
  return {
    resourceId: 'r1',
    resourceName: 'Room A',
    timeZoneId: 'Europe/Berlin',
    localDate: '2026-10-01',
    isActive: true,
    slots: [
      slot('2026-10-01T07:45:00Z', '2026-10-01T08:00:00Z', { isPast: true }),
      slot('2026-10-01T08:00:00Z', '2026-10-01T08:15:00Z'),
      slot('2026-10-01T08:15:00Z', '2026-10-01T08:30:00Z', { isBooked: true }),
      slot('2026-10-01T08:30:00Z', '2026-10-01T08:45:00Z', {
        isBooked: true,
        isMine: true,
        bookingId: 'b1',
      }),
    ],
    ...overrides,
  };
}

describe('SchedulePage', () => {
  let answers: Observable<ResourceSchedule>[];
  let scheduleApi: ReturnType<typeof vi.fn>;
  let viewerTimeZone: string;
  let navigate: MockInstance<Router['navigate']>;

  beforeEach(() => {
    answers = [];
    scheduleApi = vi.fn(() => answers.shift() ?? of(schedule()));
    viewerTimeZone = 'Europe/Berlin';
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ResourcesApi, useValue: { schedule: scheduleApi } },
        { provide: VIEWER_TIME_ZONE, useFactory: () => viewerTimeZone },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  async function render(date?: string): Promise<ComponentFixture<SchedulePage>> {
    const fixture = TestBed.createComponent(SchedulePage);
    fixture.componentRef.setInput('id', 'r1');
    fixture.componentRef.setInput('date', date);
    await fixture.whenStable();
    return fixture;
  }

  function page(fixture: ComponentFixture<unknown>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function slotRows(fixture: ComponentFixture<unknown>): HTMLElement[] {
    return [...page(fixture).querySelectorAll<HTMLElement>('li.slot')];
  }

  function button(fixture: ComponentFixture<unknown>, name: string): HTMLButtonElement {
    return [...page(fixture).querySelectorAll('button')].find(
      (b) => b.getAttribute('aria-label') === name || b.textContent?.trim() === name,
    )!;
  }

  /** The `date` query parameter of every navigation, in order. */
  function navigatedDates(): unknown[] {
    return navigate.mock.calls.map(
      ([, extras]) => (extras as NavigationExtras).queryParams?.['date'],
    );
  }

  it("loads the day from the URL and shows its slots in the resource's time", async () => {
    const fixture = await render('2026-10-01');

    expect(scheduleApi).toHaveBeenCalledWith('r1', '2026-10-01');
    expect(page(fixture).querySelector('h1')?.textContent).toContain('Room A');
    expect(page(fixture).textContent).toContain('Thursday, 1 October 2026');
    expect(page(fixture).textContent).toContain('Times are Berlin time (UTC+2).');
    expect(
      slotRows(fixture).map((row) => [
        row.querySelector('.time')?.textContent?.trim(),
        row.dataset['status'],
        row.querySelector('.status')?.textContent?.replace(/\s+/g, ' ').trim(),
      ]),
    ).toEqual([
      ['09:45–10:00', 'past', 'history Past'],
      ['10:00–10:15', 'free', 'event_available Free'],
      ['10:15–10:30', 'booked', 'event_busy Booked'],
      ['10:30–10:45', 'mine', 'person Your booking'],
    ]);
  });

  it("labels the zone with the offset on the day shown, not today's", async () => {
    answers.push(
      of(
        schedule({
          localDate: '2026-11-10',
          slots: [slot('2026-11-10T08:00:00Z', '2026-11-10T08:15:00Z')],
        }),
      ),
    );

    const fixture = await render('2026-11-10');

    expect(page(fixture).textContent).toContain('Times are Berlin time (UTC+1).');
    expect(slotRows(fixture)[0].querySelector('.time')?.textContent?.trim()).toBe('09:00–09:15');
  });

  it("asks for today in the resource's zone when the URL has no valid date", async () => {
    await render('2026-02-30');

    expect(scheduleApi).toHaveBeenCalledWith('r1', undefined);
  });

  it('loads another day when the URL changes', async () => {
    const fixture = await render('2026-10-01');

    fixture.componentRef.setInput('date', '2026-10-02');
    await fixture.whenStable();

    expect(scheduleApi).toHaveBeenLastCalledWith('r1', '2026-10-02');
  });

  it("adds the viewer's own time when their zone differs", async () => {
    viewerTimeZone = 'Europe/London';

    const fixture = await render('2026-10-01');

    expect(page(fixture).textContent).toContain(
      'Your own time (London time (UTC+1)) is shown in brackets.',
    );
    expect(slotRows(fixture)[1].querySelector('.time')?.textContent).toContain(
      '10:00–10:15 (09:00 your time)',
    );
  });

  it('shows no hints to a viewer in the same zone', async () => {
    const fixture = await render('2026-10-01');

    expect(page(fixture).querySelector('.viewer-time')).toBeNull();
    expect(page(fixture).textContent).not.toContain('Your own time');
  });

  it('moves to the previous day, the next day and today through the URL', async () => {
    const fixture = await render('2026-10-01');

    button(fixture, 'Previous day').click();
    button(fixture, 'Next day').click();
    button(fixture, 'Today').click();

    expect(navigatedDates()).toEqual(['2026-09-30', '2026-10-02', null]);
  });

  it('moves to a day picked in the date field', async () => {
    const fixture = await render('2026-10-01');
    const input = page(fixture).querySelector('input') as HTMLInputElement;

    input.value = '10/5/2026';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('change'));

    expect(navigatedDates()).toEqual(['2026-10-05']);
  });

  it('says when a resource no longer exists', async () => {
    answers.push(throwError(() => new HttpErrorResponse({ status: 404, error: {} })));

    const fixture = await render('2026-10-01');

    const alert = page(fixture).querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('This resource no longer exists.');
    expect(alert.querySelector('button')).toBeNull();
    expect(page(fixture).querySelector('a[href="/resources"]')).not.toBeNull();
  });

  it('offers to try again after a failure', async () => {
    answers.push(throwError(() => new HttpErrorResponse({ status: 503 })));
    const fixture = await render('2026-10-01');
    const alert = page(fixture).querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Something went wrong. Please try again.');

    (alert.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(scheduleApi).toHaveBeenCalledTimes(2);
    expect(slotRows(fixture)).toHaveLength(4);
  });

  it('warns that a removed resource cannot be booked', async () => {
    answers.push(of(schedule({ isActive: false })));

    const fixture = await render('2026-10-01');

    expect(page(fixture).textContent).toContain(
      'This resource has been removed and cannot be booked.',
    );
  });
});
