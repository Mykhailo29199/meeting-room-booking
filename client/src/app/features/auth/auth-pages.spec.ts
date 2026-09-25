import { HttpErrorResponse } from '@angular/common/http';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { AuthResult } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { LoginPage } from './login-page';
import { RegisterPage } from './register-page';

const signedIn = {} as AuthResult;

function problem(status: number, body: object): Observable<never> {
  return throwError(() => new HttpErrorResponse({ status, error: body }));
}

describe('auth pages', () => {
  let answer: Observable<AuthResult>;
  let returnUrl: string | null;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    answer = of(signedIn);
    returnUrl = null;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { login: () => answer, register: () => answer },
        },
        {
          provide: ActivatedRoute,
          useFactory: () => ({
            snapshot: { queryParamMap: convertToParamMap(returnUrl ? { returnUrl } : {}) },
          }),
        },
      ],
    });
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  async function render<T>(page: Type<T>): Promise<ComponentFixture<T>> {
    const fixture = TestBed.createComponent(page);
    await fixture.whenStable();
    return fixture;
  }

  function type(fixture: ComponentFixture<unknown>, name: string, value: string): void {
    const input = fixture.nativeElement.querySelector(
      `[formControlName="${name}"]`,
    ) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submit(fixture: ComponentFixture<unknown>): Promise<void> {
    (fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
  }

  function text(fixture: ComponentFixture<unknown>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  describe('LoginPage', () => {
    it('goes back to the page that asked for sign-in', async () => {
      returnUrl = '/resources/42?date=2026-10-01';
      const fixture = await render(LoginPage);
      type(fixture, 'email', 'ann@example.test');
      type(fixture, 'password', 'secret');

      await submit(fixture);

      expect(navigateByUrl).toHaveBeenCalledWith('/resources/42?date=2026-10-01');
    });

    it('goes home instead of to another site', async () => {
      returnUrl = '//evil.example/';
      const fixture = await render(LoginPage);
      type(fixture, 'email', 'ann@example.test');
      type(fixture, 'password', 'secret');

      await submit(fixture);

      expect(navigateByUrl).toHaveBeenCalledWith('/resources');
    });

    it('shows the server message for a wrong password', async () => {
      answer = problem(401, { detail: 'Invalid email or password.' });
      const fixture = await render(LoginPage);
      type(fixture, 'email', 'ann@example.test');
      type(fixture, 'password', 'wrong');

      await submit(fixture);

      const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Invalid email or password.');
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('does not send an incomplete form', async () => {
      const fixture = await render(LoginPage);

      await submit(fixture);

      expect(text(fixture)).toContain('Enter your email.');
      expect(navigateByUrl).not.toHaveBeenCalled();
    });
  });

  describe('RegisterPage', () => {
    it('shows the server password rules on the password field', async () => {
      answer = problem(400, {
        detail: 'One or more fields are invalid.',
        errors: { Password: ['Passwords must have at least one digit.'] },
      });
      const fixture = await render(RegisterPage);
      type(fixture, 'displayName', 'Ann');
      type(fixture, 'email', 'ann@example.test');
      type(fixture, 'password', 'password');

      await submit(fixture);

      const errors = fixture.nativeElement.querySelectorAll('mat-error') as NodeListOf<HTMLElement>;
      expect([...errors].map((e) => e.textContent?.trim())).toEqual([
        'Passwords must have at least one digit.',
      ]);
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    });

    it('goes home after creating the account', async () => {
      const fixture = await render(RegisterPage);
      type(fixture, 'displayName', 'Ann');
      type(fixture, 'email', 'ann@example.test');
      type(fixture, 'password', 'Secret123!');

      await submit(fixture);

      expect(navigateByUrl).toHaveBeenCalledWith('/resources');
    });
  });
});
