# Meeting Room Booking System

Concurrency-safe meeting room booking with real-time schedule updates
(ASP.NET Core, Angular, Azure SignalR Service, Azure SQL).

Work in progress — see `CLAUDE.md` for the architecture, design decisions and
what is built so far.

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
