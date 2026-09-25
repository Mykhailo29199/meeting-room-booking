import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from './app';
import { CurrentUser } from './core/api/models';
import { AuthService } from './core/auth/auth.service';

describe('App shell', () => {
  const user = signal<CurrentUser | null>(null);
  const signOut = vi.fn(() => user.set(null));

  beforeEach(() => {
    user.set(null);
    signOut.mockClear();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            user,
            isAdmin: computed(() => user()?.roles.includes('Admin') ?? false),
            signOut,
          },
        },
      ],
    });
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function signIn(roles: CurrentUser['roles']): void {
    user.set({ userId: 'user-1', email: 'ann@example.test', displayName: 'Ann', roles });
  }

  it('offers sign-in when signed out', async () => {
    const toolbar = (await render()).querySelector('mat-toolbar')!;

    expect(toolbar.textContent).toContain('Sign in');
    expect(toolbar.textContent).not.toContain('Sign out');
    expect(toolbar.querySelector('nav')).toBeNull();
  });

  it('shows who is signed in', async () => {
    signIn(['User']);
    const toolbar = (await render()).querySelector('mat-toolbar')!;

    expect(toolbar.textContent).toContain('Ann');
    expect(toolbar.textContent).not.toContain('(admin)');
    expect(toolbar.textContent).toContain('Sign out');
  });

  it('marks an admin', async () => {
    signIn(['Admin']);
    const toolbar = (await render()).querySelector('mat-toolbar')!;

    expect(toolbar.textContent).toContain('(admin)');
  });

  it('signs out and goes to the sign-in page', async () => {
    signIn(['User']);
    const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const element = await render();
    const button = [...element.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('Sign out'),
    )!;

    button.click();

    expect(signOut).toHaveBeenCalledOnce();
    expect(navigateByUrl).toHaveBeenCalledWith('/login');
  });
});
