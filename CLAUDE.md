# CLAUDE.md

Guidance for Claude Code in this repository. Keep it accurate: describe what
exists *now*, and move items out of "Not built yet" as they land.

## Project

Meeting Room Booking System (test task). Users book 15-minute slots of
resources (e.g. meeting rooms); the system must never double-book a slot,
and everyone viewing a resource's schedule must see changes in real time.

Naming follows the task brief: the bookable thing is a **Resource**
everywhere in code and API (`Resource`, `ResourceId`, `/api/resources`), so
each requirement maps to the code without translation. "Meeting room" is
only an example of a resource.

Hard requirements that shape the code:

- Simultaneous requests for the same slot: exactly one succeeds, the rest get
  a clear conflict response — never a silent overwrite, never a 500.
- Concurrency control must be an explicit, documented mechanism — not
  incidental ORM behaviour, and not "check if free, then insert".
- An automated test fires parallel booking requests at one slot and asserts
  exactly one booking is created.
- Roles: `User` views resources and schedules and books; `Admin` also
  creates, edits and removes resources and sees all bookings.
- Real-time updates via Azure SignalR Service; deployed to Azure (Web Apps,
  Azure SQL).
- Atomic commits, well-documented code suitable for review.

## Stack

- Backend: ASP.NET Core on .NET 10 (LTS; chosen over .NET 8, whose support
  ends in November 2026). Solution file: `MeetingRoomBooking.slnx`.
- Database: SQL Server (Azure SQL in production).
- Real-time: SignalR hub `/hubs/schedule`; Azure SignalR Service when
  `Azure:SignalR:ConnectionString` is set, in-process SignalR otherwise.
