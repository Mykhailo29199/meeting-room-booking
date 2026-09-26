import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { Resource, ResourceRemovalResult } from '../../../core/api/models';
import { ResourcesApi } from '../../../core/api/resources-api';
import { Confirmation, Confirmer } from '../../../core/notify/confirm-dialog';
import { Notifier } from '../../../core/notify/notifier';
import { AdminResourcesPage, removalSummary } from './admin-resources-page';
import { ResourceFormResult } from './resource-form-dialog';

function resource(overrides: Partial<Resource>): Resource {
  return {
    id: 'r1',
    name: 'Room A',
    capacity: 8,
    timeZoneId: 'Europe/Berlin',
    opensAt: '08:00:00',
    closesAt: '18:00:00',
    isActive: true,
    version: 'v1',
    ...overrides,
  };
}

const roomA = resource({});
const oldRoom = resource({ id: 'r2', name: 'Old room', isActive: false });

describe('AdminResourcesPage', () => {
  let list: ReturnType<typeof vi.fn>;
  let removeAnswer: Observable<ResourceRemovalResult>;
  let remove: ReturnType<typeof vi.fn>;
  let restoreAnswer: Observable<Resource>;
  let restore: ReturnType<typeof vi.fn>;
  let dialogResult: ResourceFormResult | undefined;
  let open: ReturnType<typeof vi.fn>;
  let confirmed: boolean;
  let confirm: ReturnType<typeof vi.fn>;
  let show: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    list = vi.fn(() => of([roomA, oldRoom]));
    removeAnswer = of({ cancelledBookings: 2, shortenedBookings: 1 });
    remove = vi.fn(() => removeAnswer);
    restoreAnswer = of({ ...oldRoom, isActive: true });
    restore = vi.fn(() => restoreAnswer);
    dialogResult = undefined;
    open = vi.fn(() => ({ afterClosed: () => of(dialogResult) }));
    confirmed = true;
    confirm = vi.fn((_: Confirmation) => of(confirmed));
    show = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ResourcesApi, useValue: { list, remove, restore } },
        { provide: MatDialog, useValue: { open } },
        { provide: Confirmer, useValue: { confirm } },
        { provide: Notifier, useValue: { show } },
      ],
    });
  });

  async function render(): Promise<ComponentFixture<AdminResourcesPage>> {
    const fixture = TestBed.createComponent(AdminResourcesPage);
    await fixture.whenStable();
    return fixture;
  }

  function rows(fixture: ComponentFixture<unknown>): HTMLElement[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('li.resource')];
  }

  function buttonsOf(row: HTMLElement): string[] {
    return [...row.querySelectorAll('button')].map((b) => b.textContent!.trim());
  }

  async function press(fixture: ComponentFixture<unknown>, label: string): Promise<void> {
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      `button[aria-label="${label}"]`,
    ) as HTMLButtonElement;
    button.click();
    await fixture.whenStable();
  }

  async function add(fixture: ComponentFixture<unknown>): Promise<void> {
    const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (b) => b.textContent?.includes('Add resource'),
    )!;
    button.click();
    await fixture.whenStable();
  }

  it('lists every resource, removed ones with Restore instead of Edit and Remove', async () => {
    const [active, removed] = rows(await render());

    expect(active.textContent).toContain('Room A');
    expect(active.textContent).toContain('Active');
    expect(buttonsOf(active)).toEqual(['Edit', 'Remove']);
    expect(removed.textContent).toContain('Removed');
    expect(buttonsOf(removed)).toEqual(['Restore']);
  });

  it('adds a resource through the form dialog, then reloads', async () => {
    dialogResult = { saved: resource({ id: 'r3', name: 'Room C' }) };
    const fixture = await render();

    await add(fixture);

    expect(open.mock.lastCall?.[1]).toEqual({ data: { resource: null } });
    expect(show).toHaveBeenCalledWith('Room C added.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('edits a resource as loaded, including its version', async () => {
    dialogResult = { saved: { ...roomA, name: 'Room A+' } };
    const fixture = await render();

    await press(fixture, 'Edit Room A');

    expect(open.mock.lastCall?.[1]).toEqual({ data: { resource: roomA } });
    expect(show).toHaveBeenCalledWith('Room A+ saved.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('changes nothing when the form is closed without saving', async () => {
    const fixture = await render();

    await press(fixture, 'Edit Room A');

    expect(show).not.toHaveBeenCalled();
    expect(list).toHaveBeenCalledOnce();
  });

  it('says so and reloads when the edited resource no longer exists', async () => {
    dialogResult = { gone: true };
    const fixture = await render();

    await press(fixture, 'Edit Room A');

    expect(show).toHaveBeenCalledWith('This resource no longer exists.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('removes a resource after explaining what happens to its bookings', async () => {
    const fixture = await render();

    await press(fixture, 'Remove Room A');

    const question = confirm.mock.lastCall![0] as Confirmation;
    expect(question.title).toBe('Remove Room A?');
    expect(question.message).toContain('future bookings are cancelled');
    expect(remove).toHaveBeenCalledExactlyOnceWith('r1');
    expect(show).toHaveBeenCalledWith(
      'Room A removed. 2 future bookings cancelled, 1 booking under way ended.',
    );
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('keeps the resource when the admin changes their mind', async () => {
    confirmed = false;
    const fixture = await render();

    await press(fixture, 'Remove Room A');

    expect(remove).not.toHaveBeenCalled();
  });

  it('restores a removed resource', async () => {
    const fixture = await render();

    await press(fixture, 'Restore Old room');

    expect(restore).toHaveBeenCalledExactlyOnceWith('r2');
    expect(show).toHaveBeenCalledWith('Old room restored. Users can book it again.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it("shows the server's reason and the current state when an action fails", async () => {
    removeAnswer = throwError(
      () =>
        new HttpErrorResponse({ status: 404, error: { detail: 'The resource does not exist.' } }),
    );
    const fixture = await render();

    await press(fixture, 'Remove Room A');

    expect(show).toHaveBeenCalledWith('The resource does not exist.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('keeps the list as it is after a network failure', async () => {
    restoreAnswer = throwError(() => new HttpErrorResponse({ status: 0 }));
    const fixture = await render();

    await press(fixture, 'Restore Old room');

    expect(show).toHaveBeenCalledWith('Something went wrong. Please try again.');
    expect(list).toHaveBeenCalledOnce();
    expect(
      (
        (fixture.nativeElement as HTMLElement).querySelector(
          'button[aria-label="Restore Old room"]',
        ) as HTMLButtonElement
      ).disabled,
    ).toBe(false);
  });
});

describe('removalSummary', () => {
  it.each<[ResourceRemovalResult, string]>([
    [{ cancelledBookings: 0, shortenedBookings: 0 }, 'It had no future bookings.'],
    [{ cancelledBookings: 1, shortenedBookings: 0 }, '1 future booking cancelled.'],
    [{ cancelledBookings: 3, shortenedBookings: 0 }, '3 future bookings cancelled.'],
    [{ cancelledBookings: 0, shortenedBookings: 2 }, '2 bookings under way ended.'],
    [
      { cancelledBookings: 2, shortenedBookings: 1 },
      '2 future bookings cancelled, 1 booking under way ended.',
    ],
  ])('%o → %s', (result, text) => {
    expect(removalSummary(result)).toBe(text);
  });
});
