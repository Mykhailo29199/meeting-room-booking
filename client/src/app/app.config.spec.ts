import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatIconModule } from '@angular/material/icon';
import { appConfig } from './app.config';

@Component({
  imports: [MatIconModule],
  template: '<mat-icon>home</mat-icon>',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class IconHost {}

describe('appConfig', () => {
  it('renders icons with the icon font that index.html loads', async () => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
    const fixture = TestBed.createComponent(IconHost);
    await fixture.whenStable();

    const icon = (fixture.nativeElement as HTMLElement).querySelector('mat-icon')!;
    expect(icon.classList).toContain('material-symbols-outlined');
    expect(icon.classList).not.toContain('material-icons');
  });
});
