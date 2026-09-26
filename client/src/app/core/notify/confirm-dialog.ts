import { ChangeDetectionStrategy, Component, inject, Injectable } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { map, Observable } from 'rxjs';

/** What a confirmation asks. */
export interface Confirmation {
  title: string;
  message: string;
  /** The button that goes ahead, named after the action ("Cancel booking"). */
  confirm: string;
  /** The button that changes nothing. */
  dismiss?: string;
}

/** A yes/no question before an action that cannot be undone. */
@Component({
  selector: 'app-confirm-dialog',
  imports: [MatButtonModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="false">
        {{ data.dismiss ?? 'Back' }}
      </button>
      <button mat-flat-button type="button" [mat-dialog-close]="true">{{ data.confirm }}</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmDialog {
  protected readonly data = inject<Confirmation>(MAT_DIALOG_DATA);
}

@Injectable({ providedIn: 'root' })
export class Confirmer {
  private readonly dialog = inject(MatDialog);

  /** True if the user confirmed; false if they dismissed the dialog in any way. */
  confirm(confirmation: Confirmation): Observable<boolean> {
    return this.dialog
      .open<ConfirmDialog, Confirmation, boolean>(ConfirmDialog, { data: confirmation })
      .afterClosed()
      .pipe(map((confirmed) => confirmed === true));
  }
}
