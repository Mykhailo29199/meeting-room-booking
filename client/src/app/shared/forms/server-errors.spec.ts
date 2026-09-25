import { FormControl, FormGroup, Validators } from '@angular/forms';
import { ApiError } from '../../core/api/api-error';
import { showApiErrorOnForm } from './server-errors';

function form() {
  return new FormGroup({
    email: new FormControl('ann@example.test', Validators.required),
    password: new FormControl('short'),
  });
}

function apiError(fieldErrors: Record<string, string[]>, message = 'Fix the fields.'): ApiError {
  return { status: 400, isUnexpected: false, message, fieldErrors };
}

describe('showApiErrorOnForm', () => {
  it('puts field errors on their fields', () => {
    const f = form();

    const message = showApiErrorOnForm(
      f,
      apiError({ password: ['Too short.', 'Needs a digit.'] }),
    );

    expect(message).toBeNull();
    expect(f.controls.password.getError('server')).toBe('Too short. Needs a digit.');
    expect(f.controls.password.touched).toBe(true);
  });

  it('clears a field error once the user edits the field', () => {
    const f = form();
    showApiErrorOnForm(f, apiError({ password: ['Too short.'] }));

    f.controls.password.setValue('longer-password');

    expect(f.controls.password.errors).toBeNull();
  });

  it('shows errors that match no field above the form', () => {
    const f = form();

    const message = showApiErrorOnForm(
      f,
      apiError({ '': ['Something about the account.'], email: ['Taken.'] }),
    );

    expect(message).toBe('Something about the account.');
    expect(f.controls.email.getError('server')).toBe('Taken.');
  });

  it('shows the message of an error without field errors', () => {
    expect(showApiErrorOnForm(form(), apiError({}, 'Invalid email or password.'))).toBe(
      'Invalid email or password.',
    );
  });
});
