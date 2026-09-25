import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { LOGIN_URL } from '../../core/auth/auth-urls';
import { AuthService } from '../../core/auth/auth.service';

/** The bar on top of every page: navigation, who is signed in, sign out. */
@Component({
  selector: 'app-toolbar',
  imports: [MatButtonModule, MatIconModule, MatToolbarModule, RouterLink, RouterLinkActive],
  templateUrl: './app-toolbar.html',
  styleUrl: './app-toolbar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppToolbar {
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigateByUrl(LOGIN_URL);
  }
}
