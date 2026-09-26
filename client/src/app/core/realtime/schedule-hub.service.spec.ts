import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { SlotsChangedMessage } from '../api/models';
import { AuthService } from '../auth/auth.service';
import {
  HUB_CONNECTION_FACTORY,
  RESTART_DELAYS_MS,
  ScheduleHubService,
} from './schedule-hub.service';

/** Stands in for SignalR's HubConnection; the test drives its events. */
class FakeConnection {
  state = HubConnectionState.Disconnected;
  failStarts = 0;
  private readonly handlers = new Map<string, (message: SlotsChangedMessage) => void>();
  private reconnectingCallback: () => void = () => undefined;
  private reconnectedCallback: () => void = () => undefined;
  private closeCallback: () => void = () => undefined;

  readonly start = vi.fn(async () => {
    if (this.failStarts > 0) {
      this.failStarts--;
      throw new Error('Server unreachable');
    }
    this.state = HubConnectionState.Connected;
  });
  readonly stop = vi.fn(async () => {
    this.state = HubConnectionState.Disconnected;
  });
  readonly invoke = vi.fn(async (_method: string, _resourceId: string) => undefined);

  on(name: string, handler: (message: SlotsChangedMessage) => void): void {
    this.handlers.set(name, handler);
  }
  onreconnecting(callback: () => void): void {
    this.reconnectingCallback = callback;
  }
  onreconnected(callback: () => void): void {
    this.reconnectedCallback = callback;
  }
  onclose(callback: () => void): void {
    this.closeCallback = callback;
  }

  send(message: SlotsChangedMessage): void {
    this.handlers.get('SlotsChanged')?.(message);
  }
  /** The connection drops and SignalR reconnects on its own. */
  reconnect(): void {
    this.state = HubConnectionState.Reconnecting;
    this.reconnectingCallback();
    this.state = HubConnectionState.Connected;
    this.reconnectedCallback();
  }
  close(): void {
    this.state = HubConnectionState.Disconnected;
    this.closeCallback();
  }
  /** Resource ids passed to a hub method, in order. */
  calls(method: string): string[] {
    return this.invoke.mock.calls.filter(([m]) => m === method).map(([, id]) => id);
  }
}

