# Build prompt — spec-kit & docs audit + root README

Copy-paste the block below into Claude Code, from the repo root.

---

Audit the spec kit and the "as it is now" documentation for drift
against the last three landed changes — fase 6 (mf-run), fase 7
(machine config `magnaflow.yml`), and cockpit v0.4 (Add project) —
then write the repo's root `README.md`. Read first:
`docs/mf-spec/README.md` + `docs/mf-spec/system.md`,
`tools/mf-spec/spec-kit/` (every file — the kit is the normative
source for target repos), `docs/fase6-mf-run/v0.1-completion-notes.md`,
`docs/fase7-machine-config/completion-notes.md`, and
`docs/fase5-cockpit/v0.4-completion-notes.md` (especially the
scaffold step: what the cockpit now pre-creates in a new project).

Scope rule: fix only documents that describe the system **as it is
now** — `docs/mf-spec/*`, `docs/kennis/*`, the kit itself, and the
new root README. The `docs/fase*/` folders are history; never
rewrite them. Docs-audit posture mirrors the fase-6 audit: verify
each suspected drift against code or completion notes before
editing — no speculative rewording.

## 1. Spec kit vs. the last three fases

Check `tools/mf-spec/spec-kit/` for each of these, and fix what's
actually wrong:

- **Fase 6**: the `.gitignore` guidance must carry all three
  runtime-ignore lines (`.magnaflow/mf-watch.log`,
  `.magnaflow/mf-watch.lock`, `.magnaflow/run/`). The completion
  notes say the kit was updated for this — verify it actually was.
- **Fase 7**: any mention of `mf-watch.yml` / `mf-cockpit.yml` as
  *current* filenames is stale — the machine config is one
  `magnaflow.yml` (`watch:` / `cockpit:` sections); the old names
  are a deprecated fallback. (Machine config is largely outside
  the kit's domain — flag only where the kit actively names the
  old files.)
- **Cockpit v0.4**: a target repo can now receive the kit two
  ways — the manual copy the kit's own instructions describe, or
  the cockpit's "Add project" scaffold, which pre-copies the kit
  into `docs/spec-kit/`, pre-adds the `.gitignore` lines, drops a
  minimal commented `.magnaflow/config.yml` stub, and seeds
  `0001-adopt-spec-system.md` as a lane draft.
  `0001-adopt-spec-system.md` must therefore be **idempotent
  against a pre-scaffolded repo**: tolerate an already-present
  `docs/spec-kit/`, merge rather than overwrite an existing
  `.gitignore` and `.magnaflow/config.yml` stub, and not assume
  the human pasted it by hand. Where its instructions assume a
  brownfield repo with existing code, make sure a brand-new empty
  project (the v0.4 case) also has a sane path.

## 2. Docs that claim current state

- **Known concrete finding, fix it**: `docs/mf-spec/README.md`'s
  adoption step lists only `.magnaflow/mf-watch.log` and
  `.magnaflow/mf-watch.lock` as the gitignore lines —
  `.magnaflow/run/` is missing (`system.md` already has all
  three).
- `docs/mf-spec/README.md` "Use it in a project": add the cockpit
  route as the second way in (one or two sentences — the manual
  copy stays documented; the cockpit path lands you at "mark the
  seeded draft ready").
- Sweep `docs/mf-spec/system.md` and `docs/kennis/*.md` for
  anything the three fases made stale (old config filenames,
  "cockpit can't do X" claims that v0.3/v0.4 made untrue). Verify
  before changing; leave accurate text alone.

## 3. Root `README.md` (new)

Write `README.md` at the repo root — a **map, not a manual**, one
screen-ish, in English like the rest of the docs:

- What MagnaFlow is, in a few sentences (spec-first, git-native
  AI development: specs are the AI's memory, commands flow
  through a git lane, a worker executes, humans gate).
- The tools, one line each: mf-worker/controller, mf-spec (+ the
  spec kit), mf-watch, mf-cockpit, mf-run.
- The two config axes, two lines: per project
  `.magnaflow/config.yml` (shared by worker + mf-run; specs have
  `docs/specs/config.yml`), per machine `magnaflow.yml`
  (`watch:` / `cockpit:` sections).
- The docs layout: `docs/fase*/` = history and rationale,
  `docs/mf-spec/` = the system as it is now, `docs/kennis/` =
  background notes, `docs/prompts/` = this repo's own delta lane.
- Where to start: run the cockpit, add a project via `+`, or
  adopt the kit manually per `docs/mf-spec/README.md`. Link,
  don't duplicate — every claim in this README must be a pointer
  or a one-liner that can't drift easily.

## Requirements

- No behavior changes anywhere — this is a docs/kit-only change;
  if a kit fix would genuinely alter worker or watcher behavior,
  stop and ask instead.
- If a "drift" turns out to be correct on inspection, leave it
  and record why it was suspected and why it stands.
- When done, write brief notes to
  `docs/mf-spec/docs-audit-notes.md` (same pattern as
  `docs/fase6-mf-run/argumentlist-audit-notes.md`): what was
  checked, what was fixed, what was verified-and-left, anything
  flagged for a human decision. Commit everything.
