# client/CLAUDE.md

Guidance for Claude Code when working in `client/`, the Angular frontend of
the Meeting Room Booking System. The repository-wide rules in `../CLAUDE.md`
still apply (small verified steps, one logical change per commit, the
developer commits, secrets never committed). Keep this file accurate: it
describes what exists *now*.

## Stack

- Angular 22, standalone components only (no NgModules), signals for state,
  `inject()` for dependencies, SCSS, strict TypeScript.
- Angular Material for all UI controls (forms, lists, dialogs, snackbars,
  date picker). No other UI library.
- Vitest (Angular CLI's unit-test builder) for tests.
- `@microsoft/signalr` for real-time updates.
- Node.js 24.15+ (or 22.22.3+).

## The backend this client talks to

The backend (ASP.NET Core, `../src`) is complete; see `../README.md` for the
full API overview and `../CLAUDE.md` for its design. What the client needs:

| Endpoint | Who | Notes |
|---|---|---|
| `POST /api/auth/register`, `POST /api/auth/login` | anyone | return `AuthResult` with `accessToken` (JWT, 60 min, no refresh) |
| `GET /api/auth/me` | signed in | whose token this is |
| `GET /api/resources` | signed in | users get bookable resources; admins also removed ones (`isActive: false`) |
| `GET /api/resources/{id}` | signed in | removed resource → 404 for users |
| `GET /api/resources/{id}/schedule?date=YYYY-MM-DD` | signed in | the day's 15-minute slots; no `date` = today in the resource's zone |
| `POST /api/bookings` | signed in | `{ resourceId, startUtc, endUtc }` → 201; slot taken → 409; rule broken → 400 |
| `DELETE /api/bookings/{id}` | owner or admin | cancels, or ends early if under way → `CancellationResult` |
| `GET /api/bookings/mine?includePast=` | signed in | own bookings |
| `GET /api/bookings?resourceId=&includePast=` | admin | everyone's bookings, with user name and email |
| `POST /api/resources`, `PUT /api/resources/{id}` | admin | `PUT` must carry the loaded `version`; stale → 409 |
| `DELETE /api/resources/{id}`, `POST /api/resources/{id}/restore` | admin | remove (cancels future bookings, returns counts) / restore |
| hub `/hubs/schedule` | signed in | invoke `WatchResource(id)` / `StopWatchingResource(id)`; event `SlotsChanged` `{ resourceId, slots: [{ startUtc, endUtc, isBooked }] }` |

Local run: the API on `http://localhost:5077` (`dotnet run --project
../src/MeetingRoomBooking.Api` or F5 in Visual Studio), Swagger at
`http://localhost:5077/swagger`. The dev admin account is configured in the
developer's git-ignored `appsettings.Development.json` (`Seed:*`).

## Talking to the API

- In development the API runs on `http://localhost:5077` (backend launch
  profile `http`). `ng serve` proxies `/api` and `/hubs` there
  (`proxy.conf.json`, WebSockets enabled), so the browser sees one origin:
  no CORS locally. Request URLs are always relative to the configured API
  base: the `API_BASE_URL` token (`core/api/api-base-url.ts`), which reads
  `environment.apiBaseUrl` (empty in development; `environment.ts` is the
  production file, swapped by `fileReplacements`).
- Components never use `HttpClient` directly; they call `AuthApi`,
  `ResourcesApi` or `BookingsApi` (`core/api/`), one method per endpoint.
- `core/api/models.ts` mirrors the backend DTOs (JSON is camelCase). When a
  backend DTO changes, change the model in the same commit. UTC times stay
  the strings the API sent (`UtcDateTime`), never parsed and re-serialised.
- The API returns RFC 9457 problem details. Turn every failed call into an
  `ApiError` with `toApiError` (`core/api/api-error.ts`) and show its
  `message` — the server's `detail`, written for the user, or a generic
  "try again" for network failures and 5xx (`isUnexpected`), whose bodies
  are never shown. Validation errors (`errors: { Field: [messages] }`)
  arrive in `fieldErrors` under camelCase names and belong on the form
  fields of the same name.

## Time — the rule that is easiest to break

- Every time the API sends is UTC; every resource has an IANA `timeZoneId`.
- **Display** times in the *resource's* time zone, labelled
  (e.g. "Berlin time (UTC+2)"); if the viewer's zone differs, add their own
  time as a hint ("10:00 (11:00 your time)"). Use the shared time helpers,
  never `new Date().getHours()` or the browser's zone for resource times.
- **Never convert user input into UTC.** To book, send back exactly the
  `startUtc` of the first chosen slot and the `endUtc` of the last one, as
  received from the schedule. The server rejects non-UTC times with 400.

## Authentication and authorization

- `AuthService` keeps the signed-in user and access token (from
  `POST /api/auth/login|register`) in `localStorage`, exposes them as
  signals, and signs out when the token expires (`expiresAtUtc`).
  Trade-off, documented on purpose: `localStorage` is readable by injected
  scripts; an httpOnly-cookie session would need backend changes. Never
  render untrusted HTML (`innerHTML`, bypassing sanitisation).
- The HTTP interceptor adds `Authorization: Bearer <token>` to API requests
  only (never to third-party URLs). A 401 signs the user out and sends them
  to `/login?returnUrl=…`.
- Guards (`authGuard`, `adminGuard`, `guestGuard`) only shape navigation.
  They are not security: the API authorizes every request. Never hide a
  check in the UI that the server does not also make.

## Real-time schedule updates

- One shared hub connection (`ScheduleHubService`) to `/hubs/schedule`,
  token via `accessTokenFactory` (sent as `?access_token=`), automatic
  reconnect.
- A schedule page calls `watch(resourceId)` on open and `unwatch` on leave,
  and applies `SlotsChanged` events to its slots.
- Group membership is lost when the connection drops, and events sent
  meanwhile are missed. On `onreconnected` the service re-joins every
  watched resource and the page **reloads its schedule**; after the
  connection closes for good it is restarted and the same happens.
- Events never say who booked. After the user's own booking or
  cancellation, reload the schedule to get `isMine` / `bookingId`.

## Error handling in the UI

| Status | Where | What the user sees |
|---|---|---|
| 409 on booking | schedule | server's message in a snackbar; the schedule reloads; the selection is cleared |
| 409 on resource edit | admin | "changed by someone else" dialog with Reload |
| 400 | forms / booking | field errors from `errors`, otherwise the `detail` |
| 401 | anywhere | signed out, redirected to login |
| 403 | anywhere | "not allowed" snackbar |
| 404 | schedule / admin | "no longer exists", back to the list |
| network / 5xx | anywhere | generic "something went wrong, try again" |

A slot booked by someone else while it is selected (`SlotsChanged`) clears
the selection with an explanation — before the user presses Book.

## Structure

```
src/app/
  core/       singletons: api/ (models, HTTP clients, problem details),
              auth/ (service, interceptor, guards), realtime/ (hub service),
              time/ (resource-time formatting), notify/ (snackbars, dialogs)
  features/   one folder per area, routed lazily with loadComponent:
              auth/ (login, register), resources/ (list),
              schedule/ (day view, slot list, booking panel),
              bookings/ (mine), admin/ (resources, all bookings)
  shared/     layout (app shell/toolbar) and small reusable pieces
```

- Components: `ChangeDetectionStrategy.OnPush`, signal inputs/outputs, no
  logic in templates beyond display. Put rules (slot selection, time
  formatting, problem-details parsing) in plain functions or services so
  they can be unit-tested without a DOM.
- Routes are lazy (`loadComponent`), guarded, and bookmarkable: the schedule
  keeps the date in the URL (`/resources/:id?date=YYYY-MM-DD`).

## Implementation plan — one commit per step

Build the client in these steps, each a working, tested state. Update the
"Status" line below as steps land.

1. **Set up the client: rules, dev proxy, API clients** — this file;
   `proxy.conf.json` wired into `angular.json` (`serve.options.proxyConfig`);
   `"analytics": false` in `angular.json` so nobody is prompted and no
   personal analytics id is committed; `src/environments/` with
   `apiBaseUrl`; `core/api/models.ts`; HTTP clients for auth, resources and
   bookings; a problem-details parser (status, `detail`, field `errors`,
   network failure) with tests.
2. **Add sign-in, app shell and route guards** — `AuthService` (token +
   user in `localStorage`, signals, sign-out at expiry), the interceptor,
   login and register pages (server field errors on the fields), the shell
   with a toolbar (admin links for admins only, user name, sign out), lazy
   routes, `authGuard` / `adminGuard` / `guestGuard`, with tests.
3. **Show resources and their day schedule** — `/resources` list;
   `/resources/:id?date=` with previous/next day and a date picker, the
   vertical list of 15-minute slots (free / booked / yours / past, not by
   colour alone), times in the resource's zone with a zone label and the
   viewer's-time hint; time helpers with tests.
4. **Book time ranges from the schedule** — Start and End selects above the
   slot list (End offers only slots reachable through free slots), clicking
   slots fills them, a summary ("10:00–11:00, 1 h") and Book; send the
   schedule's own UTC values; 409 → message, reload, clear selection; 400 →
   message; reload after success; selection rules as tested pure functions.
5. **Update schedules live over SignalR** — `@microsoft/signalr`,
   `ScheduleHubService` (one connection, token factory, automatic reconnect,
   watch/unwatch), apply `SlotsChanged`, clear a selection that just got
   booked; on reconnect re-join watched resources and reload the schedule;
   tests for the reconnect logic.
6. **Add my bookings and admin pages** — `/my-bookings` (cancel / end
   early), `/admin/resources` (create/edit dialog with the version and the
   409 "changed by someone else — reload" dialog, remove with confirmation
   and the returned counts, restore), `/admin/bookings` (filter by resource,
   include past, cancel any); loading, empty and error states.

**Status:** steps 1–2 are done — dev proxy, environments, `core/api/`
(models, HTTP clients, `toApiError`); `core/auth/` (`AuthService`, the
interceptor, the guards, `safeReturnUrl`), the login and register pages
(`shared/forms/showApiErrorOnForm` puts server field errors on the fields),
and the shell with its toolbar (`shared/layout/`), all with tests.
`/resources` is a placeholder page until step 3. The toolbar gets its admin
links in step 6, with the pages they lead to; `isAdmin` and `adminGuard`
exist already. Next is step 3.

## Quality bar

- `npx ng build` without warnings and `npx ng test --watch=false` green
  before every commit; new logic comes with tests, and a new test is proven
  able to fail (break the code, see it red, restore).
- Use Material's accessible components; every interactive element reachable
  by keyboard and labelled. Slot state is not conveyed by colour alone.

## Commands (inside client/)

```bash
npm ci                       # install exactly what package-lock.json pins
npm start                    # ng serve on http://localhost:4200, with the API proxy
npx ng build
npx ng test --watch=false
```
Run the API as well (`dotnet run --project ../src/MeetingRoomBooking.Api`
or F5 in Visual Studio) for the client to have a backend.