describe('ScheduleHubService', () => {
  let connections: FakeConnection[];
  let tokenFactory: () => string;
  let failNextStarts: number;
  const signedIn = signal(true);
  let token: string;

  beforeEach(() => {
    vi.useFakeTimers();
    connections = [];
    failNextStarts = 0;
    signedIn.set(true);
    token = 'token-1';
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { isSignedIn: signedIn, accessToken: () => token } },
        {
          provide: HUB_CONNECTION_FACTORY,
          useValue: (accessTokenFactory: () => string) => {
            tokenFactory = accessTokenFactory;
            const connection = new FakeConnection();
            connection.failStarts = failNextStarts;
            connections.push(connection);
            return connection as unknown as HubConnection;
          },
        },
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /** Lets pending promises (start, invoke) settle. */
  async function settle(): Promise<void> {
    await vi.advanceTimersByTimeAsync(0);
  }

  function service(): ScheduleHubService {
    const hub = TestBed.inject(ScheduleHubService);
    TestBed.tick();
    return hub;
  }

  function record<T>(source: { subscribe(next: (value: T) => void): unknown }): T[] {
    const values: T[] = [];
    source.subscribe((value) => values.push(value));
    return values;
  }

  it('connects on the first watch, joins the resource and reports it', async () => {
    const hub = service();
    const joined = record(hub.joined('r1'));

    hub.watch('r1');
    await settle();

    expect(connections).toHaveLength(1);
    expect(connections[0].calls('WatchResource')).toEqual(['r1']);
    expect(joined).toHaveLength(1);
  });

  it('asks for the current token on every connect', () => {
    const hub = service();
    hub.watch('r1');

    expect(tokenFactory()).toBe('token-1');
    token = 'token-2';
    expect(tokenFactory()).toBe('token-2');
  });

  it('shares one connection and one join between pages of the same resource', async () => {
    const hub = service();
    hub.watch('r1');
    await settle();

    hub.watch('r1');
    hub.watch('r2');
    await settle();

    expect(connections).toHaveLength(1);
    expect(connections[0].calls('WatchResource')).toEqual(['r1', 'r2']);
  });

  it('stops watching only when the last page leaves', async () => {
    const hub = service();
    hub.watch('r1');
    hub.watch('r1');
    await settle();

    hub.unwatch('r1');
    expect(connections[0].calls('StopWatchingResource')).toEqual([]);
    hub.unwatch('r1');
    expect(connections[0].calls('StopWatchingResource')).toEqual(['r1']);
  });

  it("passes on each resource's changes to its watchers only", async () => {
    const hub = service();
    const r1 = record(hub.slotsChanged('r1'));
    hub.watch('r1');
    await settle();
    const change = {
      startUtc: '2026-10-01T08:00:00Z',
      endUtc: '2026-10-01T08:15:00Z',
      isBooked: true,
    };

    connections[0].send({ resourceId: 'r2', slots: [change] });
    connections[0].send({ resourceId: 'r1', slots: [change] });

    expect(r1).toEqual([[change]]);
  });

  it('joins every watched resource again after an automatic reconnect', async () => {
    const hub = service();
    hub.watch('r1');
    hub.watch('r2');
    await settle();
    const joined = record(hub.joined('r1'));

    connections[0].reconnect();
    await settle();

    expect(connections[0].calls('WatchResource')).toEqual(['r1', 'r2', 'r1', 'r2']);
    expect(joined).toHaveLength(1);
  });

  it('restarts a connection that closed for good, then joins again', async () => {
    const hub = service();
    hub.watch('r1');
    await settle();
    const joined = record(hub.joined('r1'));

    connections[0].close();
    await vi.advanceTimersByTimeAsync(RESTART_DELAYS_MS[0] - 1);
    expect(connections[0].start).toHaveBeenCalledOnce();
    await vi.advanceTimersByTimeAsync(1);

    expect(connections[0].start).toHaveBeenCalledTimes(2);
    expect(connections[0].calls('WatchResource')).toEqual(['r1', 'r1']);
    expect(joined).toHaveLength(1);
  });

  it('keeps retrying an unreachable server, waiting longer each time', async () => {
    failNextStarts = 3;
    const hub = service();
    const joined = record(hub.joined('r1'));

    hub.watch('r1');
    await settle();
    await vi.advanceTimersByTimeAsync(RESTART_DELAYS_MS[0]);
    await vi.advanceTimersByTimeAsync(RESTART_DELAYS_MS[1] - 1);
    expect(connections[0].start).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    await vi.advanceTimersByTimeAsync(RESTART_DELAYS_MS[2]);

    expect(connections[0].start).toHaveBeenCalledTimes(4);
    expect(joined).toHaveLength(1);
  });

  it('does not restart when nobody watches, and connects again on the next watch', async () => {
    const hub = service();
    hub.watch('r1');
    await settle();
    hub.unwatch('r1');

    connections[0].close();
    await vi.advanceTimersByTimeAsync(RESTART_DELAYS_MS.at(-1)!);
    expect(connections[0].start).toHaveBeenCalledOnce();

    hub.watch('r1');
    await settle();
    expect(connections).toHaveLength(2);
    expect(connections[1].calls('WatchResource')).toEqual(['r1']);
  });

  it('disconnects on sign-out and does not come back', async () => {
    const hub = service();
    hub.watch('r1');
    await settle();

    signedIn.set(false);
    TestBed.tick();
    connections[0].close();
    await vi.advanceTimersByTimeAsync(RESTART_DELAYS_MS.at(-1)!);

    expect(connections[0].stop).toHaveBeenCalledOnce();
    expect(connections[0].start).toHaveBeenCalledOnce();
  });

  it('does not connect while signed out', async () => {
    signedIn.set(false);
    const hub = service();

    hub.watch('r1');
    await settle();

    expect(connections).toEqual([]);
  });
});
