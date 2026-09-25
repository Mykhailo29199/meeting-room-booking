import { FormGroup } from '@angular/forms';
import { ApiError } from '../../core/api/api-error';

/**
 * Shows a failed submit on a form: each server field error goes on the
 * control of the same name as the `server` validation error (cleared by
 * Angular as soon as the user edits that field). Returns the message to show
 * above the form, or null if everything went on fields.
 */
export function showApiErrorOnForm(form: FormGroup, error: ApiError): string | null {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const control = field ? form.get(field) : null;
    if (control) {
      control.setErrors({ ...control.errors, server: messages.join(' ') });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }

  if (unmatched.length > 0) {
    return unmatched.join(' ');
  }
  return Object.keys(error.fieldErrors).length > 0 ? null : error.message;
}
