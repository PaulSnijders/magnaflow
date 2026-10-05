# Technical

`index.html` is the cockpit's front door. It shows every registered
project in one table, a cross-project attention list, and the Add
project dialog. It takes no parameters and has no summary bar: the
header carries the sound toggle and the live badge. Appearance follows
[design](../concepts/design.md#shared-conventions).

## Attention

Answers "who is at bat?" across all projects. The server recomputes it
on every request (`AttentionRules.Build`) and keeps no memory. A lane
item is listed when it is:

- `questions`: always.
- `aborted`: only while it is the highest lane id in its project
  (ordinal, `0005 < 0005B < 0006`). Any higher id counts as "seen it":
  a follow-up, the next number, even a malformed lane file.
- `running` with no evidence write for 15 minutes ("may be stuck").
  Without an evidence directory the cmd file's own mtime is used.

`draft` and `ready` never appear. Each item links to
`command.html?p=<name>&id=<id>`. See
[command lifecycle](../concepts/command-lifecycle.md).

## Projects table

Columns: Project, Branch, Draft, Ready, Running, Questions, Done,
Aborted, Watcher, Run, Latest. Rows follow registry order (config
order, then projects added since startup). A registered project whose
path is missing stays listed, marked "(path missing)", with zero counts.

The table renders from `GET /api/projects`, which is git-free and
mf-run-free (file I/O only), so it is fast on any tree. Branch and Run
are fetched per project afterwards and never block the rest; a failure
shows `?`.

- **Branch**: from `/api/projects/{name}/git`, ` *` when the tree is
  dirty.
- **Watcher**: "alive" means `.magnaflow/mf-watch.lock` exists. Presence
  only, no PID check, unlike the project page's Watcher card.
- **Run**: `running/total` from mf-run's *tracked* fact only, so a
  service started by hand counts as not running here. `-` without
  `run:` services. The tooltip lists service URLs, with a loopback host
  rewritten to the host the browser came in on (display only).
- **Latest**: the highest-id lane item: id, title truncated to 32
  characters, status pill (`malformed` when the cmd file does not
  parse). The rst summary line is deliberately not shown here.

## Sound cues

Only each project's Latest item is tracked. A cue plays when it turns
`done`, `questions` or `aborted`. The first render is silent, and at
most one cue plays per refetch (priority aborted > questions > done).
An older command changing status does not chime here; the project page
chimes over its full lane. The toggle is one per browser (default on),
shared with every project page. It shows a pending state until the
first click or key press on the page (browser autoplay rules).

## Add project

Opened by "+ Add project", or by the empty state's button when no
project is registered. Two tabs, Existing and New. Ctrl+Enter submits.
On success the browser goes to the new project's page. A success that
returned `warnings` (no `.git`, spec kit not copied) keeps the dialog
open to list them, and the submit button becomes "Open project". A
failure shows `error`; a failed scaffold also shows the directory it
left in place and the template's captured `output`.

`POST /api/projects` with `{mode: "new"|"existing", name, path?,
template?}`. Any other mode is a 400. One process-wide lock
(`NewProjectEndpoints.ConfigWriteGate`) serializes every
`magnaflow.yml` edit, including Remove project on the
[config page](config.md#remove-project).

**Existing**: name and path are required (400). A relative path
resolves against `cockpit.new_project.root`; a relative path without a
root is a 400. The path must be an existing directory (400). A
duplicate name (case-sensitive) or the same full path
(case-insensitive) is a 409. A missing `.git` is a returned warning,
never a refusal.

**New** is offered only when `cockpit.new_project.root` is configured.
Otherwise the tab says what to add. `GET /api/new-project` returns
`{root, templates: [names], specKit: bool, separator}`; the target
preview joins root and dir name with the server's `separator`. Only names leave the
server: the request picks a template by name and can never supply a
path or command text. Validation runs in this order, and the order is
the design:

| Check | Refusal |
|---|---|
| name present | 400 |
| name not registered | 409 |
| root configured | 400 |
| sanitized dir name non-empty, not a Windows device name (`CON`, `NUL`, …) | 400 |
| `<root>/<dirname>` does not exist | 409 |
| template name exists in config | 400 |

Sanitizing trims, strips characters invalid in Windows file names and
control characters, turns whitespace into `-`, and collapses repeated
hyphens. There is no override field; the dialog previews the target.

Then, in order: mkdir; the template, if one was chosen (`copy` copies
the source's contents without a top-level `.git`; `command` gets
`{target}` and `{name}` substituted in its args, runs in the target
directory, and has a hard timeout, default 300 s, where a non-zero exit
is a failure); the spec kit into `docs/spec-kit/` (not configured or
missing is a warning); `.gitignore` gains whichever of `.magnaflow/*`
and `!.magnaflow/config.yml` are missing, line by line; a commented
`.magnaflow/config.yml` stub, only if absent; `git init` unless the
template left a `.git`; one commit `cockpit: create project <name>`;
and a seeded draft "Adopt spec system" through the normal draft write
(a failure here is a warning). The draft is inert, so the human gate
stays where it always was.

A failed step answers 422 with `{error, output, path, warnings}` and
leaves the directory in place, unregistered, for diagnosis. Re-adding
it via Existing is the recovery.

**Registration is last**, for both modes: append to `magnaflow.yml`,
then the in-memory registry, then SSE kind `projects`. So a failure
never leaves a phantom entry. The append is a text edit, never a
parse-and-regenerate, so comments and ordering survive. `magnaflow.yml`
is not in git, so the guard is a `.bak` copy, a re-parse that must show
the new entry and every project listed before, and a restore plus 500
on any failure. Two configs are refused with a 400 before anything is
scaffolded or written: the legacy `mf-cockpit.yml` filename (the same
message as Remove project), and a `magnaflow.yml` with cockpit fields
flat at the root (move them under `cockpit:`). When no file was loaded,
a new `magnaflow.yml` is created in the user config directory. See
[machine config](../concepts/machine-config.md).

## Live updates

SSE kinds `lane`, `projects`, `watch` and `run` from any project
refetch the whole list. `evidence` does not.

A project added at runtime gets its file watcher at once, through
`IProjectWatcherRegistry` (the same registry Remove project uses to
dispose one), so its lane, watcher and service events flow without a
restart. A watcher covers only the directories that exist when it
starts.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0009-cockpit-add-project.md
