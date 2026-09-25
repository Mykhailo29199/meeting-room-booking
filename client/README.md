# Meeting Room Booking — client

The Angular frontend of the Meeting Room Booking System: Angular 22,
Angular Material, Vitest. The backend, its API and the design behind both
are described in the [repository README](../README.md).

## Prerequisites

- Node.js 24.15+ (or 22.22.3+)
- The API running locally on `http://localhost:5077`:
  `dotnet run --project ../src/MeetingRoomBooking.Api` (or F5 in Visual Studio)

## Run

```bash
npm ci       # install exactly the versions package-lock.json pins
npm start    # dev server on http://localhost:4200
```

The dev server forwards `/api` and `/hubs` (including WebSockets) to the
API ([proxy.conf.json](proxy.conf.json)), so the browser talks to one
origin and no CORS setup is needed locally.

## Build and test

```bash
npx ng build                # production build into dist/
npx ng test --watch=false   # unit tests (Vitest)
```
