---
title: "mf-run: robust + explainable status (tolerant PID guard, reasons, port probe)"
status: done
attempts: 1
created: 2026-07-14
---

## Context

The cockpit Run card is flaky in practice, two observed symptoms on a real
project (wozzol):

1. A service started via the Run card shows `running`, then flips to
   `stopped` minutes later — while its process and port may well still be
   alive. Nothing explains why.
2. A service that is demonstrably running (ng serve answering on its port)
   shows `stopped`, and its pid file has silently disappeared — consistent
   with `stop`/`start` declaring the pid file *stale* (and deleting it
   without killing anything).

Status truth today (`ServiceManager.TryGetRunningPid`) is: pid file exists
AND `GetProcess(pid)` is non-null AND the OS process start time equals the
recorded one **to the exact tick** (`snapshot.StartTimeUtc ==
entry.StartTimeUtc`). Any false mismatch in that equality produces exactly
the two symptoms above, and today it is invisible: no output ever says
*which* of the three checks failed, or what the two compared timestamps
were. Also inherently: a service started outside mf-run (dev starts ng
serve by hand) has no pid file, so it reports `stopped` even though the
port answers — true by mf-run's definition, but misleading on the card.

## Task

1. **Characterize first.** Add tests pinning current behavior of the
   PID-reuse guard, including one where the snapshot start time differs
   from the recorded one by less than a second (currently: mismatch).
2. **Tolerant PID-reuse guard.** The guard exists to catch PID *reuse* —
   a different process that got the same PID, which in practice starts
   minutes-to-days later. Compare with a small tolerance (e.g. ±2 s)
   instead of exact tick equality. That keeps the guard while eliminating
   tick-level false negatives from timestamp round-tripping/OS reporting.
3. **Explain every negative.** `status` (human and `--json`) reports per
   service *why* it is not running: `no-pid-file`, `process-gone`, or
   `starttime-mismatch` (include recorded vs observed timestamp in the
   message). `stop`/`start` log the same reason whenever they treat a pid
   file as stale — a deleted pid file must never be silent again.
4. **Port probe as a second, independent signal.** Derive the port from the
   service's `url` (when present); `status` additionally reports
   `port_listening: true|false` via a plain TCP connect attempt (no HTTP,
   no new dependencies). Truth stays two separate facts — tracked-process
   and port-listening — never merged into one guess.
5. **Cockpit Run card shows the combination.** tracked + listening →
   `running`; not tracked + listening → `running (not started by mf-run)`;
   tracked + not listening → `process up, port not answering`; neither →
   `stopped`. Show the reason string from (3) when present.
6. **Tests + README.** Cover the tolerance boundary, each reason string,
   the port probe (listener in-test), and the new card states; update
   `tools/mf-run/README.md` (status semantics + exit codes unchanged
   otherwise).

## Constraints

- Constitution applies: KISS, no new dependencies, plain text state.
- No behavior change to start/stop semantics beyond the tolerance and the
  logging; mf-run stays the sole owner of pid files.
- `--json` stays backward compatible: only additive fields.
