import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guards';

const appName = 'Meeting Room Booking';

/** Every page is loaded on first visit (`loadComponent`) and guarded. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'resources' },
  {
    path: 'login',
    title: `Sign in · ${appName}`,
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    title: `Create an account · ${appName}`,
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register-page').then((m) => m.RegisterPage),
  },
  {
    path: 'resources',
    title: `Resources · ${appName}`,
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/resources/resource-list-page').then((m) => m.ResourceListPage),
  },
  { path: '**', redirectTo: 'resources' },
];