- Frontend: Angular 22 + Angular Material in `client/` (standalone
  components, SCSS, Vitest). Needs Node.js 22.22.3+ or 24.15+. UI language:
  English (matches the API's messages).

## Architecture — 4 layers, dependencies point inward only

```
Api  ──┐
       ├──> Application ──> Domain
Infrastructure ──┘        (implements Application's interfaces)
```

- `src/MeetingRoomBooking.Domain` — entities and business rules. No project
  or framework references (no EF Core, no ASP.NET Core).
  - `Resources/Resource` — name, capacity, `IsActive`, IANA `TimeZoneId`,
    opening hours (`OpensAt`–`ClosesAt`, local time); the hours define the
    resource's fixed set of 15-minute slots (`GetSlotStarts`, returns UTC).
  - `Bookings/Booking` — created only via `Booking.Create`, which enforces
    every rule (active resource, UTC inputs, end after start, 15-minute
    grid, not in the past, within local opening hours on one day) and splits
    the booking into `BookingSlot`s in ascending order. A booking is one or
    more consecutive 15-minute slots. It deliberately does not check whether
    slots are free.
  - `TimeSlots` — the 15-minute grid; `DomainException` — a rule violation
    with a user-facing message (maps to HTTP 400).
  - Entities have private setters and a private parameterless constructor
    for EF Core; IDs are `Guid.CreateVersion7()`.
- `src/MeetingRoomBooking.Application` — use cases (services), DTOs, and the
  interfaces Infrastructure implements. References Domain only — in
  particular, no EF Core.
  - `Persistence/` — `IUnitOfWork` (single commit point), `IResourceRepository`,
    `IBookingRepository`, and the two exceptions a commit can raise:
    `UniqueConstraintViolationException` (slot taken → 409) and
    `ConcurrencyConflictException` (stale edit → 409). `ITransaction` /
    `IUnitOfWork.BeginTransactionAsync` for use cases that must hold a lock
    between reading and writing; `IResourceRepository.LockAsync` is that lock.
  - `Bookings/BookingService` — create, cancel, day schedule. Services are
    concrete classes registered in `Program.cs`; they get the caller as a
    `Common/UserContext(UserId, IsAdmin)` parameter and "now" from an
    injected `TimeProvider` (tests use `FixedTimeProvider`).
  - `Resources/ResourceService` — list, get, create, update (with the
    client's `Version`), remove, restore. Admin-only actions are restricted
    by the controller (`[Authorize(Roles = Roles.Admin)]`), not re-checked
    in the service.
  - `Bookings/BookingListService` + `IBookingQueries` — "my bookings" and
    the admin's all-bookings list (≤ 500, earliest first, upcoming unless
    `includePast`). A read-only query, not a repository: it joins bookings
    with resources and Identity users, which only Infrastructure knows
    (`Infrastructure/Persistence/BookingQueries`, left join because
    `UserId` has no FK).
  - Booking input times must arrive as UTC (`...Z`); anything else is a
    `ValidationException` (400) raised in `BookingService`, never passed to
    the domain (whose UTC guard is a programming-error check → 500).
  - `Common/Exceptions.cs` — use-case outcomes the API maps to HTTP:
    `NotFoundException` 404, `ForbiddenException` 403, `ConflictException`
    409. Domain rule violations stay `DomainException` → 400.
- `src/MeetingRoomBooking.Infrastructure` — EF Core (Identity and SignalR
  planned).
  - `Persistence/AppDbContext` plus one `IEntityTypeConfiguration` per entity
    in `Persistence/Configurations/`. `BookingSlotConfiguration` holds the
    concurrency-critical primary key.
  - `Persistence/UnitOfWork` — the only caller of `SaveChangesAsync`;
    translates `DbUpdateConcurrencyException` and unique violations, lets
    everything else propagate.
  - `IUniqueConstraintViolationDetector` — recognises the provider's
    unique-violation error. Production: SQL Server 2627/2601. Tests register
    the SQLite version (`tests/.../Infrastructure/SqliteTestDatabase.cs`); no
    production class is subclassed for tests.
  - `Identity/` — `ApplicationUser`, `AuthService` (implements
    Application's `IAuthService`), `JwtTokenService` + `JwtOptions` (the
    `Jwt` config section, validated on start), `IdentitySeeder`.
  - `DependencyInjection.AddInfrastructure` — registers all of the above;
    needs `ConnectionStrings:Default` and the `Jwt` section.
  - `Persistence/DatabaseMigrator` — applies pending migrations (creating
    the database if needed). `Program` runs it before seeding only when
    `Database:MigrateOnStartup` is true: in Azure, where the free plan runs a
    single instance, so no two instances migrate at once. Everywhere else it
    is off (`appsettings.json`) and the schema comes from `dotnet ef database
    update`; the SQLite tests depend on that. `MigrateOnStartupTests` (SQL
    Server) checks an empty database gets its schema.
- `src/MeetingRoomBooking.Api` — controllers, `Program.cs`, middleware.
  Controllers stay thin and only call Application services.
  - `Errors/ApplicationExceptionHandler` — the only place exceptions become
    HTTP: `DomainException` 400, `ValidationException` 400 (with per-field
    `errors`), `AuthenticationFailedException` 401, `NotFoundException` 404,
    `ForbiddenException` 403, `ConflictException` and
    `ConcurrencyConflictException` 409, as RFC 9457 problem details whose
    `detail` is the user-facing message. Everything else (including a raw
    `UniqueConstraintViolationException` a service forgot to translate) is a
    bug and becomes a generic 500. Don't catch-and-map exceptions in
    controllers.
  - Controllers are secure by default: `[Authorize]` on the class, and
    actions that must be public opt out with `[AllowAnonymous]`.
    `ControllerSecurityTests` enforces this for every controller and pins
    the list of anonymous actions — extend it deliberately when adding one.
  - `Realtime/` — real-time schedule updates (task item 7). `ScheduleHub`
    (`[Authorize]`) only manages groups: a client calls
    `WatchResource(resourceId)` / `StopWatchingResource(resourceId)` and joins
    `resource:{id}`. `SignalRScheduleNotifier` implements Application's
    `IScheduleNotifier` and sends `SlotsChanged` =
    `SlotsChangedMessage(resourceId, slots[{startUtc, endUtc, isBooked}])`
    to that group. Services call it only **after** the commit (the loser of a
    race sends nothing): `BookingService` on book (booked) and cancel (the
    freed slots), `ResourceService.RemoveAsync` for every slot it frees.
    Sending never throws (logged instead) and ignores the request's
    cancellation token — the change is saved and other viewers must hear of
    it. The message never says who booked. Browsers send the JWT as
    `?access_token=`; `Program` accepts that on the hub path only.
  - Outside Development: HSTS and HTTPS redirection. In Azure TLS ends at
    App Service's front end; the app setting
    `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes the app trust its
    `X-Forwarded-*` headers, so it sees HTTPS (no code for it).
  - Hosts the Angular client: the published app has the client's build in
    `wwwroot` (copied there when publishing; git-ignored), served as static
    files, with `index.html` as the fallback for every other path so client
    routes work when opened directly. Unknown `/api/...` and `/hubs/...`
    paths stay 404 problem details, never the client page
    (`ClientHostingTests`). One origin, so no CORS. Locally `wwwroot` is
    empty and `ng serve` serves the client.
  - OpenAPI: the built-in document (`/openapi/v1.json`) shown by Swagger UI at
    `/swagger`, in every environment including Production (decided with
    the deployment: reviewers try the API there; every endpoint except
    register and login still needs a token).
    `OpenApi/BearerSecurityTransformer` adds the Bearer scheme and marks
    exactly the endpoints that require a token, derived from their
    `[Authorize]`/`[AllowAnonymous]` metadata. Endpoint summaries come from
    `///` comments (`GenerateDocumentationFile`, CS1591 suppressed in Api
    only) — write them for every new action.
