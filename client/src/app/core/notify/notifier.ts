import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

/**
 * Short messages about what just happened ("Booked 10:00–11:00.", a
 * conflict). Shown in a snackbar, which screen readers announce.
 */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly snackBar = inject(MatSnackBar);

  show(message: string): void {
    this.snackBar.open(message, 'Close', { duration: 6000 });
  }
}
