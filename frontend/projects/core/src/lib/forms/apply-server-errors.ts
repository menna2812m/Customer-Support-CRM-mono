import { FormGroup } from '@angular/forms';
import { AppError } from '../state/app-error';

/**
 * Binds server-side field failures onto the matching reactive form controls (spec FR-034).
 *
 * The server is the authority on validation (Constitution IV): a rule the client does not know
 * about still has to reach the field it belongs to. Anything that cannot be matched to a control
 * is attached to the form itself rather than dropped, because a silently discarded error looks to
 * the user like a form that refuses to submit for no reason.
 */
export function applyServerErrors(form: FormGroup, error: AppError): void {
  if (error.kind !== 'validation' || !error.fieldErrors) {
    return;
  }

  const unmatched: string[] = [];

  for (const [field, failures] of Object.entries(error.fieldErrors)) {
    const control = form.get(field);
    const codes = failures.map((failure) => failure.code);

    if (control) {
      control.setErrors({ ...(control.errors ?? {}), server: codes });
      control.markAsTouched();
    } else {
      unmatched.push(field);
    }
  }

  if (unmatched.length > 0) {
    form.setErrors({ ...(form.errors ?? {}), serverUnmatched: unmatched });
  }
}

/** Reads the server-supplied error codes from a control, for rendering a translated message. */
export function serverErrorCodes(form: FormGroup, field: string): string[] {
  const errors = form.get(field)?.errors;
  return Array.isArray(errors?.['server']) ? (errors['server'] as string[]) : [];
}
