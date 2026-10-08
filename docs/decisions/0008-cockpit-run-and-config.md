---
date: 2026-07-14
topic: cockpit, run, config
status: accepted
---

# 0008 — mf-cockpit v0.3 — design: Run & Config

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

mf-run's design (0010-mf-run-design.md) deferred a service card to the
cockpit. The goal: from the cockpit, see every project's services, start
and stop them, follow the url to the running debug instance, and adjust
`.magnaflow/config.yml` — without an editor or SSH session on the worker
machine. Two things stood in the way of the v0.1 invariant
(0007-cockpit-design.md): it said the cockpit "never touches
`.magnaflow/`", and it had no notion of managing processes.

## Options

- **Process control**: the cockpit manages service processes itself, or
  every status read and button is a shell-out to the mf-run binary.
- **Reading mf-run's status**: parse its human-facing output, or add a
  `status --json` flag.
- **Config editing**: a form over the parsed config, or the raw YAML
  text with a save.
- **Machine config (`magnaflow.yml`)**: editable here too, or not.

## Measurement

The invariant (every write human-initiated, landing in the existing
plain-text/git formats, no state only inside the cockpit), and
"composes by spawning, never by library reference" from
concepts/design.md.

- Managing processes in the cockpit would duplicate mf-run's logic; its
  design already intended the cockpit to get "a status dot and
  start/stop buttons as plain shell-outs".
- Parsing human-facing lines is fragile and turns their wording into an
  accidental API; a flag is honest.
- A form that regenerates YAML destroys comments and ordering — the file
  would then hold less than the cockpit, exactly what the invariant
  forbids.
- The machine config is per machine, possibly not the browser's machine;
  the cockpit editing its own config is self-surgery.

## Choice

- **Invariant clarification.** "Never touches `.magnaflow/`" protects the
  **run evidence** (`.magnaflow/<id>/`, the worker's own diary) and keeps
  meaning exactly that. `.magnaflow/config.yml` becomes editable (write
  #5: a human save, committed at once; git is the undo).
  `.magnaflow/run/` is read for display only and is gitignored; the run
  actions write nothing in git and touch OS processes only through
  mf-run.
- **Every Run read and button shells out to mf-run**; `run.command` in
  machine config is the substitutable seam, the same stub mechanism as
  `chat.command`. mf-run gained `status --json`, its exit codes
  unchanged.
- **Raw YAML editor, not a form.** The file stays the source of truth.
  A structured helper for common edits (e.g. add a service) may sit on
  top later, as long as it generates a *text* edit. Unknown keys warn,
  never reject: the file is shared by tools with different vocabularies,
  and the cockpit must not become the schema police for all of them.
- **Warn, don't block** while a command runs: editing then is harmless,
  surprising silence would not be.
- **Bind/auth unchanged** (VPN/LAN is the perimeter), but it now covers
  starting and stopping processes remotely, so binding beyond localhost
  is even more explicitly the operator's call.

Not in scope (v0.3): editing the machine config (later narrowed by
0009-cockpit-add-project.md for one structured append), a form editor,
service log live-streaming (bounded tail plus SSE refresh suffices),
health checks or URL probing (the dot meant "process alive", mf-run's
own contract — later revised; see specs/cockpit/project.md), auth.

Current behavior: specs/cockpit/project.md (Run card),
specs/cockpit/config.md (config page, save flow, guards),
specs/cockpit/index.md (Run column), specs/run/mf-run.md (`--json`),
concepts/machine-config.md (`run.command`, timeouts).
