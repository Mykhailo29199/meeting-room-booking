import { HttpErrorResponse } from '@angular/common/http';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Observable, of, throwError } from 'rxjs';
import { Resource } from '../../../core/api/models';
import { ResourcesApi } from '../../../core/api/resources-api';
import { VIEWER_TIME_ZONE } from '../../../core/time/viewer-time-zone';
import { ResourceFormDialog, ResourceFormResult } from './resource-form-dialog';

const room: Resource = {
  id: 'r1',
  name: 'Room A',
  capacity: 8,
  timeZoneId: 'Europe/Berlin',
  opensAt: '08:00:00',
  closesAt: '18:00:00',
  isActive: true,
  version: 'v1',
};

describe('ResourceFormDialog', () => {
  let saveAnswer: Observable<Resource>;
  let create: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let getAnswer: Observable<Resource>;
  let get: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    saveAnswer = of(room);
    getAnswer = of(room);
    create = vi.fn(() => saveAnswer);
    update = vi.fn(() => saveAnswer);
    get = vi.fn(() => getAnswer);
    TestBed.configureTestingModule({
      providers: [
        { provide: ResourcesApi, useValue: { create, update, get } },
        { provide: VIEWER_TIME_ZONE, useValue: 'Europe/Berlin' },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
  });

  let dialogRef: MatDialogRef<ResourceFormDialog, ResourceFormResult>;
  let result: ResourceFormResult | undefined;

  async function open(resource: Resource | null): Promise<HTMLElement> {
    result = undefined;
    dialogRef = TestBed.inject(MatDialog).open<
      ResourceFormDialog,
      { resource: Resource | null },
      ResourceFormResult
    >(ResourceFormDialog, { data: { resource } });
    dialogRef.afterClosed().subscribe((value) => (result = value));
    await dialogRef.afterOpened().toPromise();
    await stable();
    return document.querySelector('mat-dialog-container') as HTMLElement;
  }

  async function stable(): Promise<void> {
    await TestBed.inject(ApplicationRef).whenStable();
  }

  function field(dialog: HTMLElement, name: string): HTMLInputElement {
    return dialog.querySelector(`[formControlName="${name}"]`) as HTMLInputElement;
  }

  async function type(dialog: HTMLElement, name: string, value: string): Promise<void> {
    const input = field(dialog, name);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    await stable();
  }

  function button(dialog: HTMLElement, name: string): HTMLButtonElement {
    return [...dialog.querySelectorAll('button')].find((b) => b.textContent?.trim() === name)!;
  }

  async function press(dialog: HTMLElement, name: string): Promise<void> {
    button(dialog, name).click();
    await stable();
  }

  it("adds a resource, starting in the admin's zone", async () => {
    const dialog = await open(null);
    expect(dialog.textContent).toContain('Add a resource');
    expect(field(dialog, 'timeZoneId').value).toBe('Europe/Berlin');

    await type(dialog, 'name', ' Room A ');
    await type(dialog, 'capacity', '8');
    await press(dialog, 'Add');
    await dialogRef.afterClosed().toPromise();

    expect(create).toHaveBeenCalledExactlyOnceWith({
      name: 'Room A',
      capacity: 8,
      timeZoneId: 'Europe/Berlin',
      opensAt: '08:00:00',
      closesAt: '18:00:00',
    });
    expect(result).toEqual({ saved: room });
  });

  it('edits a resource, sending the version it was loaded with', async () => {
    const dialog = await open(room);
    expect(field(dialog, 'name').value).toBe('Room A');

    await type(dialog, 'closesAt', '20:00');
    await press(dialog, 'Save');

    expect(update).toHaveBeenCalledExactlyOnceWith('r1', {
      name: 'Room A',
      capacity: 8,
      timeZoneId: 'Europe/Berlin',
      opensAt: '08:00:00',
      closesAt: '20:00:00',
      version: 'v1',
    });
  });

  it('does not send hours that break the rules', async () => {
    const dialog = await open(room);

    await type(dialog, 'opensAt', '08:10');
    await type(dialog, 'closesAt', '07:00');
    await press(dialog, 'Save');

    expect(update).not.toHaveBeenCalled();
    expect(dialog.textContent).toContain('Use a quarter hour');
    expect(dialog.textContent).toContain('It must close after it opens.');
  });

  it('does not send a time zone that is not in the list', async () => {
    const dialog = await open(room);

    await type(dialog, 'timeZoneId', 'Berlin');
    await press(dialog, 'Save');

    expect(update).not.toHaveBeenCalled();
    expect(dialog.textContent).toContain('Choose a time zone from the list.');
  });

  it("shows the server's reason and stays open", async () => {
    saveAnswer = throwError(
      () =>
        new HttpErrorResponse({
          status: 400,
          error: { detail: 'Opening hours must start and end on a 15-minute boundary.' },
        }),
    );
    const dialog = await open(room);

    await press(dialog, 'Save');

    expect(dialog.querySelector('[role="alert"]')?.textContent).toContain(
      'Opening hours must start and end on a 15-minute boundary.',
    );
    expect(result).toBeUndefined();
    expect(button(dialog, 'Save').disabled).toBe(false);
  });

  it('offers to reload after someone else changed the resource, then saves on the new version', async () => {
    saveAnswer = throwError(() => new HttpErrorResponse({ status: 409, error: {} }));
    getAnswer = of({ ...room, name: 'Room A (renamed)', version: 'v2' });
    const dialog = await open(room);

    await type(dialog, 'capacity', '10');
    await press(dialog, 'Save');

    expect(dialog.querySelector('[role="alert"]')?.textContent).toContain(
      'Someone else changed this resource',
    );
    expect(button(dialog, 'Save').disabled).toBe(true);

    await press(dialog, 'Reload');

    expect(get).toHaveBeenCalledWith('r1');
    expect(field(dialog, 'name').value).toBe('Room A (renamed)');
    expect(field(dialog, 'capacity').value).toBe('8');
    expect(dialog.querySelector('[role="alert"]')).toBeNull();

    saveAnswer = of(room);
    await press(dialog, 'Save');
    expect(update.mock.lastCall?.[1]).toMatchObject({ name: 'Room A (renamed)', version: 'v2' });
  });

  it('closes when the resource no longer exists', async () => {
    saveAnswer = throwError(() => new HttpErrorResponse({ status: 404, error: {} }));
    const dialog = await open(room);

    await press(dialog, 'Save');
    await dialogRef.afterClosed().toPromise();

    expect(result).toEqual({ gone: true });
  });
});
