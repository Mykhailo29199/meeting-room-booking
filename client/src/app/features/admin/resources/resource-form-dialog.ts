import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Observable } from 'rxjs';
import { toApiError } from '../../../core/api/api-error';
import { Resource } from '../../../core/api/models';
import { ResourcesApi } from '../../../core/api/resources-api';
import { VIEWER_TIME_ZONE } from '../../../core/time/viewer-time-zone';
import { showApiErrorOnForm } from '../../../shared/forms/server-errors';
import {
  closesAfterOpening,
  filterTimeZones,
  formValueOf,
  knownTimeZone,
  knownTimeZones,
  MAX_NAME_LENGTH,
  onQuarterHour,
  toResourceRequest,
} from './resource-form';

export interface ResourceFormData {
  /** The resource to edit, as last loaded; null to create one. */
  resource: Resource | null;
}

/** How the dialog ended; undefined if it was closed without saving. */
export type ResourceFormResult = { saved: Resource } | { gone: true };

/**
 * Creates a resource or edits one. An edit sends the `version` it was loaded
 * with: if another admin saved in between, the server answers 409 and the
 * dialog offers to reload the current version instead of overwriting theirs.
 */
@Component({
  selector: 'app-resource-form-dialog',
  imports: [
    MatAutocompleteModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    ReactiveFormsModule,
  ],
  templateUrl: './resource-form-dialog.html',
  styleUrl: './resource-form-dialog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResourceFormDialog {
  private readonly api = inject(ResourcesApi);
  private readonly dialogRef =
    inject<MatDialogRef<ResourceFormDialog, ResourceFormResult>>(MatDialogRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly zones = knownTimeZones();

  /** The version the edit is based on; replaced by Reload after a conflict. */
  private readonly resource = signal(inject<ResourceFormData>(MAT_DIALOG_DATA).resource);
  protected readonly isNew = this.resource() === null;
  protected readonly maxNameLength = MAX_NAME_LENGTH;

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      name: ['', [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)]],
      capacity: [null as number | null, [Validators.required, Validators.min(1)]],
      timeZoneId: ['', [Validators.required, knownTimeZone(this.zones)]],
      opensAt: ['', [Validators.required, onQuarterHour]],
      closesAt: ['', [Validators.required, onQuarterHour]],
    },
    { validators: closesAfterOpening },
  );

  private readonly typedZone = toSignal(this.form.controls.timeZoneId.valueChanges, {
    initialValue: '',
  });
  protected readonly zoneOptions = computed(() => filterTimeZones(this.zones, this.typedZone()));

  protected readonly saving = signal(false);
  /** A message for the whole form, e.g. a rule the server enforced. */
  protected readonly error = signal<string | null>(null);
  /** True after a 409: someone else saved this resource since it was loaded. */
  protected readonly conflict = signal(false);

  constructor() {
    this.form.setValue(formValueOf(this.resource(), inject(VIEWER_TIME_ZONE)));
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const request = toResourceRequest(this.form.getRawValue());
    const resource = this.resource();
    const saving: Observable<Resource> = resource
      ? this.api.update(resource.id, { ...request, version: resource.version })
      : this.api.create(request);

    this.saving.set(true);
    this.error.set(null);
    saving.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (saved) => this.dialogRef.close({ saved }),
      error: (error: unknown) => {
        this.saving.set(false);
        const problem = toApiError(error);
        if (problem.status === 409) {
          this.conflict.set(true);
        } else if (problem.status === 404) {
          this.dialogRef.close({ gone: true });
        } else {
          this.error.set(showApiErrorOnForm(this.form, problem));
        }
      },
    });
  }

  /** After a conflict: loads the current version; the user's edits are replaced. */
  protected reload(): void {
    const resource = this.resource();
    if (!resource) {
      return;
    }
    this.saving.set(true);
    this.api
      .get(resource.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (current) => {
          this.saving.set(false);
          this.conflict.set(false);
          this.resource.set(current);
          this.form.setValue(formValueOf(current, current.timeZoneId));
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const problem = toApiError(error);
          if (problem.status === 404) {
            this.dialogRef.close({ gone: true });
          } else {
            this.error.set(problem.message);
          }
        },
      });
  }
}
