import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AppToolbar } from './shared/layout/app-toolbar';

/** The app shell: the toolbar, and the current page below it. */
@Component({
  imports: [AppToolbar, RouterOutlet],
  selector: 'app-root',
  template: `
    <app-toolbar />
    <main>
      <router-outlet />
    </main>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
