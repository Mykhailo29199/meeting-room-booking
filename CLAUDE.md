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
- Real-time: SignalR, Azure SignalR Service in production (planned).
- Frontend: Angular in `client/` (planned).

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
    `ConcurrencyConflictException` (stale edit → 409).
  - `Bookings/BookingService` — create, cancel, day schedule. Services are
    concrete classes registered in `Program.cs`; they get the caller as a
    `Common/UserContext(UserId, IsAdmin)` parameter and "now" from an
    injected `TimeProvider` (tests use `FixedTimeProvider`).
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
  all-bookings view. A booking that commits at the same moment an admin
  deactivates the resource is accepted on purpose: deactivation keeps
  existing bookings, so it equals booking just before — no lock needed.
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

## Not built yet

Swagger UI, booking/schedule/resource endpoints, resource management and
all-bookings admin use cases, the parallel-requests concurrency test,
SignalR, Angular client, Azure deployment. What exists: the Domain layer,
the persistence layer (EF Core model, unit of work, repositories, migrations
`InitialCreate` and `AddIdentity`), the booking service (create, cancel,
schedule), authentication (register, login, me, roles, admin seeding), the
exception-to-HTTP mapping, and their tests.

## Tests and databases

- Default `dotnet test` runs everything on SQLite — no setup, so a reviewer
  can run it anywhere.
- HTTP-level tests use `tests/.../Api/ApiFactory` (`WebApplicationFactory`):
  the real Program on SQLite, environment `Testing` (so the developer's
  appsettings.Development.json is never read), a random test `Jwt:Key` and a
  test admin. `ApiFactory.RegisterAsync()` / `CreateClient(token)` give a
  signed-in client.
- `SqlServerPersistenceTests` (`[SqlServerFact]`) cover what SQLite cannot:
  SQL Server's duplicate-key error numbers and the migrations. They run only
  when `MEETINGROOMBOOKING_TEST_SQLSERVER` holds a server connection string;
  otherwise they are reported as skipped. Each test class creates and drops
  its own `MeetingRoomBookingTests_<guid>` database by applying the migrations.
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
```
