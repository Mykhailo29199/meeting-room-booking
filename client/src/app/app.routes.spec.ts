import { routes } from './app.routes';
import { adminGuard, authGuard, guestGuard } from './core/auth/auth.guards';

// Guards only shape navigation (the API authorizes every request), but a
// page behind the wrong guard would show users actions that can only fail.
describe('routes', () => {
  function guardsOf(path: string): unknown[] {
    const route = routes.find((r) => r.path === path);
    expect(route, `route ${path}`).toBeDefined();
    return route!.canActivate ?? [];
  }

  it.each(['login', 'register'])('%s is for signed-out users', (path) => {
    expect(guardsOf(path)).toEqual([guestGuard]);
  });

  it.each(['resources', 'resources/:id', 'my-bookings'])('%s needs a signed-in user', (path) => {
    expect(guardsOf(path)).toEqual([authGuard]);
  });

  it.each(['admin/resources', 'admin/bookings'])('%s is for admins', (path) => {
    expect(guardsOf(path)).toEqual([adminGuard]);
  });
});
