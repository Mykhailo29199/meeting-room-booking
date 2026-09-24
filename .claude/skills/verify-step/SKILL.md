---
name: verify-step
description: Full pre-commit verification for this repository — clean rebuild, all tests, what git would commit, and a scan for leaked secrets. Use before proposing any commit, or when the user asks "is it ready", "check everything", "can I commit".
---

# verify-step

Runs every check a step must pass before it is committed, and reports the
result as a short checklist. It never commits.

Run the commands from the repository root (Git Bash syntax).

## 1. Clean build

```bash
dotnet build MeetingRoomBooking.slnx --no-incremental
```

Must end with `0 Warning(s)` and `0 Error(s)`. `--no-incremental` matters: a
file restored after a mutation check can keep an old timestamp, and an
incremental build would then test stale binaries.

## 2. Tests

```bash
dotnet test MeetingRoomBooking.slnx --no-build
```

Report passed/failed counts. If this step added or changed a test, confirm it
was proven able to fail (break the guarded code, see it go red, restore). If
that was not done, do it now — a test that cannot fail proves nothing.

## 3. What would be committed

```bash
git status --short -uall
```

List the files and flag anything that must not be committed: `bin/`, `obj/`,
`.vs/`, `*.user`, `appsettings.Development.json`, databases, large binaries.

## 4. Secrets

```bash
git check-ignore -q src/MeetingRoomBooking.Api/appsettings.Development.json && echo "dev settings ignored"
# This skill file is excluded: it contains the pattern itself.
git ls-files -mo --exclude-standard -- . ':!.claude/skills/verify-step' | xargs -r grep -nIE \
  '(Password|Pwd)=[^;"<]+|AccountKey=|AccessKey=|"Key":\s*"[^"<]{8,}|"AdminPassword":\s*"[^"<]+"'
```

The grep must print nothing. Placeholders in `<angle brackets>` inside
`*.example.json` are fine.

## 5. Documentation still true

If the step added or changed a feature, check that `CLAUDE.md` ("Not built
yet", design decisions) and `README.md` still describe reality. Stale docs
count as a failed check.

## Report

A compact checklist, one line per check (✅ / ❌ with the reason), then a
verdict: **ready to commit**, or the list of blockers. Do not run
`git add` or `git commit` — suggest `/prepare-commit` instead.
