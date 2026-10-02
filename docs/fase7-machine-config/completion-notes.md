# Fase 7 — one machine config: magnaflow.yml — completion notes

## What shipped

mf-watch and mf-cockpit each still own their own config loader (no
shared library — standing decoupling rule), but both now read from
one per-machine file, `magnaflow.yml`, under their own top-level
section (`watch:` / `cockpit:`). Each tool ignores the other's
section via the `IgnoreUnmatchedProperties` posture both already had.

**Lookup order**, per tool, first hit wins:

1. `--config <path>` (mf-cockpit: or `MF_COCKPIT_CONFIG`, its
   existing test-injection env var, at the same priority slot) —
   explicit and authoritative. A missing file here is *not* a further
   lookup; it yields defaults, exactly like before fase 7 (see
   "Deviation" below).
2. `magnaflow.yml` next to the binary.
3. The user config dir — `%APPDATA%\MagnaFlow\magnaflow.yml` on
   Windows, `~/.config/magnaflow/magnaflow.yml` elsewhere.
4. The tool's old filename next to the binary (`mf-watch.yml` /
   `mf-cockpit.yml`) — read exactly as before, always with a one-line
   deprecation notice on console and (for mf-watch) in its log file.

**Format detection** is independent of *which* location produced the
file, except for step 4, which always forces old-format (flat,
unsectioned) parsing per the "read exactly as before" requirement:

- A file with a top-level `watch:` / `cockpit:` key is read as
  sectioned — no notice.
- A file without that key but with any recognizable top-level legacy
  field (e.g. `git_sync`, `interval_min_minutes` for mf-watch;
  `port`, `projects` for mf-cockpit) is read as legacy — notice fires,
  even though it was found via `magnaflow.yml`, `--config`, or the
  user config dir, not just the deprecated filename.
- A file with neither (e.g. only the *other* tool's section, or
  empty) yields all-defaults — no notice, no error.

Both `WatchConfig.Load` and `CockpitConfig.Load` now return
`(Config, Error, Notice)` instead of `(Config, Error)`. `LocatePath`
takes optional `baseDirectory`/`userConfigDirectory` overrides so
tests can point the lookup chain at temp dirs instead of the real
`AppContext.BaseDirectory` / `%APPDATA%`.

## Validation performed

- Both tools' full unit test suites are green: mf-watch 54/54,
  mf-cockpit 91/91 (`dotnet test` in each `tools/*/`).
- New unit tests per tool cover: sectioned read, legacy flat read,
  the other tool's section ignored, sectioned format winning over
  stray top-level fields, the legacy filename forcing legacy parsing
  and always noticing (even on an empty file), full lookup-order
  precedence via temp-dir overrides (`--config` wins and does not
  fall through when missing; `magnaflow.yml` next to the binary beats
  the user dir; the user dir beats the legacy filename; nothing
  anywhere yields defaults), and existing validation errors
  (interval bounds, duplicate project names, invalid YAML) still
  fire in both the flat and sectioned shapes.
- Compiled Release binaries, end-to-end:
  - mf-watch `--once` against a disposable project, once with a
    sectioned `magnaflow.yml` and once with a legacy `mf-watch.yml`
    (same `git_sync`/`worker` values in both) — identical log
    output and exit code (0) in both runs; the legacy run additionally
    printed and logged the deprecation notice, the sectioned run did
    not.
  - mf-cockpit started and served `GET /api/projects` (`[]`, as
    configured) identically against a sectioned `magnaflow.yml` and a
    legacy `mf-cockpit.yml`; the legacy run printed the deprecation
    notice, the sectioned run did not.

## Deviations

- **`--config` (and mf-cockpit's `MF_COCKPIT_CONFIG`) does not fall
  through to steps 2–4 when the given path is missing.** The
  bouwprompt's lookup-order list technically reads as one ordered
  chain with "first hit wins" across all four steps, which would
  imply a missing `--config` path falls through to auto-discovery.
  That was flagged as a genuine ambiguity and the user chose the
  authoritative interpretation instead: an explicit `--config` (or
  the cockpit's env var) is used exactly as given, and a missing file
  there yields defaults with no fallthrough — matching the behavior
  both tools already had and already had tests for. Documented here
  per the bouwprompt's own instruction to record such decisions.

## Left for later

- No migration tooling was written to auto-merge an existing
  `mf-watch.yml` + `mf-cockpit.yml` pair into one `magnaflow.yml` —
  operators currently on the old files can keep running them
  (deprecation notice only) or hand-merge at their convenience.
- The user config dir (`%APPDATA%\MagnaFlow` /
  `~/.config/magnaflow`) is new plumbing with no bundled tooling to
  create or edit it; it's a plain lookup location for a hand-placed
  file.
