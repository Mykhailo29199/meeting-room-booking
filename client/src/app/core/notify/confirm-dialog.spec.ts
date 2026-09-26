import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Confirmer } from './confirm-dialog';

describe('Confirmer', () => {
  async function ask(): Promise<{ answer: () => boolean | undefined; dialog: HTMLElement }> {
    let answer: boolean | undefined;
    TestBed.inject(Confirmer)
      .confirm({
        title: 'Cancel this booking?',
        message: 'Room A, 10:00–11:00.',
        confirm: 'Cancel booking',
        dismiss: 'Keep it',
      })
      .subscribe((confirmed) => (answer = confirmed));
    await TestBed.inject(MatDialog).openDialogs[0].afterOpened().toPromise();
    const dialog = document.querySelector('mat-dialog-container') as HTMLElement;
    return { answer: () => answer, dialog };
  }

  function buttonNamed(dialog: HTMLElement, name: string): HTMLButtonElement {
    return [...dialog.querySelectorAll('button')].find((b) => b.textContent?.trim() === name)!;
  }

  async function closed(): Promise<void> {
    await TestBed.inject(MatDialog).openDialogs[0]?.afterClosed().toPromise();
  }

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
  });

  it('asks the question with named buttons', async () => {
    const { dialog } = await ask();

    expect(dialog.textContent).toContain('Cancel this booking?');
    expect(dialog.textContent).toContain('Room A, 10:00–11:00.');
    expect(buttonNamed(dialog, 'Cancel booking')).toBeDefined();
    expect(buttonNamed(dialog, 'Keep it')).toBeDefined();
  });

  it('answers true when confirmed', async () => {
    const { answer, dialog } = await ask();

    const done = closed();
    buttonNamed(dialog, 'Cancel booking').click();
    await done;

    expect(answer()).toBe(true);
  });

  it('answers false when dismissed', async () => {
    const { answer, dialog } = await ask();

    const done = closed();
    buttonNamed(dialog, 'Keep it').click();
    await done;

    expect(answer()).toBe(false);
  });

  it('answers false when closed any other way (Escape, backdrop)', async () => {
    const { answer } = await ask();

    const done = closed();
    TestBed.inject(MatDialog).openDialogs[0].close();
    await done;

    expect(answer()).toBe(false);
  });
});
