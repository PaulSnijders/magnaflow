---
date: 2026-07-15
topic: cockpit, watch
status: accepted
---

# 0011 — mf-cockpit watch toggle

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

After "Add project" (decision 0009) a new project was spec-first-ready,
but nothing turned its watcher on without a manual step. mf-watch has
no per-project "on" in its config: it runs one project per process,
selected by `--project`. Linux already had real per-project supervision
(the `mf-watch@.service` systemd template from `install.sh`). Windows
had none: `start-magnaflow.ps1` starts one `mf-watch.exe`, hardcoded to
a single project at install time.

## Options

1. **One mechanism everywhere**: the cockpit spawns and kills mf-watch
   itself on every OS.
2. **Per OS**: on Linux shell out to `systemctl --user` against the
   existing template unit; on Windows spawn and kill mf-watch directly.
3. **Windows scheduled tasks or services** per project, so a toggled
   watcher survives a reboot.

For the Windows "is it running, which PID" question: a PID record kept
by the cockpit, or mf-watch's own lock file.

## Measurement

- Use real supervision where it exists: systemd already gives
  per-project instances that survive reboot and logout, so spawning by
  hand on Linux would be a step back.
- "Shell out, own nothing", as with mf-run: no process logic in the
  cockpit where an OS tool already has it.
- One source of truth for liveness. A record held by the cockpit can
  disagree with reality, and is lost when the cockpit restarts between
  a start and a stop click.
- Effort: option 3 needs installer work that was not attempted.

## Choice

Option 2, chosen once at startup by OS (`IWatchControl`). Current
behavior: `specs/concepts/watch-supervision.md`, and the Watcher card
in `specs/cockpit/project.md`.

- **Liveness on Windows comes from `.magnaflow/mf-watch.lock`**, already
  the "a watcher is alive" signal. That required mf-watch to keep the
  lock readable by others and to write PID plus start time, the same
  shape as mf-run's PID files, so the cockpit applies mf-run's PID-reuse
  guard instead of trusting a crash-leftover file. The cockpit
  duplicates the read side of that format rather than referencing
  mf-watch: the standing decoupling rule.
- **The Windows spawn seam is duplicated from mf-run's**, not shared,
  for the same reason.
- **Known gap, accepted**: a watcher toggled on under Windows does not
  survive reboot or logout (option 3 left for later).
- **Scoped out for v1**: no post-start liveness check on Windows (an
  immediate failure shows "started" and never becomes running), no
  capture of mf-watch's stderr before its own log starts, and no
  broadcast on toggle (the clicking page refetches; other open clients
  see the change on their next watch event or reload). Each is a
  follow-up if it turns out to matter.

Shipped alongside: the spec kit's adopt prompt derives a project's
mf-run `run:` section, verified against the actual build output and
left out for library or CLI-only projects.
