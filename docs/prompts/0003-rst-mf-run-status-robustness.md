---
title: "mf-run: robust + explainable status (tolerant PID guard, reasons, port probe)"
cmd: 0003-cmd-mf-run-status-robustness.md
done: 2026-07-14
---

## What was done

**`ServiceManager` (`tools/mf-run/src/MagnaFlow.MfRun/Runtime/ServiceManager.cs`)**

- The PID-reuse guard (`EvaluateEntry`) now compares recorded vs. observed process start time with
  a ±2s tolerance (`PidReuseTolerance`) instead of exact tick equality, and returns a reason
  string whenever a service is not tracked: `no-pid-file`, `process-gone`, or
  `starttime-mismatch: recorded <ts>, observed <ts>`.
- `Status` became `StatusAsync`, exposing `Reason` (set only when `Running` is false) and
  `PortListening` (`true`/`false`, or `null` when the service has no `url`) on
  `ServiceStatusEntry`. Both are additive, nullable fields — `StatusJson`'s existing
  `WhenWritingNull` option omits them automatically when unset, so `--json` stays backward
  compatible.
- `StartOneAsync`'s stale-pid-file cleanup and `StopOne`'s stale-pid-file detection both now log
  the reason inline (`"stale pid file removed (<reason>)"`) instead of a fixed string — a deleted
  pid file is never silent.
- Added `IPortProbe`/`TcpPortProbe` (`Infrastructure/IPortProbe.cs`): a plain TCP connect attempt
  against `127.0.0.1:<port>` with a 300ms timeout, no new dependency. Port is derived from the
  service's `url` via `Uri.TryCreate`.

**`Program.cs`**: wires `TcpPortProbe` into `ServiceManager`; the human-readable `status` line now
shows `[<reason>]` when stopped and `(port listening)`/`(port not listening)` when a port was
probed.

**Tests**: `ServiceManagerTests.cs` gained a tolerance-boundary theory (0ms/500ms/2000ms tolerated,
2001ms not), reason-string coverage for all three codes, port-probe coverage (listening,
not-listening, no-url), and a stale-pid-file-logs-reason case. `TestSupport.cs` gained
`FakePortProbe`. New `PortProbeTests.cs` exercises the real `TcpPortProbe` against an actual
`TcpListener`. `StatusJsonTests.cs` gained serialize/omit cases for `reason`/`portListening`. All
46 mf-run tests pass.

**`tools/mf-run/README.md`**: documented the tolerance, the three reason codes, and the new
"Status semantics" section explaining tracked vs. port-listening as two independent facts.

**mf-cockpit**: `RunServiceStatusDto` (`Models/Dtos.cs`) gained the same two additive nullable
fields, deserialized case-insensitively from mf-run's JSON (no change needed to `RunEndpoints.cs`,
which already passes `mf-run`'s JSON straight through). `project.html`'s Run card (`runState()`)
now combines `running`+`portListening` into the four card states from the spec (running / running
(not started by mf-run) / process up, port not answering / stopped), and appends the reason string
to the status text whenever present. Added a `.run-dot.warning` (amber) style for the
process-up-port-silent state. All 131 mf-cockpit tests pass unaffected (the E2E stub's JSON output
has no `reason`/`portListening` fields, which the additive contract tolerates).

## Decisions

- Kept `Reason` as a single string (short code, or the code plus embedded timestamps for
  `starttime-mismatch`) rather than a separate code + detail pair — the cockpit card only ever
  needs to display it verbatim, and CLI/JSON consumers can still prefix-match the code.
- `PortListening` is `bool?`, not `bool`, so "no url configured" (nothing to probe) stays distinct
  from "probed and got nothing" — the card's combination logic depends on that three-way split.
- Did not add a tolerance to the separate immediate-liveness recheck in `StartOneAsync` (the
  `afterWait.StartTimeUtc != justSpawned.StartTimeUtc` check ~2s after spawn) — that compares two
  live OS snapshots directly, not a value round-tripped through the PID file's text, so it isn't
  the guard the task scoped the tolerance to.
- `TcpPortProbe`'s 300ms connect timeout is a local, in-process constant (not configurable) — KISS,
  and status is already an interactive-latency operation per the existing ~2s start liveness wait.

## Scope

`tools/mf-run/` (ServiceManager, Program, README, tests) and `tools/mf-cockpit/` (Dtos.cs,
project.html, style.css). No dependency additions; no change to start/stop exit-code semantics.
