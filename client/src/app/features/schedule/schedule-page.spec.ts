import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { NavigationExtras, provideRouter, Router } from '@angular/router';
import { filter, map, NEVER, Observable, of, Subject, throwError } from 'rxjs';
import type { MockInstance } from 'vitest';
import { BookingsApi } from '../../core/api/bookings-api';
import { Booking, ResourceSchedule, Slot, SlotsChangedMessage } from '../../core/api/models';
import { ResourcesApi } from '../../core/api/resources-api';
import { Notifier } from '../../core/notify/notifier';
import { ScheduleHubService } from '../../core/realtime/schedule-hub.service';
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
  let bookingAnswer: Observable<Booking>;
  let create: ReturnType<typeof vi.fn>;
  let show: ReturnType<typeof vi.fn>;
  let hubChanges: Subject<SlotsChangedMessage>;
  let hubJoins: Subject<string>;
  let watch: ReturnType<typeof vi.fn>;
  let unwatch: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    answers = [];
    scheduleApi = vi.fn(() => answers.shift() ?? of(schedule()));
    bookingAnswer = of({} as Booking);
    create = vi.fn(() => bookingAnswer);
    show = vi.fn();
    hubChanges = new Subject();
    hubJoins = new Subject();
    watch = vi.fn();
    unwatch = vi.fn();
    viewerTimeZone = 'Europe/Berlin';
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ResourcesApi, useValue: { schedule: scheduleApi } },
        { provide: BookingsApi, useValue: { create } },
        { provide: Notifier, useValue: { show } },
        {
          provide: ScheduleHubService,
          useValue: {
            watch,
            unwatch,
            slotsChanged: (id: string) =>
              hubChanges.pipe(
                filter((message) => message.resourceId === id),
                map((message) => message.slots),
              ),
            joined: (id: string) =>
              hubJoins.pipe(
                filter((joined) => joined === id),
                map(() => undefined),
              ),
          },
        },
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
    return [...page(fixture).querySelectorAll<HTMLElement>('.slot')];
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

  describe('booking', () => {
    // Berlin 10:00, 10:15, 10:30 free | 10:45 booked | 11:00 free.
    const freeDay = (): ResourceSchedule =>
      schedule({
        slots: [
          slot('2026-10-01T08:00:00Z', '2026-10-01T08:15:00Z'),
          slot('2026-10-01T08:15:00Z', '2026-10-01T08:30:00Z'),
          slot('2026-10-01T08:30:00Z', '2026-10-01T08:45:00Z'),
          slot('2026-10-01T08:45:00Z', '2026-10-01T09:00:00Z', { isBooked: true }),
          slot('2026-10-01T09:00:00Z', '2026-10-01T09:15:00Z'),
        ],
      });

    beforeEach(() => {
      scheduleApi.mockImplementation(() => answers.shift() ?? of(freeDay()));
    });

    function slotButton(fixture: ComponentFixture<unknown>, time: string): HTMLButtonElement {
      return slotRows(fixture).find((row) =>
        row.querySelector('.time')?.textContent?.trim().startsWith(time),
      ) as HTMLButtonElement;
    }

    async function click(fixture: ComponentFixture<unknown>, element: HTMLElement): Promise<void> {
      element.click();
      await fixture.whenStable();
    }

    function summary(fixture: ComponentFixture<unknown>): string {
      return page(fixture).querySelector('.summary')?.textContent?.trim() ?? '';
    }

    function selectedTimes(fixture: ComponentFixture<unknown>): string[] {
      return slotRows(fixture)
        .filter((row) => row.getAttribute('aria-pressed') === 'true')
        .map((row) => row.querySelector('.time')!.textContent!.trim());
    }

    it('chooses a range by clicking free slots', async () => {
      const fixture = await render('2026-10-01');

      await click(fixture, slotButton(fixture, '10:00'));
      await click(fixture, slotButton(fixture, '10:30'));

      expect(summary(fixture)).toBe('10:00–10:45, 45 min');
      expect(selectedTimes(fixture)).toEqual(['10:00–10:15', '10:15–10:30', '10:30–10:45']);
      expect(slotButton(fixture, '10:15').textContent).toContain('Selected');
      expect(slotButton(fixture, '10:45').disabled).toBe(true);
    });

    it('offers only ends reachable through free slots', async () => {
      const fixture = await render('2026-10-01');
      const loader = TestbedHarnessEnvironment.loader(fixture);
      const [start, end] = await loader.getAllHarnesses(MatSelectHarness);

      await start.open();
      await start.clickOptions({ text: '10:00' });
      await end.open();
      const ends = await Promise.all((await end.getOptions()).map((option) => option.getText()));
      await end.clickOptions({ text: '10:30 (30 min)' });

      expect(ends).toEqual(['10:15 (15 min)', '10:30 (30 min)', '10:45 (45 min)']);
      expect(summary(fixture)).toBe('10:00–10:30, 30 min');
    });

    it("books exactly the schedule's UTC values, then reloads", async () => {
      const fixture = await render('2026-10-01');
      await click(fixture, slotButton(fixture, '10:00'));
      await click(fixture, slotButton(fixture, '10:15'));

      await click(fixture, button(fixture, 'Book'));

      expect(create).toHaveBeenCalledExactlyOnceWith({
        resourceId: 'r1',
        startUtc: '2026-10-01T08:00:00Z',
        endUtc: '2026-10-01T08:30:00Z',
      });
      expect(show).toHaveBeenCalledWith('Booked 10:00–10:30, 30 min.');
      expect(scheduleApi).toHaveBeenCalledTimes(2);
      expect(selectedTimes(fixture)).toEqual([]);
    });

    it('sends one request however often Book is pressed', async () => {
      bookingAnswer = NEVER;
      const fixture = await render('2026-10-01');
      await click(fixture, slotButton(fixture, '10:00'));

      await click(fixture, button(fixture, 'Book'));
      await click(fixture, button(fixture, 'Book'));

      expect(create).toHaveBeenCalledOnce();
      expect(button(fixture, 'Book').disabled).toBe(true);
    });

    it('explains a conflict, reloads and clears the choice', async () => {
      bookingAnswer = throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { detail: 'This time has just been booked by someone else.' },
          }),
      );
      const fixture = await render('2026-10-01');
      await click(fixture, slotButton(fixture, '10:00'));

      await click(fixture, button(fixture, 'Book'));

      expect(show).toHaveBeenCalledWith('This time has just been booked by someone else.');
      expect(scheduleApi).toHaveBeenCalledTimes(2);
      expect(selectedTimes(fixture)).toEqual([]);
      expect(summary(fixture)).toBe('Choose a start time, or click a free slot below.');
    });

    it('shows a rule the server enforced, and keeps a choice that is still free', async () => {
      bookingAnswer = throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              detail: 'One or more fields are invalid.',
              errors: { StartUtc: ['Bookings must be made in advance.'] },
            },
          }),
      );
      const fixture = await render('2026-10-01');
      await click(fixture, slotButton(fixture, '10:00'));

      await click(fixture, button(fixture, 'Book'));

      expect(show).toHaveBeenCalledWith('Bookings must be made in advance.');
      expect(scheduleApi).toHaveBeenCalledTimes(2);
      expect(selectedTimes(fixture)).toEqual(['10:00–10:15']);
    });

    it('drops a choice that the reloaded schedule shows as taken', async () => {
      bookingAnswer = throwError(
        () => new HttpErrorResponse({ status: 400, error: { detail: 'That slot has started.' } }),
      );
      const fixture = await render('2026-10-01');
      await click(fixture, slotButton(fixture, '10:00'));
      const started = freeDay();
      started.slots[0] = { ...started.slots[0], isPast: true };
      answers.push(of(started));

      await click(fixture, button(fixture, 'Book'));

      expect(show).toHaveBeenCalledWith('That slot has started.');
      expect(selectedTimes(fixture)).toEqual([]);
    });

    it('keeps the choice after a network failure so the user can try again', async () => {
      bookingAnswer = throwError(() => new HttpErrorResponse({ status: 0 }));
      const fixture = await render('2026-10-01');
      await click(fixture, slotButton(fixture, '10:00'));

      await click(fixture, button(fixture, 'Book'));

      expect(show).toHaveBeenCalledWith('Something went wrong. Please try again.');
      expect(scheduleApi).toHaveBeenCalledOnce();
      expect(summary(fixture)).toBe('10:00–10:15, 15 min');
      expect(button(fixture, 'Book').disabled).toBe(false);
    });

    it('clears the choice', async () => {
      const fixture = await render('2026-10-01');
      await click(fixture, slotButton(fixture, '10:00'));

      await click(fixture, button(fixture, 'Clear'));

      expect(selectedTimes(fixture)).toEqual([]);
      expect(button(fixture, 'Book').disabled).toBe(true);
    });

    it('offers nothing to book on a removed resource', async () => {
      answers.push(of({ ...freeDay(), isActive: false }));

      const fixture = await render('2026-10-01');

      expect(page(fixture).querySelector('.booking')).toBeNull();
      expect(slotRows(fixture).every((row) => (row as HTMLButtonElement).disabled)).toBe(true);
    });

    it('says when no slot is left to book', async () => {
      const full = freeDay();
      full.slots = full.slots.map((s) => ({ ...s, isBooked: true }));
      answers.push(of(full));

      const fixture = await render('2026-10-01');

      expect(page(fixture).querySelector('.booking')).toBeNull();
      expect(page(fixture).textContent).toContain('No free slots left on this day.');
    });

    describe('live updates', () => {
      function changed(start: string, end: string, isBooked: boolean): void {
        hubChanges.next({ resourceId: 'r1', slots: [{ startUtc: start, endUtc: end, isBooked }] });
      }

      function status(fixture: ComponentFixture<unknown>, time: string): string {
        return slotButton(fixture, time)
          .querySelector('.status')!
          .textContent!.replace(/\s+/g, ' ')
          .trim();
      }

      it('watches the resource while the page shows it', async () => {
        const fixture = await render('2026-10-01');
        expect(watch).toHaveBeenCalledExactlyOnceWith('r1');

        fixture.destroy();

        expect(unwatch).toHaveBeenCalledExactlyOnceWith('r1');
      });

      it('switches to another resource opened on the same page', async () => {
        const fixture = await render('2026-10-01');

        fixture.componentRef.setInput('id', 'r2');
        await fixture.whenStable();

        expect(unwatch).toHaveBeenCalledExactlyOnceWith('r1');
        expect(watch.mock.calls).toEqual([['r1'], ['r2']]);
      });

      it('shows slots booked by someone else without reloading', async () => {
        const fixture = await render('2026-10-01');

        changed('2026-10-01T08:00:00Z', '2026-10-01T08:15:00Z', true);
        await fixture.whenStable();

        expect(status(fixture, '10:00')).toBe('event_busy Booked');
        expect(slotButton(fixture, '10:00').disabled).toBe(true);
        expect(scheduleApi).toHaveBeenCalledOnce();
      });

      it('shows slots that became free', async () => {
        const fixture = await render('2026-10-01');

        changed('2026-10-01T08:45:00Z', '2026-10-01T09:00:00Z', false);
        await fixture.whenStable();

        expect(status(fixture, '10:45')).toBe('event_available Free');
        expect(slotButton(fixture, '10:45').disabled).toBe(false);
      });

      it('clears a choice that someone else just booked, and says so', async () => {
        const fixture = await render('2026-10-01');
        await click(fixture, slotButton(fixture, '10:00'));
        await click(fixture, slotButton(fixture, '10:15'));

        changed('2026-10-01T08:15:00Z', '2026-10-01T08:30:00Z', true);
        await fixture.whenStable();

        expect(selectedTimes(fixture)).toEqual([]);
        expect(show).toHaveBeenCalledWith(
          'The time you chose has just been booked by someone else. Please choose another.',
        );
      });

      it('keeps a choice when other slots change', async () => {
        const fixture = await render('2026-10-01');
        await click(fixture, slotButton(fixture, '10:00'));

        changed('2026-10-01T09:00:00Z', '2026-10-01T09:15:00Z', true);
        await fixture.whenStable();

        expect(selectedTimes(fixture)).toEqual(['10:00–10:15']);
        expect(show).not.toHaveBeenCalled();
      });

      it("leaves the user's own booking in flight to its response", async () => {
        bookingAnswer = NEVER;
        const fixture = await render('2026-10-01');
        await click(fixture, slotButton(fixture, '10:00'));
        await click(fixture, button(fixture, 'Book'));

        // The server announces the booking before its response arrives.
        changed('2026-10-01T08:00:00Z', '2026-10-01T08:15:00Z', true);
        await fixture.whenStable();

        expect(show).not.toHaveBeenCalled();
        expect(summary(fixture)).toBe('10:00–10:15, 15 min');
      });

      it('reloads after (re)joining, to catch up on missed changes', async () => {
        const fixture = await render('2026-10-01');

        hubJoins.next('r2');
        hubJoins.next('r1');
        await fixture.whenStable();

        expect(scheduleApi).toHaveBeenCalledTimes(2);
      });
    });
  });
});
