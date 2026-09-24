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
   and set `ConnectionStrings:Default` to your SQL Server.
2. Create the database:
   ```bash
   dotnet tool restore
   dotnet ef database update --project src/MeetingRoomBooking.Infrastructure --startup-project src/MeetingRoomBooking.Api
   ```
3. Run it:
   ```bash
   dotnet run --project src/MeetingRoomBooking.Api
   ```

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
