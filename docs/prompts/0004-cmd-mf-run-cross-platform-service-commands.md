---
title: "mf-run: cross-platform service commands (per-OS wrapper resolution)"
status: done
attempts: 1
created: 2026-07-14
---

## Context

A project's `.magnaflow/config.yml` travels in git and must work on both the
Windows dev machine and a future Linux worker. Today a service `command` is
one literal file path, so wrappers are OS-locked: wozzol currently points at
`web/Wozzol/mf-start-web.cmd` and `wozzol-ionic/mf-start-ionic.cmd`, which
cannot run on Linux — and a Linux-only `.sh` path would break Windows. The
wrappers exist for good reasons (mf-run spawns one real file, no shell
strings, no env vars), so the fix is resolution, not shell support.

## Task

1. **Extensionless command resolution.** When a service `command` has no
   extension and the literal file does not exist, resolve per OS before the
   existing exists-check: Windows tries `<command>.cmd` then `<command>.bat`
   then `<command>.exe`; Unix tries `<command>` then `<command>.sh`. A
   command *with* an extension keeps today's exact behavior. Resolution is
   logged in start output (which file was picked).
2. **Unix spawn parity.** Verify the `/bin/sh` branch of
   `SystemProcessSpawner` end-to-end with a `.sh` wrapper: args pass-through
   (`"$@"` equivalent of the .cmd `%*` pattern), detach, log redirect, kill
   tree. Note in README that `.sh` wrappers need the executable bit (or are
   invoked via `sh` explicitly — pick one, document it).
3. **Tests.** Resolution order per OS (fake spawner + temp files), with-
   extension unchanged, not-found error message lists what was tried.
4. **README.** Document the convention: commit one wrapper per OS next to
   each other (`mf-start-web.cmd` + `mf-start-web.sh`), reference them
   extensionless from config.

## Constraints

- Constitution: KISS, no new dependencies. mf-run still never runs shell
  strings and never sets env vars — wrappers own that.
- Follow-up outside this repo (manual, after this lands): wozzol2 adds the
  two `.sh` wrappers and drops the extensions in its config.yml.
