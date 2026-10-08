---
date: 2026-07-04
topic: specs, mf-spec
status: accepted
---

# 0003 — Own spec system v2 — spec-first native, less friction

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

The strongest argument for state specs (NxtPhase talk, June 2026): they
are a compact projection of the code that serves as the AI's memory.
Measured on a real project: code ≈ 443k tokens, specs ≈ 19k — more than
20× smaller. One source, three uses: memory for the AI, in-app help for
users, documentation for the team. Delta-heavy systems (Spec Kit,
OpenSpec) were retired for that reason (`0002-spec-strategy.md`).

v1 of our system was built for a world where code was the truth:
`/spec` updates specs from the code, `/spec-drift` routinely hunts
differences — manual work you must remember. It also hardcoded Next.js
(`pages/` mirrors `app/`), and was distributed by copying a folder, so
improvements flowed back by hand. Help was single-language, while
Wozzol ships in six.

## Options

- Keep v1 and add discipline, or reverse the direction: **spec-first
  native**, where the spec changes before or with the code and the
  routine actions are replaced by safety nets that run themselves.
- For non-Next.js projects: an adapter framework per stack, or a
  minimal **surface** declaration (a glob plus help settings).
- For distribution: keep copying, or a kit plus installer on the
  GitHub Spec Kit model.

## Measurement

Less friction for a solo developer: nothing to remember to run. Cheap
deterministic checks flag; AI judgement runs only over what was
flagged, never when nothing is wrong. Tooling needs only a glob per
surface; grounding intelligence is the AI's job anyway. Coexistence
with GitHub Spec Kit in the same repo.

## Choice

- **Spec-first native.** Updating the spec is part of every change,
  carried by a skill and a CLAUDE.md rule, including loose follow-up
  remarks. `/spec` remains only as a one-time draft from code for new
  pages and brownfield onboarding. Carried over from v1: the
  Help/`# Technical` split, pages/concepts with path mirroring, git
  dates, symbol names, anchor slugs, `BUG:`. Enforcement was planned as
  a pre-commit gate plus an automatic CI/weekly drift run, and later
  revised to a local lint and a dated STATUS.md; see `KIT.md`
  ("Deliberately not in the kit") and `0016-genres-and-gate.md`.
- **Surfaces**, with the adapter deliberately almost nothing. A spec
  unit needs a stable id, a way to enumerate expected units and a
  mapping to owning code; `concepts/` with its `Code:` line is the
  universal fallback for anything without a route.
- **Language**: specs and system output English; conversation free.
  The Help section is canonical in the surface's `help_language`;
  translations are generated, hash-tracked derivatives, never
  hand-edited. Existing hand-written help (e.g. in a database) is input
  to the initial sync, after which the direction reverses: the spec is
  canonical and the database copy is pushed by a sync step — never two
  hand-maintained copies. Not yet built and not in any spec: storage as
  a parallel tree (`docs/help/<lang>/…`), automatic re-translation on
  change, a per-language glossary fed into every run, and per-language
  build assets with locale fallback to English (UI microcopy stays
  framework i18n, sharing the glossary).
- **Distribution**: development home `tools/mf-spec/`; phase 0 copy +
  adopt prompt, phase 1 `mf-spec init`/`update`, where update's value is
  a recorded version that refreshes unchanged files and flags locally
  customized ones instead of overwriting. Installation order must never
  matter — learned on ProjectA, where installing our system before Spec
  Kit silently skipped a hook. Built with our own default flow, not
  Spec Kit.
- **Name**: `mf-spec`, following the tool convention (`mf-worker`);
  avoid "SpecFlow" (an existing .NET BDD tool) and confusion with
  GitHub Spec Kit.

First proof: kit v0 adopted cleanly in hello-website (2026-07-04), and
"add contact page" produced its page spec in the same turn.

Current behavior: `tools/mf-spec/system.md`, `tools/mf-spec/README.md`,
`tools/mf-spec/spec-kit/KIT.md`, specs/README.md.
