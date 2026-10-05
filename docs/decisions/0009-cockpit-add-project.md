---
date: 2026-07-14
topic: cockpit, onboarding
status: accepted
---

# 0009 — mf-cockpit v0.4 — design: Add project (2026-07-14)

> Migrated from `docs/decisions/0009-cockpit-add-project.md` on 2026-10-05. Pre-dates the four-section
> decision format; original text unchanged.

A plus button on `index.html`. Goal in one sentence: from the
cockpit you can add a project — an existing working copy, or a
brand-new one that the cockpit scaffolds (git repo, spec kit,
optionally a code template) and registers, so the first spec-kit
command is a draft in the lane before you've opened a terminal.
Extends `docs/decisions/0007-cockpit-design.md`; its invariant still
governs, with one note below. This design assumes fase 7 (the
machine-config merge) — which has landed.

## Invariant note (write #6, and the machine config)

v0.3 scoped machine-config editing out as "self-surgery" — that
exclusion targeted a *raw editor* for a file that may belong to a
different machine than the browser's. Write #6 is different in
kind: a project directory the cockpit creates necessarily lives on
the cockpit's own machine, and registering it means appending one
schema-known entry to the `cockpit: projects:` list of the config
file this process actually loaded. A narrow, structured append is
not an editor. Everything else in the v0.3 exclusion stands (the
raw machine-config editor remains out of scope).

One consequence to be honest about: `magnaflow.yml` is **not in
git**. For writes #1–#5, git is the undo; here it cannot be.
Compensating guards below ("Writing magnaflow.yml").

## The dialog (`index.html`)

A `+` next to the Projects header (and the empty state's call to
action — with zero projects this button *is* the cockpit's front
door). A modal with two modes:

- **Existing** — name + path (absolute, or relative to
  `new_project.root` when configured). Always available.
- **New** — name, a template dropdown (the configured templates
  plus a built-in "empty"), and a live preview of the computed
  target path (`<root>\<sanitized-name>`). Available only when
  `new_project.root` is configured; otherwise the tab explains
  what to add to `magnaflow.yml` instead of half-working.

`GET /api/new-project` feeds the dialog:
`{root?, templates: [names], specKit: bool}` — names only; the
template definitions themselves never leave the server.

## Machine config: `cockpit.new_project`

```yaml
cockpit:
  new_project:
    root: C:\GIT                # parent dir for new projects
    spec_kit: C:\GIT\magnaflow\tools\mf-spec\spec-kit
    templates:
      - name: dotnet-scaffold   # external generator
        type: command
        command: dotnet
        args: ["C:\\tools\\scaffolder.dll", "--cmd", "{target}"]
        timeout_seconds: 300    # default 300
      - name: basic-site        # plain directory copy
        type: copy
        source: C:\GIT\templates\basic-site
```

Two template kinds, because both already exist in practice:

- `copy` — recursive copy of `source`'s *contents* into the new
  project dir, skipping a top-level `.git` if the source happens
  to be a working copy.
- `command` — spawn `command args...` with `{target}` (absolute
  new-project path) and `{name}` substituted in args, cwd set to
  the target dir, a hard timeout (kill + report, same posture as
  the mf-run spawns in v0.3). Exit code ≠ 0 is a failure.

Everything here is machine config, which is exactly right: paths
and installed generators are per-machine by design (the fase-7
config-landscape principle). Note the security shape this buys:
the browser only ever sends a template *name*; the command and
args are resolved server-side from operator-authored config —
nothing from the request is ever part of a spawned command line.

## Creating a project (`POST /api/projects` — write #6)

Body `{mode: "new"|"existing", name, path?, template?}`. For
`new`, the steps in order — the order is the design:

1. **Validate**: name required and unique among registered
   projects; sanitized dirname non-empty and not a Windows
   reserved device name (`CON`, `NUL`, …); target dir must not
   exist; template name (if given) must exist in config.
   Sanitization: trim, strip characters invalid in Windows file
   names, spaces → `-`, collapse repeats. The dialog previews the
   result; there is no override field (rename by hand later if it
   matters — KISS).
