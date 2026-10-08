# 0001 — Adopt the spec system in this repo

The `docs/spec-kit/` folder next to this prompt is a copy of the spec
kit (1.3, spec-first): one markdown spec per page grouped per surface,
`concepts/` for cross-cutting logic (always including `design.md`),
split by a `# Technical` marker into user help and developer notes,
guarded by a spec-first rule, a drift audit and a format lint. Install
it in this repo and bring the specs to complete.

Steps:

1. **Derive the surface config from the repo.** Analyze the tree:
   which frameworks, which route patterns (e.g. `app/**/page.tsx`,
   Angular route files, static `*.html`), whether there is user-facing
   help and in which language(s). Draft `docs/specs/config.yml` from
   that (template with examples: `spec-kit/docs/specs/config.yml`) and
   **verify each glob by listing its actual matches**. Then show the
   draft to the user for confirmation — surface names, `help` /
   `help_language` and anything you could not infer are their call.
   Only continue after confirmation. If the project has no code yet,
   there is nothing to derive: leave `surfaces: {}`, still do steps 2–6,
   then skip step 7 — configure real surfaces the first time code
   lands, per the deepen-on-touch rule.

   If the repo already has specs in an older layout (`docs/specs/pages/`
   from a v1 install): declare the surfaces, then move each spec file
   under its surface folder following the `slug` rule, in one commit,
   before anything else. The lint reports any folder that is not a
   declared surface.

2. **Place the files.**
   - `spec-kit/docs/specs/README.md` → `docs/specs/README.md`
   - `spec-kit/docs/specs/config.yml` → `docs/specs/config.yml`
   - `spec-kit/commands/*` → `.claude/commands/`
   - `spec-kit/skills/specs/SKILL.md` → `.claude/skills/specs/SKILL.md`
   - `spec-kit/skills/brainstorm/`, `skills/architect/` and
     `skills/cc-review/` (`SKILL.md` each) → `.claude/skills/<name>/` —
     idea sparring before design (`/brainstorm`, writes only to the
     git-ignored `.scratch/brainstorm/`; add `.scratch/` to
     `.gitignore`), the design role (`/architect`) and the review of an
     executed cmd. Starting points:
     tune them to the project (its constraints, where the app runs, how
     to exercise it); after that they belong to the project.
   - `spec-kit/scripts/spec_lint.mjs` → `scripts/spec_lint.mjs` (Node,
     no dependencies; `/spec-drift` runs it for the "Format problems"
     section)
   - `spec-kit/docs/CLAUDE.md` → `docs/CLAUDE.md` (design-plane guide;
     merge if the repo already has one)
   - `spec-kit/docs/decisions/README.md` → `docs/decisions/README.md`
     and `spec-kit/docs/context/README.md` → `docs/context/README.md` —
     the two record genres. Nothing else goes in: no template, no
     example content; the README is the whole convention and may stand
     alone forever.
   - create an empty `docs/prompts/` (with a `.gitkeep`) — the delta
     lane. Make sure no `.gitignore` rule excludes it, `.claude/`, or
     `docs/specs/`: the prompt archive is the only record of *what was
     asked for*, STATUS.md records only the outcome.

3. **Merge `spec-kit/CLAUDE-section.md`** into this repo's CLAUDE.md
   (create one if missing). Don't duplicate anything it already says.

4. **Pin line endings.** Create `.gitattributes` (or add to it) with
   `*.md text eol=lf`, `*.mjs text eol=lf` and `*.yml text eol=lf`. With
   `core.autocrlf=true` — the Git for Windows default — git stores LF
   but checks files out as CRLF, so the docs and scripts differ between
   a Windows and a Linux checkout, and every fresh clone re-introduces
   the difference without anyone touching a file. Pinning removes that
   for the files the kit reads and writes.

   Do **not** normalise source code that has been CRLF for years — that
   rewrites hundreds of files for no gain. Pin the docs and the scripts,
   leave the rest, and say so in a comment in the file.

   `.gitattributes` only takes effect on checkout, and
   `git add --renormalize .` fixes just the index. With a **clean
   working tree**, force the re-checkout:
   `git rm --cached -r . && git reset --hard`. If the blobs were already
   LF this produces no diff at all — the fix is to the working tree, not
   the history.

