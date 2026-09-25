import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { Resource } from '../../core/api/models';
import { ResourcesApi } from '../../core/api/resources-api';
import { ResourceListPage } from './resource-list-page';

function resource(overrides: Partial<Resource>): Resource {
  return {
    id: 'r1',
    name: 'Room A',
    capacity: 8,
    timeZoneId: 'Europe/Berlin',
    opensAt: '08:00:00',
    closesAt: '18:00:00',
    isActive: true,
    version: 'v1',
    ...overrides,
  };
}

describe('ResourceListPage', () => {
  let answers: Observable<Resource[]>[];
  let list: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    answers = [];
    list = vi.fn(() => answers.shift() ?? of([]));
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: ResourcesApi, useValue: { list } }],
    });
  });

  async function render(): Promise<ComponentFixture<ResourceListPage>> {
    const fixture = TestBed.createComponent(ResourceListPage);
    await fixture.whenStable();
    return fixture;
  }

  function links(fixture: ComponentFixture<unknown>): HTMLAnchorElement[] {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        'a[mat-list-item]',
      ),
    ];
  }

  it('links every resource to its schedule, with capacity and local opening hours', async () => {
    answers.push(
      of([
        resource({ id: 'r1', name: 'Room A' }),
        resource({ id: 'r2', name: 'Phone booth', capacity: 1, timeZoneId: 'America/New_York' }),
      ]),
    );

    const [roomA, booth] = links(await render());

    expect(roomA.getAttribute('href')).toBe('/resources/r1');
    expect(roomA.textContent).toContain('Room A');
    expect(roomA.textContent).toContain('Up to 8 people');
    expect(roomA.textContent).toContain('08:00–18:00, Berlin time (UTC+');
    expect(booth.getAttribute('href')).toBe('/resources/r2');
    expect(booth.textContent).toContain('1 person');
    expect(booth.textContent).toContain('New York time (UTC-');
  });

  it('marks removed resources, which only admins receive', async () => {
    answers.push(of([resource({ name: 'Old room', isActive: false })]));

    const [oldRoom] = links(await render());

    expect(oldRoom.textContent).toContain('Old room');
    expect(oldRoom.textContent).toContain('(removed)');
  });

  it('says when there is nothing to book', async () => {
    answers.push(of([]));

    const fixture = await render();

    expect(links(fixture)).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('There are no resources to book yet.');
  });

  it('offers to try again after a failure', async () => {
    answers.push(
      throwError(() => new HttpErrorResponse({ status: 0 })),
      of([resource({})]),
    );
    const fixture = await render();
    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Something went wrong. Please try again.');

    (alert.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(list).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(links(fixture)).toHaveLength(1);
  });
});
