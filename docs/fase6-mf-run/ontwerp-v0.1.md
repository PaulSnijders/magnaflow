# mf-run v0.1 — design (2026-07-14)

The missing piece between "the worker finished" and "you are testing
the result": a small process manager for the project's own running
application(s) — the dev/debug instance on the worker machine. It
exists because Claude Code trips over locked ports and files while
the app is running, and because after a run you want the fresh build
already up so you can click through it immediately, remotely.

```text
worker run: plan gate passed, implementation about to start
      → mf-run stop        (ports and files released)
      → implement, build, test   (all unchanged)
      → mf-run start       (fresh build running)
      → human opens the app's URL from their own machine
```

## Why a separate tool

The core of this feature is not orchestration but process
management: PID files, killing an entire process tree (the reason
ports stay locked), starting several services in order, reporting
status. That is a bounded job with its own lifetime — a small
deterministic tool in the MagnaFlow sense. It pays out three ways:
the worker stays thin (spawn `mf-run stop`/`start`, read the exit
code, done — no library coupling, same decoupling mf-watch proved);
the human gets the manual lever (`mf-run stop` beats hunting a
process tree by hand); and the cockpit later gets a status dot and
start/stop buttons as plain shell-outs instead of duplicated process
logic. mf-watch stays out of it entirely — it still knows only
frontmatter statuses and exit codes.

## Commands

```text
mf-run start   [service] [--project <path>]
mf-run stop    [service] [--project <path>]
mf-run restart [service] [--project <path>]
mf-run status  [service] [--project <path>]
```

No service name = all configured services (start in list order, stop
in reverse). `status` prints one line per service (name, running or
stopped, PID, url if configured) and exits 0 if everything asked
about is running, 1 otherwise — so scripts and the worker can branch
on it without parsing text.

## Config

In the **project's** `.magnaflow/config.yml` — running is a property
of the project, not of the machine. A console app simply has no
`run:` block and everything below is skipped.

```yaml
run:
  services:
    - name: web
      command: src/MyApp/bin/Debug/net10.0/MyApp.exe
      args: ["--urls", "http://0.0.0.0:5000"]   # optional
      workdir: src/MyApp                         # optional, default project root
      url: http://worker-1:5000                  # optional, informational
```

A list from day one: api + frontend style projects are real, and a
list is no harder than a single item. `command` should point at the
**built executable**, not `dotnet run` — `dotnet run` spawns a child
process and rebuilds on start; the built exe is what the worker's
own build step just produced, starts instantly, and dies cleanly.
(Tree-kill below handles the child-process case anyway, for anyone
who configures it regardless.)

`url` is not used by mf-run to do anything — it is printed by
`status`, echoed into the worker's report, and later rendered by the
cockpit as a clickable link with a running/stopped dot. Binding to
`0.0.0.0` (or a Tailscale address) so the debug instance is
reachable from the human's own machine is the service's own args or
appsettings — mf-run does not manage binding, same perimeter honesty
as the cockpit (VPN/LAN is the boundary, no auth or tunnel here).

## Process management

- **Detached start**: services are spawned detached (no window,
  stdout/stderr redirected), so they outlive mf-run and the worker
  that called it.
- **PID files**: `.magnaflow/run/<service>.pid` — plain text: PID
  plus the process start time. The start time guards against PID
  reuse (a recycled PID belongs to a process with a different start
  time → the file is stale, not a hit). `cat` suffices, as always.
- **Stop kills the whole tree** (`Process.Kill(entireProcessTree:
  true)`), because an orphaned child is exactly the locked-port
  problem this tool exists to solve. Stop is idempotent: nothing
  running (or a stale PID file) is cleaned up silently and exits 0 —
  the worker calls stop unconditionally and must not fail on "was
  not running".
- **Start liveness check**: after spawning, wait ~2 s; if the
  process already died, report failure and print the tail of its
  log. No health checks or URL polling in v0.1 — "the process is
  alive" is the contract; whether it serves correctly is what the
  human is about to test anyway.
- **Start is a no-op if already running** (per the PID file + start
  time), exit 0 with a message. `restart` = stop + start.
- **Service logs**: stdout/stderr to `.magnaflow/run/<service>.log`,
  truncated on each start — the log of the *current* instance, next
  to the worker's own evidence, tail-viewable by the cockpit later.