5. **MagnaFlow?** Ask one question: does this project run with the
   MagnaFlow tooling (mf-worker, mf-cockpit)? If not, skip this step
   entirely — the spec system does not depend on it. If yes:

   5a. **Worker config.** If `.magnaflow/config.yml` does not exist, or
   exists only as a commented-out placeholder with no active `build:`/
   `test:` keys (the cockpit's "Add project" scaffold leaves one), fill
   it in with this repo's ACTUAL build and test commands (derive them
   from the project; verify each runs). If the project has no code yet,
   leave the stub as-is and note it in the summary. Otherwise, do not
   overwrite an existing file's values.

   Single-stack repo:

   ```yaml
   build:
     command: <build command>       # required (or use commands:)
   test:
     command: <test command>        # required (or use commands:)
   defaults:
     max_attempts: 3                # optional, default 3
     command_timeout_minutes: 30    # optional, default 30
   agent:
     command: claude                # optional, default claude
     args: []                       # optional
   ```

   Monorepo (multiple stacks): use `commands:` — a list, run
   sequentially, first failure stops:

   ```yaml
   build:
     commands:
       - dotnet build web/Solution.sln
       - npm --prefix frontend run build
   test:
     commands:
       - dotnet test web/Tests/Tests.csproj
   ```

   Only include commands that actually pass today — a permanently red
   command makes every task fail. If a stack's tests are broken, leave
   them out and note it in the summary.

   If the project has its own long-running dev/debug process (a web
   server, a background service — something mf-run should start/stop
   around a worker run), also add a `run:` section with one service per
   such process, using the project's actual built executable and args —
   verify the build output path exists (build first if needed):

   ```yaml
   run:
     services:
       - name: web
         command: src/MyApp/bin/Debug/net10.0/MyApp.exe   # the built exe, not `dotnet run`
         args: ["--urls", "http://0.0.0.0:5000"]           # optional
         workdir: src/MyApp                                 # optional, default: project root
   ```

   A library or CLI-only project has nothing for mf-run to manage —
   leave `run:` out entirely; that's the normal case, not a gap.

   5b. **Runtime ignore lines.** Ensure these two lines are in this
   repo's `.gitignore`, in this order (create the file if it doesn't
   exist; add only what is missing):

   ```gitignore
   .magnaflow/*
   !.magnaflow/config.yml
   ```

   Everything under `.magnaflow/` is machine-local: the watcher's
   log/lock, mf-run's `run/` PID files, the worker's `<id>/` logs, and
   `<id>/session.yml`. A session id is an agent transcript handle bound
   to one machine *and* one checkout path, so committing it does not
   share anything — on a second machine it resolves fine and then fails
   inside the agent, which is worse than the clean "no prior session,
   start fresh" you get when the file simply isn't there. Raw agent logs
   are megabytes of unreviewable output; the committed audit trail is
   the `rst-` file's `summary:`. `config.yml` is the exception: it is
   project-level worker config and travels in git, hence the negation.

   Ignored, not merely untracked: left untracked these files dirty the
   very tree mf-watch polls, and the worker's dirty-tree guard then
   blocks every run — a self-inflicted deadlock.

   Adopting a repo that already committed `.magnaflow/<id>/` evidence
   (an earlier kit version)? Untrack it once, in its own commit:
   `git rm -r --cached .magnaflow` followed by
   `git add .magnaflow/config.yml`.

6. **Quality baseline.** Check what the repo already has to catch
   problems mechanically:
   - warnings as errors and analyzers — .NET: `TreatWarningsAsErrors`
     and `AnalysisLevel`, ideally once in `Directory.Build.props`;
   - a linter — Angular/JS: eslint;
   - a dependency vulnerability scan in CI — `dotnet list package
     --vulnerable`, `npm audit`.

   Then ask the user one question: **set it up, or advise only?** The
   user decides. Set up means only what keeps the build green today: if
   switching something on would turn the build red, do not fix the
   warnings here — leave it off, note the warning count, and suggest a
   separate cmd for it.

   Either way, record the outcome in a short `## Quality baseline`
   section of `docs/specs/concepts/design.md`: what is on, what was
   advised (with the count where one was taken). Then the question is
   asked once, and `0002` can see it was answered. If design.md does not
   exist yet, create it with `# Technical` as its only h1 and just this
   section; step 7 fills in the rest. A project without code yet records
   the advice only.

7. **Write the initial specs (brownfield sync).** Skip this step
   entirely if step 1 found no code yet. Otherwise: if the project has
   existing hand-written help content (e.g. in a database), ask the
   user to export it to files first and use it as source material for
   the Help sections — it captures intent the code cannot show, and its
   wording is known to users, so prefer it over freshly generated text.
   Every generated spec ends its `# Technical` section with
   `DRAFT: generated from code, not human-reviewed.` (removed later, the
   first time a human works with that spec).
   Run `/spec-drift` to get the full missing list. **Calibrate first**:
   write 2–3 specs, show them to the user, and adjust tone/length/level
   of detail to their feedback before writing the rest. Drafting a whole
   repo from code is where specs bloat: hold each `# Technical` to what
   the code cannot say (see `/spec` step 3), under ~800 words. Then work
   through the missing list in batches (~10 units), committing per batch
   so the sync is restartable — pages must be complete per surface.
   Write `docs/specs/_overview.md` (the application as a whole) and
   `docs/specs/concepts/design.md`, the design system, in two halves:
   - **engineering principles** — architecture and layering, dependency
     policy, security basics (secrets never in code, parameterised
     queries), reuse before writing new code. Derive the principles the
     code already follows, show them to the user and let them confirm or
     adjust: a handful of short rules, not a handbook. `/architect`
     checks every cmd against design.md, so a principle written there
     steers every change.
   - **appearance**, when there is a UI — layout rules, components,
     tone. Page specs reference it instead of describing appearance.

   Keep the Quality baseline section from step 6. Add a concept for
   every mechanism that determines behavior and cannot be read off one
   module.

8. **Verify.** Finish with another `/spec-drift`; STATUS.md must be
   clean (all sections "none" except Recent spec updates), "Format
   problems" included.

When done, delete the `docs/spec-kit/` copy and any other copy of the
kit in the repo (a folder holding `KIT.md`; the master lives in the
magnaflow repo under `tools/mf-spec/spec-kit/`) and show a summary: where
each file went, the surfaces configured, whether the MagnaFlow step ran,
the quality baseline outcome, and the final STATUS.md.
