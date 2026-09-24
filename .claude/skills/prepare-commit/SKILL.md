---
name: prepare-commit
description: Draft an atomic commit for the current changes following this repository's conventions, as ready-to-paste commands — never runs git commit. Use when the user asks for a commit message, "how do I commit this", or after verify-step passes.
---

# prepare-commit

The task requires an atomic commit history where every commit says what
changed and why. The developer commits; this skill prepares the commit.

## 1. Look at the changes

```bash
git status --short -uall
git diff
```

Read new (untracked) files too — `git diff` does not show them.

## 2. Check atomicity

One commit = one logical change. If the changes mix unrelated things (for
example a feature plus an unrelated doc fix), propose a split: file groups in
dependency order, one message each. If files that must never be committed
show up, stop and point them out (run `/verify-step`).

## 3. Write the message

- **Subject:** imperative mood, English, at most 72 characters, no trailing
  period — "Add booking service", not "Added" or "Adds".
- **Body:** what changed and **why** — the reason a reviewer needs, such as
  the design decision it implements or the bug it prevents. Not a file list;
  the diff already shows that.

## 4. Output

Give ready-to-paste commands that work in both PowerShell and Git Bash:

```bash
git add <explicit paths>
git commit -m "<subject>" -m "<body>"
```

- Use explicit paths rather than `git add .` unless every change belongs to
  this commit.
- Do not use double quotes inside the message text.
- Do **not** run `git add` or `git commit` yourself.
