# Meeting Room Booking System

Concurrency-safe meeting room booking with real-time schedule updates
(ASP.NET Core, Angular, Azure SignalR Service, Azure SQL).

Work in progress — see `CLAUDE.md` for the architecture, design decisions and
what is built so far.

## Prerequisites
- .NET 10 SDK

## Build and test
```bash
dotnet build MeetingRoomBooking.slnx
dotnet test MeetingRoomBooking.slnx
```

## Development with Claude Code
This project is built with Claude Code as a pair programmer. I set the
direction, make the design decisions, test the running application, review
every step and make every commit. Claude scaffolds, drafts code and docs, runs
builds and tests, and proposes commit messages.

- [`CLAUDE.md`](CLAUDE.md) holds the design decisions and working rules that
  Claude follows in every session.
