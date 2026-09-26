import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { filter, switchMap, tap } from 'rxjs';
import { ApiError, toApiError } from '../../../core/api/api-error';
import { Resource, ResourceRemovalResult } from '../../../core/api/models';
import { ResourcesApi } from '../../../core/api/resources-api';
import { Confirmer } from '../../../core/notify/confirm-dialog';
import { Notifier } from '../../../core/notify/notifier';
import { formatLocalTime, zoneLabel } from '../../../core/time/resource-time';
import { ResourceFormData, ResourceFormDialog, ResourceFormResult } from './resource-form-dialog';

/**
 * Admins add, edit, remove and restore resources. Removing never deletes:
 * the server deactivates the resource and releases its future slots, and
 * says how many bookings that affected.
 */
@Component({
  selector: 'app-admin-resources-page',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, RouterLink],
  templateUrl: './admin-resources-page.html',
  styleUrl: './admin-resources-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminResourcesPage {
  private readonly api = inject(ResourcesApi);
  private readonly dialog = inject(MatDialog);
  private readonly confirmer = inject(Confirmer);
  private readonly notifier = inject(Notifier);
  private readonly destroyRef = inject(DestroyRef);

  private readonly resources = signal<Resource[] | null>(null);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly loading = signal(false);
  /** The resource being removed or restored, whose buttons are disabled meanwhile. */
  protected readonly busy = signal<string | null>(null);

  protected readonly items = computed(() => {
    const now = new Date();
    return this.resources()?.map((resource) => ({
      resource,
      capacity: resource.capacity === 1 ? '1 person' : `Up to ${resource.capacity} people`,
      hours:
        `${formatLocalTime(resource.opensAt)}–${formatLocalTime(resource.closesAt)}, ` +
        zoneLabel(resource.timeZoneId, now),
    }));
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (resources) => {
          this.loading.set(false);
          this.resources.set(resources);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.resources.set(null);
          this.error.set(toApiError(error));
        },
      });
  }

  protected add(): void {
    this.openForm(null);
  }

  protected edit(resource: Resource): void {
    this.openForm(resource);
  }

  protected remove(resource: Resource): void {
    this.confirmer
      .confirm({
        title: `Remove ${resource.name}?`,
        message:
          'Users will no longer see or book it. Its future bookings are cancelled and bookings ' +
          'under way end now; past bookings stay. You can restore it later, but cancelled ' +
          'bookings do not come back.',
        confirm: 'Remove',
        dismiss: 'Keep it',
      })
      .pipe(
        filter((confirmed) => confirmed),
        tap(() => this.busy.set(resource.id)),
        switchMap(() => this.api.remove(resource.id)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => this.done(`${resource.name} removed. ${removalSummary(result)}`),
        error: (error: unknown) => this.failed(error),
      });
  }

  protected restore(resource: Resource): void {
    this.busy.set(resource.id);
    this.api
      .restore(resource.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.done(`${resource.name} restored. Users can book it again.`),
        error: (error: unknown) => this.failed(error),
      });
  }

  private openForm(resource: Resource | null): void {
    this.dialog
      .open<ResourceFormDialog, ResourceFormData, ResourceFormResult>(ResourceFormDialog, {
        data: { resource },
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        if (!result) {
          return;
        }
        if ('saved' in result) {
          this.notifier.show(
            resource ? `${result.saved.name} saved.` : `${result.saved.name} added.`,
          );
        } else {
          this.notifier.show('This resource no longer exists.');
        }
        this.load();
      });
  }

  private done(message: string): void {
    this.busy.set(null);
    this.notifier.show(message);
    this.load();
  }

  private failed(error: unknown): void {
    this.busy.set(null);
    const problem = toApiError(error);
    this.notifier.show(problem.message);
    if (!problem.isUnexpected) {
      // E.g. already removed by another admin: show the current state.
      this.load();
    }
  }
}

/** E.g. `2 future bookings cancelled, 1 booking under way ended.` */
export function removalSummary(result: ResourceRemovalResult): string {
  const parts = [
    result.cancelledBookings > 0
      ? `${count(result.cancelledBookings, 'future booking')} cancelled`
      : null,
    result.shortenedBookings > 0
      ? `${count(result.shortenedBookings, 'booking under way', 'bookings under way')} ended`
      : null,
  ].filter((part) => part !== null);
  return parts.length > 0 ? `${capitalize(parts.join(', '))}.` : 'It had no future bookings.';
}

function count(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}