- Wording rule for comments and commit messages: inner layers *throw*; only
  the API *returns* status codes — write "throws ConflictException, which the
  API returns as 409", not "the service returns 409".
- `tests/MeetingRoomBooking.Tests` — xUnit tests, including the concurrency
  test.

## Design decisions (already made — don't change without asking)

- **One row per 15-minute slot.** A booking for 10:00–11:00 is stored as four
  `BookingSlot` rows whose primary key is `(ResourceId, SlotStartUtc)`
  (`PK_BookingSlots`, clustered), inserted in one transaction: either every
  slot is taken or none is. This
  blocks overlapping bookings at the database level, not only identical
  start times. Slots are inserted in ascending time order so concurrent
  overlapping inserts queue on the first shared slot instead of deadlocking.
- **Concurrency control = that primary key.** The database rejects the
  losing insert. The single commit point (`IUnitOfWork.CompleteAsync`)
  translates *only* a unique-constraint violation into a conflict; every
  other DB error propagates as a real error. Never catch `DbUpdateException`
  broadly — that hides real failures as fake "conflicts".
- **Resource edits use optimistic concurrency.** A `Version` shadow property
  (Guid, `IsConcurrencyToken`) is re-stamped on every save by
  `AppDbContext`, so a stale edit fails instead of silently overwriting
  another admin's change. Deliberately an app-generated Guid rather than SQL
  Server `rowversion`: same technique, but identical on SQL Server and on the
  SQLite test database. Bookings need no version: they are only inserted
  (protected by the primary key) and deleted, never edited.
- **Every instant is UTC; each resource has its own time zone.** Resources
  may be in different countries. Opening hours are local wall-clock times in
  the resource's IANA `TimeZoneId` (Windows ids are converted to IANA);
  bookings and slots are stored in UTC and converted through that zone, so
  local hours stay fixed across daylight-saving changes. The domain rejects
  non-UTC `DateTime`s. `nowUtc` is passed in explicitly (the Application
  layer supplies it), so time-dependent rules are testable. EF Core must
  mark `DateTime`s it reads as `DateTimeKind.Utc`.
