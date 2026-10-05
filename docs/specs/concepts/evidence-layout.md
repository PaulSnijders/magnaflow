# Technical

`.magnaflow/` at the project root is the runtime plane: what tools write
while they work, as opposed to the lane in `docs/prompts/`, which is
what they report. This file pins the worker's part, `.magnaflow/<id>/`,
and what its readers rely on.

```text
.magnaflow/
  config.yml           # project config: committed (see machine-config)
  <id>/                # one per command that has run; <id> = NNNN-name
    claude.log         # raw agent output, plan phase + every attempt
    build.log          # build command output per attempt
    test.log           # test command output per attempt
    session.yml        # the agent session the last run ended with
  mf-watch.log, mf-watch.lock, mf-watch.wake   # mf-watch (watch-supervision)
  run/                 # mf-run PID files and service logs (mf-run)
```

## Logs

- Plain UTF-8 text without a BOM. Files are **appended** across runs and
  never truncated, so one file holds a command's whole history,
  including runs before and after a questions pause.
- Section headers, one line each, timestamps in local time without an
  offset:
  - plan phase in `claude.log`: `=== plan — yyyy-MM-ddTHH:mm:ss ===`
  - each implementation attempt, in all three logs:
    `=== attempt N/M — yyyy-MM-ddTHH:mm:ss ===`
- `claude.log` holds the agent's raw `stream-json` lines verbatim. The
  build and test logs hold the merged output of every command in that
  section.
- The writers are flushed and closed before any staging. An open log can
  look empty to git.

## session.yml

```yaml
session: <agent session id>
```

Written at the end of every run that has a session id (pause or
terminal outcome), overwriting the previous one. Nothing is written when
the agent never reported one. Readers treat a missing file, a missing key
or a blank value as "no session". See [session resume](session-resume.md).

## Why machine-local

The whole folder except `config.yml` is gitignored, as `.magnaflow/*`
plus `!.magnaflow/config.yml`. Reasons:

- Logs are megabytes of output no one reviews. The committed audit
  trail is the rst and its `summary:` line.
- Session ids only resolve on the machine, and in the checkout path,
  that ran them, so committing them would point other machines at
  nothing.
- Untracked but not ignored, they would dirty the tree after every run.
  The worker's dirty-tree guard would then refuse the next run, and
  mf-watch would stall on it.

## What the worker commits

Before each outcome commit, the worker stages `.magnaflow/<id>/` only
when the folder exists and the project does not ignore it
(`git check-ignore`). Both cases are normal, so neither can cost the run
its commit. A project that still tracks `.magnaflow/` gets its evidence
committed as before. The work commit always excludes `.magnaflow/`.

## Readers rely on

- **Cockpit** (`EvidenceReader`) is read-only and never writes here. It
  tails the three logs, reads `session.yml`, and computes duration from
  the timestamp in `claude.log`'s **first line** to the latest write of
  the three logs, or to now while `running`. Because logs append, a
  re-run command's duration spans from its very first run, including
  any questions pause. The first-line header format is a contract: a
  first line that does not match the header gives no duration.
- **Cockpit follow-up** reads the parent's `session.yml` to fill the new
  draft's `resume:` with a raw session id.
- **Worker** reads `session.yml` for self-resume and for `resume:`
  pointing at a command id. The existence of `.magnaflow/<id>/` is what
  "this command has run" means to `ResumeResolver`.

DRAFT: generated from code, not human-reviewed.

Code: tools/worker-controller/src/MagnaFlow.WorkerController/Execution/AttemptLogWriter.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Execution/SessionEvidence.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Infrastructure/GitClient.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Evidence/EvidenceReader.cs, .gitignore
Why: prompts/0017-cmd-terminal-commit-survives-evidence-staging.md
