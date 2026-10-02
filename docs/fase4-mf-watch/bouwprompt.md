# Build prompt — mf-watch v0.1

Copy-paste the block below into Claude Code, from the repo root.

---

Build `tools/mf-watch/` exactly per the design in
`docs/fase4-mf-watch/ontwerp-v0.1.md`. Read that file first; it is
the spec. Also read `tools/worker-controller/` to match its
conventions (project layout, config handling, test style, .NET 10).

Summary of what you're building: a small cross-platform C# console
daemon that polls `docs/prompts/` for cmd files with
`status: ready`, spawns the `mf-worker` binary per hit (sequentially,
one at a time), optionally does `git pull` before / `git push` after
(`git_sync` config), notifies the human via a configurable
`notify_command` shell template when a run finishes, raises
questions, or errors, and sleeps with adaptive backoff
(`interval_min` / `interval_max` / `idle_grace` — stay at min until
idle_grace passes with no activity, then double per empty poll to
max, reset on any activity).

Requirements beyond the design doc:

- Unit tests in the same style and framework as the worker
  controller's, with fakes for git, process spawning, and the clock
  (the backoff logic must be testable without sleeping).
- A `--once` flag: one poll cycle, then exit — use it for end-to-end
  validation against a disposable project with a stub worker command
  (same substitution mechanism the worker controller's own validation
  uses), covering: ready→run→done, empty poll, lockfile refusal of a
  second instance, and git_sync pull/push.
- Config file next to the binary or via `--config`, YAML, same
  parsing approach as the worker controller.
- Log to console and append to `.magnaflow/mf-watch.log`.
- No UI, no tray, no library reference to the worker controller —
  process spawn only.
- If anything in the design doc is ambiguous or contradicts what you
  find in `tools/worker-controller/`, stop and ask rather than guess.

When done, write completion notes to
`docs/fase4-mf-watch/v0.1-completion-notes.md` in the same style as
`docs/fase2-worker-controller/v0.2-completion-notes.md`: what
shipped, validation performed, deviations, left for later.