- **Booking behaviour.** "Cancel" releases every slot that has not started
  yet (`Booking.Release`): before the start it deletes the whole booking;
  while it is under way ("we finished early") it frees the future slots,
  keeps the past ones and the slot in progress, and moves `EndUtc` back;
  when nothing is left to release it is a 400. A user does this only to
  their own bookings; an admin to any. Cancellation is not in the task
  brief — it exists because item 7 talks about slot status *changing*, and
  a slot becomes free only when released. A schedule shows
  every slot as free/booked/past but never reveals who booked someone else's
  slot (`BookingId` only on the viewer's own slots); admins get a separate
  all-bookings view.
- **Removing a resource** (`DELETE /api/resources/{id}`, the task's
  "remove") never deletes rows: the resource is deactivated (hidden from
  users, not bookable) and every slot of it that has not started is
  released in the same transaction — future bookings are cancelled,
  bookings under way are cut short, past bookings stay as history. An admin
  can restore it; cancelled bookings do not come back. Admins see removed
  resources, users get 404.
- **Booking vs. removal: the resource-row lock.** Invariant: never an active
  booking of a removed resource. Both `BookingService.CreateAsync` and
  `ResourceService.RemoveAsync` open a transaction and first call
  `IResourceRepository.LockAsync` — a no-op `UPDATE Resources SET IsActive =
  IsActive` that takes an exclusive row lock until commit — and only then
  read. Whoever locks first finishes before the other reads: a booking
  committed first is released by the removal; a booking waiting behind a
  removal sees the resource inactive (400). Both take the resource lock
  before anything else, so they cannot deadlock. Cost: bookings of the same
  resource run one after another (milliseconds each); different resources
  never wait. The lock is only for removal consistency — which request wins
  a slot is still decided by the `BookingSlots` primary key. Deliberately an
  UPDATE, not a `SELECT ... WITH (UPDLOCK)`: portable across SQL Server and
  SQLite, and it locks even under READ_COMMITTED_SNAPSHOT (Azure SQL's
  default), where plain reads do not block. Don't remove either lock
  call — `RemovalRaceTests` (SQL Server variant) fails without them.
- **Editing a resource** requires the `Version` from the client's last read
  (`UpdateResourceRequest.Version`); a stale version is a 409, never a silent
  overwrite. Existing bookings are kept even if new opening hours no longer
  cover them.
- **Users never see UTC.** The API returns slots in UTC plus the resource's
  `TimeZoneId`; the frontend shows a schedule in the *resource's* local time,
  labelled (e.g. "Berlin time (UTC+2)"), and adds the viewer's own time as a
  hint only when their time zone differs ("10:00 (11:00 your time)"). When a
  slot is clicked, the frontend sends back the exact UTC value it received —
  it never converts user input itself.
- Use `Europe/Berlin` (or another long-stable id) in tests: Windows ICU on
  some machines does not know `Europe/Kyiv`, only the old `Europe/Kiev`.
- **Auth.** ASP.NET Core Identity (users/roles in the same database, via
  `IdentityDbContext<ApplicationUser>`) plus JWT bearer tokens signed with
  HS256 (`Jwt:Key`, ≥ 32 chars; the app refuses to start without it), 60
  minutes, no refresh tokens. Claims keep their short JWT names (`sub` = user
  id = `Booking.UserId`, `email`, `name`, `role`); the API turns off inbound
  claim mapping. `POST /api/auth/register` always gives role `User` and
  signs in; `POST /api/auth/login` answers wrong password and unknown email
  with the same 401 message; `GET /api/auth/me` echoes the token. An
  `Admin` exists only through `IdentitySeeder` (startup) from
  `Seed:AdminEmail` / `Seed:AdminPassword` — never from registration, never
  hard-coded. Controllers get the caller via `User.ToUserContext()`
  (`Api/Auth/ClaimsPrincipalExtensions`). `Bookings.UserId` deliberately has
  no foreign key to `AspNetUsers`: accounts and bookings stay decoupled, and
  user ids only ever come from validated tokens.

## Deployment

- Azure, free tiers only (README "Deployment" has the resources, limits and
  the portal setup): one Linux Web App (plan F1) serving the API and the
  Angular client, Azure SQL Database (free offer, auto-pause at the free
  limit), Azure SignalR Service (Free_F1, Default mode). Resources are
  created by hand in the portal; there is no infrastructure script.
- `.github/workflows/deploy.yml` builds and tests everything on each push
  and pull request and deploys `main` when all tests pass: `dotnet
  publish`, the client's `ng build` copied into `wwwroot`,
  `azure/webapps-deploy`. Sign-in is OIDC (federated credentials for the
  `main` branch, role Website Contributor on the Web App only); the deploy
  job must not use a GitHub environment, which would change the token's
  subject.
- Configuration comes from the Web App's settings (`Jwt__Key`, `Seed__*`,
  `Azure__SignalR__ConnectionString`, `Database__MigrateOnStartup=true`,
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, connection string
  `Default`); secrets never go into the repository, and
  `appsettings.Development*.json` is excluded from publishing.

## Not built yet

Nothing from the task brief. What exists: the whole backend —
Domain, persistence (migrations `InitialCreate` and `AddIdentity`), the
REST API (auth, resources and schedules, bookings) with exception-to-HTTP
mapping and Swagger UI, real-time updates over SignalR (task item 7), the
concurrency test (task item 6), and their tests — and the Angular client in
`client/` (sign-in, schedules with booking and live updates, my bookings,
admin pages; see `client/CLAUDE.md`), deployed to Azure (see
"Deployment").

