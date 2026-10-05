
## Question (round 1)

**How far should step 1 go, given that a terminal Ctrl+C reaches the
worker directly through the process group?** The full question with
options (A) and (B) is in `0019-pln-watch-bug-fixes.md`, "Open
questions".

**Answer**: (A). Do only the prescribed token separation in this run,
plus the second-Ctrl+C log line and the tests from the plan. Keep a
narrowed BUG line in `docs/specs/watch/mf-watch.md` "Shutdown": a
foreground Ctrl+C still reaches mf-worker and its agent through the
terminal's process group. Do not write `0019B` yourself; mention it as
the open follow-up in the rst. The watcher on the worker machine runs
under systemd, where this does not arise, so detaching the worker (B) is
not worth a second spawn path per OS now.

Step 2 as planned: `FileShare.None` on non-Windows, `FileShare.Read` on
Windows, the OS-aware tests and the spec updates.

## Question (round 2)

(A) Do only the prescribed token separation in this run. Keep a

**Answer**: Not a separate question. This is a fragment of round 1's
option (A); see the answer there.

## Question (round 3)

(B) Also detach the worker from the console in this run. On Unix,

**Answer**: Not a separate question. This is a fragment of round 1's
option (B), which was not chosen; see round 1.
