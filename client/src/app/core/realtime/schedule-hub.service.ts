import { effect, inject, Injectable, InjectionToken, OnDestroy } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { filter, map, Observable, Subject } from 'rxjs';
import { API_BASE_URL } from '../api/api-base-url';
import { Guid, SlotChange, SlotsChangedMessage } from '../api/models';
import { AuthService } from '../auth/auth.service';

/** The hub's path and the names the server uses (see the API's `ScheduleHub`). */
export const SCHEDULE_HUB_PATH = '/hubs/schedule';
const SLOTS_CHANGED = 'SlotsChanged';
const WATCH = 'WatchResource';
const STOP_WATCHING = 'StopWatchingResource';

/** Waits before restarting a connection that closed for good, then the last one repeats. */
export const RESTART_DELAYS_MS = [2_000, 5_000, 10_000, 30_000];

/**
 * Builds the hub connection; a token so tests can use a fake. The token
 * factory is asked on every (re)connect, so a new sign-in's token is used.
 */
export const HUB_CONNECTION_FACTORY = new InjectionToken<
  (accessTokenFactory: () => string) => HubConnection
>('HUB_CONNECTION_FACTORY', {
  providedIn: 'root',
  factory: () => {
    const url = `${inject(API_BASE_URL)}${SCHEDULE_HUB_PATH}`;
    return (accessTokenFactory) =>
      new HubConnectionBuilder()
        // Browsers cannot set headers on WebSockets, so the client sends the
        // token as ?access_token=, which the API accepts on the hub path only.
        .withUrl(url, { accessTokenFactory })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();
  },
});

/**
 * One connection to the schedule hub, shared by every page. A page calls
 * {@link watch} for the resource it shows and {@link unwatch} when it leaves,
 * then applies {@link slotsChanged}.
 *
 * Events sent while the connection is down are lost, and the server forgets
 * group membership when a connection drops. So after every (re)connection
 * the service joins each watched resource again and {@link joined} fires for
 * it: the page then reloads its schedule to catch up. The same happens on the
 * first join, which closes the gap between loading a schedule and joining.
 */
@Injectable({ providedIn: 'root' })
export class ScheduleHubService implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly createConnection = inject(HUB_CONNECTION_FACTORY);

  /** How many pages watch each resource. */
  private readonly watched = new Map<Guid, number>();
  private readonly changes = new Subject<SlotsChangedMessage>();
  private readonly joins = new Subject<Guid>();

  private connection: HubConnection | null = null;
  /**
   * True from our own handling of a (re)connect until the connection drops.
   * Not SignalR's state, which turns Connected just before that handling
   * runs: a watch in between would join twice.
   */
  private ready = false;
  private restartTimer: ReturnType<typeof setTimeout> | undefined;
  private failedRestarts = 0;

  constructor() {
    // A signed-out user must not keep a connection authorized by the old token.
    effect(() => {
      if (!this.auth.isSignedIn()) {
        this.disconnect();
      }
    });
  }

  /** Changes to this resource's slots. Never says who booked. */
  slotsChanged(resourceId: Guid): Observable<SlotChange[]> {
    return this.changes.pipe(
      filter((message) => message.resourceId === resourceId),
      map((message) => message.slots),
    );
  }

  /** Fires whenever the connection has (re)joined this resource: reload to catch up. */
  joined(resourceId: Guid): Observable<void> {
    return this.joins.pipe(
      filter((id) => id === resourceId),
      map(() => undefined),
    );
  }

  watch(resourceId: Guid): void {
    const count = this.watched.get(resourceId) ?? 0;
    this.watched.set(resourceId, count + 1);
    if (count > 0) {
      return;
    }
    if (this.connection && this.ready) {
      void this.join(this.connection, resourceId);
    } else {
      // Joined once the connection is up.
      this.connect();
    }
  }

  unwatch(resourceId: Guid): void {
    const count = this.watched.get(resourceId) ?? 0;
    if (count > 1) {
      this.watched.set(resourceId, count - 1);
      return;
    }
    this.watched.delete(resourceId);
    if (this.connection && this.ready) {
      // Best effort: the server forgets the group when the connection ends anyway.
      this.connection.invoke(STOP_WATCHING, resourceId).catch(() => undefined);
    }
  }

  ngOnDestroy(): void {
    this.disconnect();
  }

  private connect(): void {
    if (this.connection || !this.auth.isSignedIn()) {
      return;
    }
    const connection = this.createConnection(() => this.auth.accessToken() ?? '');
    this.connection = connection;
    connection.on(SLOTS_CHANGED, (message: SlotsChangedMessage) => this.changes.next(message));
    connection.onreconnecting(() => (this.ready = false));
    // Automatic reconnect succeeded: a new connection id with no groups.
    connection.onreconnected(() => this.joinAll(connection));
    // Automatic reconnect gave up, or the server closed the connection.
    connection.onclose(() => {
      if (this.connection === connection) {
        this.ready = false;
        this.scheduleRestart(connection);
      }
    });
    this.start(connection);
  }

  private start(connection: HubConnection): void {
    connection
      .start()
      .then(() => {
        this.failedRestarts = 0;
        this.joinAll(connection);
      })
      .catch(() => {
        // Server unreachable: try again later, as long as this is still the connection.
        if (this.connection === connection) {
          this.scheduleRestart(connection);
        }
      });
  }

  private scheduleRestart(connection: HubConnection): void {
    clearTimeout(this.restartTimer);
    if (this.watched.size === 0 || !this.auth.isSignedIn()) {
      // Nobody needs it: connect again on the next watch.
      this.connection = null;
      return;
    }
    const delay = RESTART_DELAYS_MS[Math.min(this.failedRestarts, RESTART_DELAYS_MS.length - 1)];
    this.failedRestarts++;
    this.restartTimer = setTimeout(() => {
      if (this.connection === connection) {
        this.start(connection);
      }
    }, delay);
  }

  private joinAll(connection: HubConnection): void {
    this.ready = true;
    for (const resourceId of this.watched.keys()) {
      void this.join(connection, resourceId);
    }
  }

  private async join(connection: HubConnection, resourceId: Guid): Promise<void> {
    try {
      await connection.invoke(WATCH, resourceId);
    } catch {
      // The connection dropped meanwhile; the next reconnection joins again.
      return;
    }
    if (this.connection === connection && this.watched.has(resourceId)) {
      this.joins.next(resourceId);
    }
  }

  private disconnect(): void {
    clearTimeout(this.restartTimer);
    const connection = this.connection;
    this.connection = null;
    this.ready = false;
    this.failedRestarts = 0;
    void connection?.stop();
  }
}
