import { ChangeDetectionStrategy, Component } from '@angular/core';

/** The resources a user can book. The list itself is not built yet. */
@Component({
  selector: 'app-resource-list-page',
  template: '<h1>Resources</h1>',
  styles: `
    :host {
      display: block;
      padding: 16px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResourceListPage {}
