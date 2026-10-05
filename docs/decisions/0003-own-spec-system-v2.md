---
date: 2026-07-04
topic: specs, mf-spec
status: accepted
---

# 0003 — Own spec system v2 — spec-first native, less friction

> Migrated from `docs/decisions/0003-own-spec-system-v2.md` on 2026-10-05. Pre-dates the four-section
> decision format; original text unchanged.

> Successor of the current system (copy in
> `docs/old-spec-kit-for-reference/`). Strategy and writing principles:
> `docs/decisions/0002-spec-strategy.md`. Name: still to be chosen (working title
> below). Status: design ideas, 2026-07-04.

## Why state specs: memory for the AI

The strongest argument for this system (from the NxtPhase talk, June
2026): state specs are a **compact projection of the code** that serves
as the AI's memory. Measured on a real project: code ≈ 443k tokens,
specs ≈ 19k — more than 20× smaller. Every new session starts blank and
no large project fits in context; working at spec level and diving into
code only when needed pushes that wall far back. One source, three
uses: memory for the AI, in-app help for users, documentation for the
team — and it makes projects transferable (open `docs/` and start
talking).

This is also why delta-heavy systems (GitHub Spec Kit, OpenSpec) were
evaluated and retired from the default workflow: their artifacts are
per-change scaffolding with no life after merge, while state specs
appreciate over the project's lifetime. See
`docs/decisions/0002-spec-strategy.md` for the decision and the three-tier
default flow.

## The core difference from v1

v1 was built for a world where code was the truth: `/spec` updates
specs *from the code*, `/spec-drift` routinely looks for differences.
That means manual work: you have to remember to run commands.

v2 reverses the direction (spec-first native): the spec changes first or
together with the code. Drift then almost cannot arise by
construction — so the routine actions disappear and are replaced by
**enforcement and safety nets that run by themselves**.

## Friction reducers

1. **No more standalone `/spec` actions.** Updating the spec is part of
   every change: a skill + CLAUDE.md rule ensures the agent updates the
   affected state spec in the same turn for every behavioral change
   — including loose follow-up remarks ("make that button blue").
   You no longer do anything for it yourself.

2. **Enforcement in the background, not in your head.** Pre-commit gate:
   code touched without a spec change = commit rejected. The `hotfix:` prefix
   is the escape hatch and automatically adds a debt line to STATUS.md.
   The format lint runs in the same gate.

3. **Drift check becomes an invisible safety net.** No longer run manually
   but automatically (CI and/or weekly). It should be green and
   only speaks up when red. STATUS.md thereby changes from worklist
   into exception report.

4. **Hooks instead of commands.**
   - Claude Code *SessionStart* hook: show the STATUS summary, so
     open items come into view automatically.
   - Spec Kit `extensions.yml` *after_implement* hook: the fold-in step
     (delta → state specs) automatically after every feature.

5. **New page without spec = one-time draft from code.** The
   v1 mechanism (writing a spec from code) remains for
   brownfield onboarding and new pages, but as a one-time
   generation step — after that, spec-first applies.

6. **Deterministic and small remains the foundation.** The cheap script
   (git dates, lint) flags; AI judgement (behavioral vs cosmetic) runs
   only over what was flagged. No AI in the loop when nothing is
   wrong.

## What carries over unchanged from v1

- Help/`# Technical` split: specs double as in-app help.
- Pages/concepts model with path mirroring.
- Git dates (never mtimes), symbol names (no line numbers), anchors
  with slugs (no section numbers), `BUG:` prefix.
- Short over complete — plus the new writing principle: specify
  decisions, not defaults (see spec-strategie.md).

## Surfaces: generic page referencing

v1 hardcodes Next.js (`pages/` mirrors `app/`, discovery via
`app/**/page.tsx`). v2 generalizes this with **surfaces**: a project has
one or more surfaces on which users or systems touch the product — the
app frontend, a static website, later an admin portal or an API. Each
surface gets its own subtree in the spec tree and a declaration in a
small config file:

```yaml
# docs/specs/config.yml
surfaces:
  app:                              # docs/specs/app/...
    adapter: nextjs-app-router
    root: frontend
    routes: app/**/page.tsx
    help: true                      # in-app help, multi-language
  web:                              # docs/specs/web/...
    adapter: static-pages
    root: site
    routes: "**/*.html"
    help: false                     # marketing pages need no help
```

Principles:

- Single-surface projects keep the flat layout (default surface);
  `concepts/` stays cross-surface where it is.
- A spec unit needs exactly three generic things: a stable ID (its
  path), a way to **enumerate** expected units (for "missing specs"),
  and a mapping to **owning code** (for drift and grounding). For pages
  the router provides all three via one glob + path-to-slug rule.
