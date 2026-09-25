# Meeting Room Booking System

Concurrency-safe meeting room booking with real-time schedule updates
(ASP.NET Core, Angular, Azure SignalR Service, Azure SQL).

Work in progress — see `CLAUDE.md` for the architecture, design decisions and
what is built so far.

## How double booking is prevented

**The requirement.** When several requests for the same slot arrive at
effectively the same time, exactly one succeeds and the others get a clear
conflict response — never a silent overwrite, never a server error.

**The mechanism: the database's primary key on individual slots.**

- A booking is one or more consecutive 15-minute slots. Besides the
  `Bookings` row, every slot it occupies is stored as a row in
  `BookingSlots`, whose **primary key is `(ResourceId, SlotStartUtc)`**
  ([`BookingSlotConfiguration`](src/MeetingRoomBooking.Infrastructure/Persistence/Configurations/BookingSlotConfiguration.cs)).
  A booking for 10:00–11:00 owns the rows 10:00, 10:15, 10:30 and 10:45.
- Creating a booking inserts the booking and all its slot rows in **one
  transaction**. The database accepts the insert only if none of those
  primary-key values exists yet. Of several racing requests, the first to
  commit wins; every other one fails with a duplicate-key error (SQL Server
  2627) and its whole transaction is rolled back — no partial booking.
- There is **no "check if free, then insert" step** that could race: the
  code never asks whether a slot is free before booking it. The database
  decides, atomically, at insert time.
- Only that error becomes a conflict:
  [`UnitOfWork`](src/MeetingRoomBooking.Infrastructure/Persistence/UnitOfWork.cs)
  recognises exactly the unique-key violation and turns it into
  `UniqueConstraintViolationException`;
  [`BookingService`](src/MeetingRoomBooking.Application/Bookings/BookingService.cs)
  turns that into `ConflictException`, which the API returns as **409** with
  a readable message. Any other database error is not caught and stays a
  real error, so a genuine failure is never disguised as "someone else was
  faster".

**Why slots, not one row per booking.** Bookings are ranges. A unique index on
the booking's start time alone would reject two bookings starting at 10:00,
but accept 10:00–11:00 and 10:15–10:30 together. SQL Server has no
constraint for "ranges must not overlap". Splitting a booking into slots
turns "no overlap" into "no duplicate key", which a primary key enforces:
any two overlapping bookings share at least one slot.

**Why not the alternatives.**

| Approach | Why not here |
|---|---|
| Check if free, then insert | Two requests both see "free" and both insert. Explicitly ruled out by the task. |
| Transactional locking (`SERIALIZABLE` / `UPDLOCK, HOLDLOCK` + overlap query) | Correct if every detail is right, but relies on subtle, database-specific range-lock behaviour; the key constraint gives the same guarantee declaratively. |
| Optimistic concurrency with a version column (`rowversion`) | Protects *updates* of an existing row. A free slot has no row to version; it would require pre-creating a row for every slot of every resource for every future day. |
| Unique index on the booking's start time | Misses overlapping ranges (see above). |

**No deadlocks between racing bookings.** The primary key is clustered, so
slot rows are stored in time order and every booking inserts its slots in
ascending order: overlapping transactions wait on the first shared slot
instead of locking in opposite orders.

**Removing a resource while someone books it.** Admins can remove a resource;
this cancels its future bookings in one transaction. So that no active
booking of a removed resource can survive a race, booking and removal both
start their transaction by taking an exclusive lock on the resource's row
(a no-op `UPDATE`,
[`ResourceRepository.LockAsync`](src/MeetingRoomBooking.Infrastructure/Persistence/Repositories/ResourceRepository.cs)).
Whichever gets it first finishes before the other reads. The cost is that
bookings of the *same* resource run one after another for a few
milliseconds each; different resources never wait for each other. Which
request wins a slot is still decided by the primary key.

**Editing a resource.** Two admins editing the same resource at once must not
overwrite each other. Resources carry a version token (a `Guid`, renewed on
every save): an update is written only if the stored version still equals
the one the admin loaded, otherwise it is a 409. It is an application-managed
`Guid` rather than SQL Server's `rowversion` so that it behaves identically
on SQL Server and on the SQLite database the tests run against.

