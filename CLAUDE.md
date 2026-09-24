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
  interfaces Infrastructure implements (repositories, unit of work,
  notifier). References Domain only — in particular, no EF Core.
- `src/MeetingRoomBooking.Infrastructure` — EF Core, Identity, SignalR.
- `src/MeetingRoomBooking.Api` — controllers, `Program.cs`, middleware.
  Controllers stay thin and only call Application services.
- `tests/MeetingRoomBooking.Tests` — xUnit tests, including the concurrency
  test.

## Design decisions (already made — don't change without asking)

- **One row per 15-minute slot.** A booking for 10:00–11:00 is stored as four
  `BookingSlot` rows with a unique index on `(ResourceId, SlotStartUtc)`,
  inserted in one transaction: either every slot is taken or none is. This
  blocks overlapping bookings at the database level, not only identical
  start times. Slots are inserted in ascending time order so concurrent
  overlapping inserts queue on the first shared slot instead of deadlocking.
- **Concurrency control = that unique index.** The database rejects the
  losing insert. The single commit point (`IUnitOfWork.CompleteAsync`)
  translates *only* a unique-constraint violation into a conflict; every
  other DB error propagates as a real error. Never catch `DbUpdateException`
  broadly — that hides real failures as fake "conflicts".
- **Every instant is UTC; each resource has its own time zone.** Resources
  may be in different countries. Opening hours are local wall-clock times in
  the resource's IANA `TimeZoneId` (Windows ids are converted to IANA);
  bookings and slots are stored in UTC and converted through that zone, so
  local hours stay fixed across daylight-saving changes. The domain rejects
  non-UTC `DateTime`s. `nowUtc` is passed in explicitly (the Application
  layer supplies it), so time-dependent rules are testable. EF Core must
  mark `DateTime`s it reads as `DateTimeKind.Utc`.
- **Users never see UTC.** The API returns slots in UTC plus the resource's
  `TimeZoneId`; the frontend shows a schedule in the *resource's* local time,
  labelled (e.g. "Berlin time (UTC+2)"), and adds the viewer's own time as a
  hint only when their time zone differs ("10:00 (11:00 your time)"). When a
  slot is clicked, the frontend sends back the exact UTC value it received —
  it never converts user input itself.
- Use `Europe/Berlin` (or another long-stable id) in tests: Windows ICU on
  some machines does not know `Europe/Kyiv`, only the old `Europe/Kiev`.
- **Auth:** JWT signed with HS256 using `Jwt:Key`. Registration always yields
  `User`; an `Admin` is created only by a startup seeder from
  `Seed:AdminEmail` / `Seed:AdminPassword` config.

## Not built yet

Persistence (EF Core, the unique `(ResourceId, SlotStartUtc)` constraint,
migrations), booking service, concurrency test, auth, API endpoints,
SignalR, Angular client, Azure deployment. The Domain layer and its unit
tests exist; nothing else does yet.

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
```
