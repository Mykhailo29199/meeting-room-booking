import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ApiError, toApiError } from '../../core/api/api-error';
import { Resource } from '../../core/api/models';
import { ResourcesApi } from '../../core/api/resources-api';
import { formatLocalTime, zoneLabel } from '../../core/time/resource-time';

/**
 * The resources the user can book, each linking to its schedule. Admins also
 * see removed resources, marked as such (the API sends them to admins only).
 */
@Component({
  selector: 'app-resource-list-page',
  imports: [MatButtonModule, MatIconModule, MatListModule, MatProgressBarModule, RouterLink],
  templateUrl: './resource-list-page.html',
  styleUrl: './resource-list-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResourceListPage {
  private readonly api = inject(ResourcesApi);
  private readonly destroyRef = inject(DestroyRef);

  private readonly resources = signal<Resource[] | null>(null);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly loading = computed(() => this.resources() === null && this.error() === null);

  protected readonly items = computed(() => {
    // Opening hours are local times; the zone's offset is today's.
    const now = new Date();
    return this.resources()?.map((resource) => ({
      id: resource.id,
      name: resource.name,
      isActive: resource.isActive,
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
    this.resources.set(null);
    this.error.set(null);
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (resources) => this.resources.set(resources),
        error: (error: unknown) => this.error.set(toApiError(error)),
      });
  }
}
