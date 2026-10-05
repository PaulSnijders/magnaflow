# Technical

MagnaFlow config has exactly two axes, and a value belongs to exactly
one of them:

| Axis | File | Lives | Holds |
|---|---|---|---|
| per machine | `magnaflow.yml` | outside git | daemon settings, machine paths, which projects this machine shows |
| per project | `.magnaflow/config.yml` | in the project's git | how to build, test and run *this* project, for any machine |

Machine-specific values (`git_sync`, `notify_command`, the cockpit's
`projects:` paths, tool binary paths) never go into a project's git.
Project values never go into `magnaflow.yml`. Every tool has its own
loader. There is no shared config library. That follows the standing
decoupling rule, see [design](design.md#i-small-deterministic-composable-tools).

## Machine: `magnaflow.yml`

One file per machine, one top-level section per daemon:

```yaml
watch:     # mf-watch, see ../watch/mf-watch.md#config-watch
  git_sync: false
cockpit:   # mf-cockpit
  port: 5210
  bind: localhost
  projects:
    - name: my-project       # unique; the {name} in every /api/projects/{name} route
      path: C:/GIT/my-project
  chat: { enabled: true, command: claude, args: [], timeout_minutes: 5 }
  run: { command: mf-run, timeout_seconds: 60 }
  watch: { command: mf-watch }   # Windows watch toggle only
  new_project: { root: ..., spec_kit: ..., templates: [...] }
```

- Each tool reads only its own section and ignores every other key. A
  future tool claims a new section without touching the others.
- Every field has a default. No file at all is valid and means all
  defaults.
- mf-worker and mf-run have no machine config.
- Paths are taken literally. `~` is **not** expanded, so write
  paths out in full. Cockpit directory paths are made absolute against
  the cockpit's working directory. Command values stay as written, so a
  bare name resolves via `PATH`.

### Lookup order {#lookup-order}

Each daemon resolves its file independently. The first hit wins:

1. `--config <path>`. The cockpit also accepts `MF_COCKPIT_CONFIG`, as a
   test seam. An explicit path is authoritative: if it does not exist,
   the result is all defaults, never a further lookup.
2. `magnaflow.yml` next to the binary.
3. The user config dir: `%APPDATA%\MagnaFlow\magnaflow.yml` on Windows,
   `~/.config/magnaflow/magnaflow.yml` elsewhere. This is where the
   installers put it, see [machine install](machine-install.md).
4. The tool's legacy file next to the binary (below).

The cockpit shows the path it actually used in `GET /api/config`, so
"why is it running the default?" can be answered from the dashboard.

### Legacy file names {#legacy-file-names}

Before the merge, each daemon had its own flat file: `mf-watch.yml` or
`mf-cockpit.yml`, fields at the root and no section. They still work and
behave the same, with a one-line deprecation notice on stderr (mf-watch
also writes it to its log):

- Found by step 4 of the lookup: always read as flat, always noticed.
- Any other file: a top-level `watch:` / `cockpit:` key means sectioned.
  Without it, recognizable root-level fields are read as flat, with the
  notice. A file is never refused just for having the old shape.

The cockpit build still copies a flat `mf-cockpit.yml` next to its
binary. Both installers delete it from the install. A dev build run from
`bin/` without any `magnaflow.yml` therefore runs on the legacy file.

### Cockpit writes ("Add project" / remove)

The cockpit is the only tool that writes `magnaflow.yml`.
`MagnaflowYmlAppender` adds or removes exactly one `cockpit: projects:`
entry as a narrow text edit. It never parses and regenerates the file,
so comments, ordering and hand formatting survive. Rules:

- A legacy `mf-cockpit.yml` is refused: merge it first. The deprecation
  path grows no new features.
- A `magnaflow.yml` with cockpit fields flat at the root (no `cockpit:`
  key) is refused on Add project: a new `cockpit:` section would hide
  them. The message says to move them under `cockpit:`; nothing is
  migrated automatically. Another tool's root section (`watch:`) is not
  a cockpit field.
- An existing file is edited where the lookup found it. When no file
  was found, a new one is created in the user config dir, never next to
  the binary, which may not be writable.
- The new entry is written as `- name:` / `path:` with forward slashes.
  Indentation follows the existing structure. It handles four shapes: a
  block list, an inline `projects: []`, a `cockpit:` section without
  `projects:`, and no `cockpit:` section at all.
- A `.bak` copy is written first. Then the file is re-parsed and
  verified: on append the new entry round-trips and every previously
  listed project is still read; on removal the entry is gone and every
  other entry survived. On any failure the original is restored.
  Callers serialize writes with one lock.

## Project: `.magnaflow/config.yml`

Committed with the project, identical on every machine. One file, with
sections owned by different readers:

```yaml
build:
  command: dotnet build        # or commands: [..] — exactly one form
test:
  commands: [dotnet test]
defaults:
  max_attempts: 3
  command_timeout_minutes: 30
agent:
  command: claude
  args: []
run:
  command: mf-run              # how the worker invokes mf-run
  services: [...]              # mf-run's service list
```

| Section | mf-worker | mf-run | mf-cockpit |
|---|---|---|---|
| `build`, `test` | required: exactly one of `command` / `commands` (non-empty) each | ignored | summary card |
| `defaults` | `max_attempts`, `command_timeout_minutes` | ignored | `max_attempts` in summary |
| `agent` | `command`, `args` | ignored | ignored |
| `run.command` | the mf-run executable | ignored | ignored |
| `run.services` | only "any services?": decides whether mf-run is spawned at all | full list, validated, see [mf-run](../run/mf-run.md#config) | service names for the Run card |

- A missing file is fatal for mf-worker. It means "no services" for
  mf-run and "no Run card" for the cockpit.
- Unknown top-level keys are ignored by every reader. The cockpit's
  config editor only warns about them, because the file is shared by
  tools with different vocabularies. The cockpit refuses only a YAML
  syntax error, plus a save that conflicts with a concurrent edit on
  disk.
- mf-watch never reads this file.

DRAFT: generated from code, not human-reviewed.

Code: tools/mf-watch/src/MagnaFlow.MfWatch/Config/WatchConfig.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Config/CockpitConfig.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Config/MagnaflowYmlAppender.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Config/ProjectConfigReader.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Config/ProjectConfig.cs, tools/mf-run/src/MagnaFlow.MfRun/Config/RunConfig.cs
Why: decisions/0012-one-machine-config.md