2. **mkdir** the target.
3. **Code template**, if one was chosen (copy or command, above).
4. **Spec kit**: copy the configured `spec_kit` dir into
   `<target>/docs/spec-kit/` — the adoption layout mf-spec's own
   README prescribes. No `spec_kit` configured → skip, with a
   warning in the response.
5. **Hygiene files**, only where the template didn't already
   provide them: `.gitignore` gets the runtime ignore lines
   (`.magnaflow/mf-watch.log`, `.magnaflow/mf-watch.lock`,
   `.magnaflow/run/` — the mf-spec + fase-6 set), and a minimal
   commented `.magnaflow/config.yml` stub so the worker's needs
   are visible from day one.
6. **git init** — skipped when the template already produced a
   `.git` (a generator may clone); then `git add -A` + initial
   commit `cockpit: create project <name>`.
7. **Seed the first command**: a lane *draft* (the existing
   write-#1 code path, no new mechanism) whose content points at
   `docs/spec-kit/0001-adopt-spec-system.md` per mf-spec's own
   adoption flow. A draft is inert — the human gate (draft →
   ready) stays exactly where it has always been. This is the
   "start immediately" moment without anything auto-running.
8. **Register** — append to `cockpit: projects:` in
   `magnaflow.yml` (below) and to the in-memory registry in the
   same request, so the project is served without a restart.
9. SSE: a new coarse kind `projects`, so an open `index.html`
   refreshes its list — same debounce, no payloads, as ever.

Registration is deliberately **last**: a failed scaffold never
leaves a phantom entry. Scaffold failures (template exit code,
timeout, copy error) return the captured output and **leave the
directory in place** for diagnosis — the human deletes or fixes
it; the response says so. The one awkward inverse — scaffold
succeeded, config append failed — is self-healing: the directory
is complete and valid, so "Existing" mode registers it.

`mode: "existing"` is steps 1, 8, 9 only, with path validation
instead of scaffolding: the path must exist and be a directory;
duplicate name *or* path → 409; a missing `.git` is a returned
warning, never a rejection (the git card degrades visibly, which
is the honest signal).

## Writing magnaflow.yml

- The fase-7 loader learns to expose its **resolved path**
  (`CockpitConfig.Load` already returns `(Config, Error, Notice)`;
  the path it settled on joins that tuple).
- Three cases: found a sectioned file → append there. Found the
  *legacy* filename (`mf-cockpit.yml`) → **refuse** with a clear
  message; the deprecation path doesn't grow new features. Found
  nothing (running on defaults) → create `magnaflow.yml` in the
  user config dir (`%APPDATA%\MagnaFlow` / `~/.config/magnaflow`)
  — the next-to-binary location may not be writable at all.
- The append is a **text edit**, not a parse-and-regenerate — a
  regenerated file destroys comments and ordering, the same
  reasoning that made write #5 a raw editor. Supported shapes: an
  existing `projects:` block list (append an entry), an inline
  `projects: []` (replace with a block list), a `cockpit:` section
  without the key (insert it), no `cockpit:` section (append one).
- Because git is not the undo here: write `magnaflow.yml.bak`
  first, apply the edit, **re-parse the result** and verify the
  new entry round-trips; on any failure restore the `.bak` and
  return 500. A server-side single-writer lock serializes
  concurrent adds.

## Guards

- All of write #6 sits behind the same bind/auth stance as ever
  (VPN/LAN is the perimeter) — but note it now covers *creating
  directories and running configured programs*, so binding beyond
  localhost is even more explicitly the operator's call. The
  request can only choose among operator-defined templates and a
  name; it cannot supply paths outside `new_project.root` in
  `new` mode, nor any command text ever.
- Template spawns: hard timeout, tree-kill on expiry, captured
  output in the response (a failed scaffold's output is exactly
  what you want to read right there).
- The endpoint holds one lock across scaffold + register, so two
  simultaneous adds cannot interleave on the config file.

## Not in scope (v0.4)

Registering the project with **mf-watch** (`watch:` section,
possibly a different machine — the project page's watcher line
already shows the silence, which is the prompt to go configure
the worker); a template-management UI (`magnaflow.yml` in an
editor *is* the management UI); delete/rename/unregister from the
cockpit; a dirname override field; auto-running the adopt prompt
(the seeded draft is the whole gesture); the raw machine-config
editor (still self-surgery, per v0.3).