- **The adapter is deliberately almost nothing**: a glob and optionally
  one line of prose for the AI ("route = path without extension").
  Deterministic tooling only needs the glob; grounding intelligence is
  the AI's job anyway. No adapter framework.
- The `concepts/` mechanism with its `Code:` line is already the
  universal fallback for anything without a route (APIs, jobs, CLI
  tools); a surface with its own enumeration rule (e.g. FastAPI routes)
  can be added when a project needs it. This largely answers the open
  question "granularity beyond pages" in spec-strategie.md.
- Help behavior is declared per surface (`help:`), including its
  language set.

## Multi-language help

Decision (2026-07-04): specs and all system output are English; the
conversation language is free. Help sections are end-user content and
therefore multi-language (e.g. Wozzol ships in six languages).

- The Help section in the spec is canonical, written in the surface's
  `help_language` (default English; refined 2026-07-04 for
  single-language projects with existing hand-written help, e.g. Floater
  is Dutch-only). Translations are derived artifacts, generated from
  the canonical version — never hand-edited sources. `# Technical` is
  always English.
- Existing hand-written help (e.g. in a database) is **input to the
  initial sync**, not reconciled afterwards: export it to files once and
  feed it to the adopt run alongside the code. After adoption the
  direction reverses: the spec is canonical and the database copy
  becomes a derived artifact, pushed by a sync step — never two
  hand-maintained copies.
- Storage: a parallel tree mirroring the pages structure, e.g.
  `docs/help/<lang>/orders.md`. Only the Help section is translated;
  `# Technical` stays English.
- Staleness is tracked mechanically: each translation records a content
  hash of the canonical Help section it was generated from. The lint
  flags hash mismatches as stale — the same drift machinery, no
  judgement needed.
- Regeneration is automatic: a hook/CI step re-translates on change (AI
  translation is cheap); human review is optional per language.
- Each project keeps a small glossary per language (product terms,
  do-not-translate list) that is fed into every translation run, so
  terminology stays consistent across pages and releases.
- At build time, help compiles to per-language assets; the app resolves
  the user's locale with fallback to English. UI microcopy (buttons,
  labels) is regular framework i18n — a separate mechanism, sharing the
  glossary.

## Packaging and distribution

The GitHub Spec Kit model is proven and we copy it: the system is plain
files (skills, commands, config template, lint script, hooks) plus a
trivial installer. v1's manual model (copy folder + adopt prompt) is the
same idea without tooling.

- **Development home:** `tools/mf-spec/` in the magnaflow monorepo, next
  to `tools/worker-controller/`; the installable payload is a folder
  inside it. hello-website is the proving ground; magnaflow itself
  becomes a target repo later (dogfooding).
- **Phase 0 (now):** distribute by copying the payload folder into a
  target repo and running the adopt prompt — v1's proven pattern, no
  tooling needed for the first few repos.
- **Phase 1:** `mf-spec init` and `mf-spec update`. Init is a copier;
  the real value is **update**: a version file recorded at install time
  lets update refresh unchanged files and flag locally customized ones
  instead of overwriting — the thing manual copying cannot do, and v1's
  documented weak point ("improvements flow back" by hand).
- **Constraint:** must coexist with GitHub Spec Kit in the same repo —
  no collisions (`docs/specs/` vs their `specs/`, own skill-name
  prefix).
- **Constraint: installation order must not matter** (learned 2026-07-07
  on ProjectA: own system first, Spec Kit later → the
  `extensions.yml` hook silently never got installed). If `.specify/`
  is absent at install time, the installer must say so explicitly and
  name the follow-up action instead of skipping silently; `mf-spec
  update` closes such gaps on the next run.
- **Build route:** built with our own default flow (state specs + one
  lean delta doc per milestone), dogfooding it. Spec Kit is retired
  from the default workflow (see spec-strategie.md); its wiring was
  removed from the kit in v0.3.

## Name

To avoid: "SpecFlow" (existing .NET BDD tool), and confusion with
GitHub Spec Kit. Candidates so far: **MagnaSpex** (wink),
**MagnaSpec**, or simply follow the tool convention: `mf-spec` next to
`mf-worker` — then the system name can come later.

## Status and next step

Kit v0 exists at `tools/mf-spec/spec-kit/` (README, config.yml,
adopt prompt, /spec, /spec-drift, skill, CLAUDE section; lint,
pre-commit gate, translations and hooks deliberately deferred).

Tested in hello-website (2026-07-04): adopt ran clean; a behavior change
("add contact page") produced the page spec in the same turn —
spec-first rule works. Next: a larger brownfield project (multiple
surfaces, real volume, calibrated initial sync), then feature
`002-mf-spec` for the installer/update tooling.