## Worker integration

The worker controller learns two moments, both config-driven and
both skipped entirely when the project has no `run:` services:

- **Stop after the plan gate, immediately before implementation.**
  Not at the start of the run: the plan phase touches no code, so
  the app keeps running while planning — and keeps running through a
  `questions` pause, so the human can keep clicking around while
  answering. A **stop failure aborts before any code change**, in
  the same posture as the dirty-tree guard: refuse to start work,
  leave the command `ready`, consume no attempt, notify — the locked
  ports the stop was meant to release are still locked, so
  implementing anyway would hit exactly the problem this exists to
  prevent.
- **Start after a terminal status — `done` and `aborted` both.**
  After `aborted` the last successful build may still be perfectly
  runnable, and looking at the running app is often how you diagnose
  what went wrong. A **start failure is a warning, never a retry**:
  a line in the rst report (with the service log tail) and the
  normal notification channel — it does not change the run's status.
  A `questions` pause starts nothing, because it stopped nothing.

Mechanically: the worker spawns `<run.command> stop --project
<root>` / `start ...` as a process and reads the exit code —
`run.command` defaults to `mf-run` and is substitutable, the same
FR-019-style stub mechanism every tool's validation has used. No
library reference in either direction.

## Repo hygiene and mf-spec

Everything under `.magnaflow/run/` (PID files, service logs) is
local-only machine state and MUST be gitignored — untracked it
dirties the very tree the worker guards, and the dirty-tree guard
then blocks every run: the exact lesson `mf-watch.log`/`.lock`
already taught. This fase therefore also carries the small mf-spec
updates along: the runtime-plane description in
`docs/mf-spec/system.md` gains `.magnaflow/run/`, the kit's adopt
prompt (0001) adds the ignore line next to the existing mf-watch
pair, and KIT.md gets a migration note plus a version-stamp bump so
existing target repos pick it up idempotently via update prompt
0002.

**mf-watch is deliberately untouched.** A stop-failure refusal
leaves the command `ready` with a non-zero worker exit — the same
shape as the dirty-tree refusal mf-watch already reports as an error
and simply re-encounters on the next poll. No new statuses, no new
exit codes, no mf-run awareness in the watcher.

## Invariant: optional, like everything else

Every mf-run action is something a human can do by hand: start the
exe, kill the tree, delete a PID file. PID files and logs are plain
text in `.magnaflow/run/`. The worker without a `run:` block, and
mf-run without a worker, both behave exactly as before/standalone.
If a mechanism can't also be done manually, the mechanism is wrong —
same rule as the worker, mf-watch, and the cockpit.

## Guards

- PID-reuse guard via recorded process start time (never kill an
  innocent recycled PID).
- Tree-kill on stop; stop idempotent; stale PID files cleaned up.
- Start refuses with a clear message when the command path does not
  exist (the usual cause: build output path typo, or start attempted
  after a failed build).
- No two services may share a name; config validation fails fast.

## Tech

C# console app, .NET 10, `tools/mf-run/` — same toolchain and
conventions as the other three tools. YamlDotNet for the config
read; no Spectre.Console.Cli needed for four verbs (mf-watch
precedent). Reads the target project's `.magnaflow/config.yml`;
`--project` defaults to the current directory, same as the worker.

## Cockpit (later, explicitly not v0.1)

`project.html` gets a service card: name, running/stopped dot
(shell-out to `mf-run status`), the `url` as a link, start/stop
buttons — each an explicit human-initiated action per the cockpit's
own invariant, and each a process spawn, no shared code. Designed
here so mf-run's `status` exit-code/output contract is stable enough
to build on; built in a cockpit version, not now.

> **Built:** `docs/fase5-cockpit/ontwerp-v0.3.md` /
> `v0.3-completion-notes.md` — the Run card described above, plus
> the `status --json` flag it needed (added to this tool by that
> fase, tests in this tool's own suite).

## Not in scope (v0.1)

Health checks / URL polling, log rotation, auto-restart on file
change (watch mode), per-service environment variables, anything
deploy-shaped, auth or tunnels for remote access (the network is the
perimeter, as with the cockpit), and the cockpit card above.