## Tests and databases

- Default `dotnet test` runs everything on SQLite — no setup, so a reviewer
  can run it anywhere.
- HTTP-level tests use `tests/.../Api/ApiFactory.cs` (`WebApplicationFactory`):
  the real Program, environment `Testing` (so the developer's
  appsettings.Development.json is never read), a random test `Jwt:Key` and a
  test admin. `ApiFactory` runs it on SQLite; `SqlServerApiFactory` runs it
  unchanged on a migrated SQL Server test database (opt-in, only the
  connection string differs). `RegisterAsync()`, `CreateAdminClientAsync()`,
  `CreateUserClientAsync()` give signed-in clients; `Database` lets a test
  check what was actually stored.
- **The concurrency test (task item 6)** is `tests/.../Api/ConcurrencyTests.cs`:
  20 users, released by one start signal, POST the same slots (and, in a
  second scenario, different ranges sharing one slot) through the full HTTP
  pipeline; asserts exactly one 201, 19 × 409, no 5xx, and exactly one
  booking with its slots in the database; a third scenario repeats the race
  five times. Same scenarios on SQLite (`ConcurrencyTests`, always) and SQL
  Server (`SqlServerConcurrencyTests`, opt-in). On SQLite, which serialises
  writes, they check the API contract; the SQL Server run is what exercises
  the concurrency control — run it after any change to booking, locking or
  the slot key. Keep this test green and meaningful — it is what the
  reviewer runs.
- `SqlServerPersistenceTests` (`[SqlServerFact]`) cover what SQLite cannot:
  SQL Server's duplicate-key error numbers and the migrations. They run only
  when `MEETINGROOMBOOKING_TEST_SQLSERVER` holds a server connection string;
  otherwise they are reported as skipped. Each test class creates and drops
  its own `MeetingRoomBookingTests_<guid>` database by applying the migrations,
  then switches on READ_COMMITTED_SNAPSHOT to behave like Azure SQL.
- Race tests (`RemovalRaceTests`) force an interleaving: hold one side's
  transaction open, start the other, assert it waits, then commit. Only the
  SQL Server variants can catch a missing lock: SQLite's shared-cache reads
  block on uncommitted writes anyway, so there the race cannot happen. Run
  the SQL Server tests before relying on any change to locking.
- Local dev database: `MeetingRoomBookingDb` on `MMU\MSSQLSERVER01`. Never
  modify or drop any other database on that server.
- After changing the EF model, add a migration (command below) and check that
  `dotnet ef migrations has-pending-model-changes` reports none.

## Working conventions

- Work in small steps; one logical change per commit, with a message that
  says what changed and why. Claude prepares and verifies each step and
  proposes the commit message; the developer reviews and commits. Claude
  does not run `git commit` itself.
- Before proposing a commit, run `/verify-step`; draft the commit with
  `/prepare-commit`. Both skills live in `.claude/skills/`.
- The build must stay warning-free — `TreatWarningsAsErrors` is on for every
  project via `Directory.Build.props`.
- Secrets never go into committed files. `appsettings.Development.json` is
  git-ignored; committed `*.example.json` templates document required keys.
- Persistence goes through `IUnitOfWork`; only `UnitOfWork.CompleteAsync`
  calls `SaveChangesAsync`.
- README "How double booking is prevented" is the reviewer-facing
  explanation of the concurrency design (task item 5 asks for an explained
  decision). Any change to booking, the slot key, conflict translation or
  locking must update it in the same commit, and its claims must match the
  code and the tests.
- Before trusting a new test, prove it can fail: break the code it guards,
  watch it go red, restore. Then rebuild with `--no-incremental` — a restored
  file can keep an old timestamp and leave a stale build behind.

## Commands

```bash
dotnet build MeetingRoomBooking.slnx
dotnet test MeetingRoomBooking.slnx
dotnet run --project src/MeetingRoomBooking.Api

# EF Core CLI is a local tool pinned in dotnet-tools.json
dotnet tool restore
dotnet ef migrations add <Name> --project src/MeetingRoomBooking.Infrastructure --startup-project src/MeetingRoomBooking.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/MeetingRoomBooking.Infrastructure --startup-project src/MeetingRoomBooking.Api

# Angular client (run inside client/)
npm ci
npx ng serve
npx ng build
npx ng test --watch=false
```