**How it is proven.** The [concurrency test](#the-concurrency-test) races 20
simultaneous HTTP requests for the same slot — and 20 overlapping ranges —
and checks for exactly one booking; race tests force both orders of a
booking and a removal. The primary key, the narrow conflict translation, the
resource-row lock and the version check are each covered by a test that
fails when that guard is removed (the lock by the SQL Server run, since
SQLite serialises writes anyway).

## Prerequisites
- .NET 10 SDK
- SQL Server (any edition, including Express or LocalDB) to run the API

## Build and test
```bash
dotnet build MeetingRoomBooking.slnx
dotnet test MeetingRoomBooking.slnx
```
The tests need no setup: they run against an in-memory SQLite database.
Tests that need a real SQL Server are skipped unless you point them at one:
```bash
# PowerShell
$env:MEETINGROOMBOOKING_TEST_SQLSERVER = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True"
dotnet test MeetingRoomBooking.slnx
```
They create and drop their own temporary databases.

### The concurrency test
```bash
dotnet test MeetingRoomBooking.slnx --filter "FullyQualifiedName~ConcurrencyTests"
```
20 users send a booking request for the same slot at the same moment,
through the full HTTP API. The test asserts that exactly one request gets
`201 Created`, the other 19 get `409 Conflict` (never a server error), and
exactly one booking is stored. A second scenario sends 20 *different* time
ranges that all share one slot — the case a unique index on the booking's
start time alone would miss; a third repeats the race five times. With
`MEETINGROOMBOOKING_TEST_SQLSERVER` set, the same scenarios also run on SQL
Server (`SqlServerConcurrencyTests`). On SQLite, which serialises writes, the
scenarios check the API contract; the SQL Server run is what exercises the
concurrency control.

## Run the API locally
1. Create your local settings from the template (the copy is git-ignored):
   ```bash
   cp src/MeetingRoomBooking.Api/appsettings.Development.example.json src/MeetingRoomBooking.Api/appsettings.Development.json
   ```
   and fill in:
   - `ConnectionStrings:Default` — your SQL Server;
   - `Jwt:Key` — any random secret of at least 32 characters (the API will not
     start without it);
   - `Seed:AdminEmail` / `Seed:AdminPassword` — the admin account created on
     startup (8+ characters with upper and lower case, a digit and a symbol).
     Registration through the API only ever creates regular users.
2. Create the database:
   ```bash
   dotnet tool restore
   dotnet ef database update --project src/MeetingRoomBooking.Infrastructure --startup-project src/MeetingRoomBooking.Api
   ```
3. Run it:
   ```bash
   dotnet run --project src/MeetingRoomBooking.Api
   ```
4. Open Swagger UI at http://localhost:5077/swagger (Visual Studio opens it
   automatically). To call protected endpoints:
   1. `POST /api/auth/login` with the admin from `Seed:*` (or
      `POST /api/auth/register` to create a regular user);
   2. copy `accessToken` from the response;
   3. click **Authorize** and paste the token (without `Bearer `).

   Endpoints with a lock icon need a token; `GET /api/auth/me` shows whose
   token you are using.

## API overview
All endpoints except register and login need a bearer token. Times in
requests and responses are UTC; each resource carries its IANA time zone for
display.

| Endpoint | Who | What |
|---|---|---|
| `POST /api/auth/register`, `POST /api/auth/login` | anyone | get an access token |
| `GET /api/auth/me` | signed in | whose token this is |
| `GET /api/resources`, `GET /api/resources/{id}` | signed in | resources (admins also see removed ones) |
| `GET /api/resources/{id}/schedule?date=` | signed in | the day's 15-minute slots: free, booked, past |
| `POST /api/bookings` | signed in | book slots; 409 if someone took them first |
| `DELETE /api/bookings/{id}` | owner or admin | cancel, or end early if under way |
| `GET /api/bookings/mine` | signed in | own bookings |
| `GET /api/bookings` | admin | all users' bookings |
| `POST/PUT/DELETE /api/resources…`, `POST /api/resources/{id}/restore` | admin | create, edit, remove, restore resources |

## Real-time updates
Everyone viewing a resource's schedule sees bookings and cancellations
immediately, without refreshing (SignalR; Azure SignalR Service in Azure).

1. Connect to `/hubs/schedule` with the access token (browsers send it as
   `?access_token=<token>`; the official SignalR client does this for you).
2. Call `WatchResource(resourceId)` for the schedule on screen
   (`StopWatchingResource` when leaving it).
3. Handle `SlotsChanged`: `{ resourceId, slots: [{ startUtc, endUtc, isBooked }] }`
   — mark those slots booked or free.

Events are sent only after a change is saved, and never say who booked.

## Development with Claude Code
This project is built with Claude Code as a pair programmer. I set the
direction, make the design decisions, test the running application, review
every step and make every commit. Claude scaffolds, drafts code and docs, runs
builds and tests, and proposes commit messages.

- [`CLAUDE.md`](CLAUDE.md) holds the design decisions and working rules that
  Claude follows in every session.
- Custom skills in [`.claude/skills/`](.claude/skills) automate the routine
  before every commit: `/verify-step` (clean build, tests, what would be
  committed, secret scan, docs still accurate) and `/prepare-commit` (checks
  the change is one logical commit and drafts its message).
