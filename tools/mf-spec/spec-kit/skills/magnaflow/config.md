# `.magnaflow/config.yml`

The project's MagnaFlow config. It is committed and identical on every
machine, so it never holds machine paths or settings such as `git_sync`,
which live in each machine's own `magnaflow.yml`. mf-worker requires the
file. mf-run and the cockpit read only their own parts, and mf-watch
never reads it. Unknown keys are ignored.

```yaml
build:
  command: dotnet build App.sln     # or commands: [..], exactly one form
test:
  commands:
    - dotnet test App.sln
    - npm --prefix web test
defaults:
  max_attempts: 3                   # optional, default 3
  command_timeout_minutes: 30       # optional, default 30
agent:
  command: claude                   # optional, default claude
  args: ["--permission-mode", "bypassPermissions"]
run:
  services: [...]                   # optional, below
  stable: {...}                     # optional, below
```

## `build` and `test`

Both are required, each with exactly one of `command` (one string) or
`commands` (a non-empty list, run in order, the first failure stops).
After every implementation attempt the worker runs build then test. Red
means another attempt, up to `max_attempts`, and then the cmd is
aborted.

**Only commands that pass today.** A permanently red command makes every
cmd fail. Leave a broken suite out, and say so in a comment, until a cmd
fixes it. Never put anything here that deploys, installs or restarts the
MagnaFlow tools.

## `defaults`

- `max_attempts`: implementation attempts per cmd before it is aborted.
- `command_timeout_minutes`: the limit for each build or test command.

## `agent`

The CLI the worker runs for a cmd and for a `prompt` action. It runs
non-interactively, so nobody can answer a permission prompt. With
Claude Code's default mode every edit is refused and the agent
implements nothing. Set `args` to a permission mode that lets it work,
for example `["--permission-mode", "bypassPermissions"]`.

## `run.services`

The project's own long-running dev processes (a web server, a
background service). The worker stops them before a run so ports and
files are free, and starts them after it, so the fresh build is up.
A library or CLI project has none: leave `run:` out. That is the normal
case.

```yaml
run:
  services:
    - name: web                                    # unique, not "stable"
      command: src/App/bin/Debug/net10.0/App       # the built exe, not `dotnet run`
      args: ["--urls", "http://0.0.0.0:5000"]      # optional
      workdir: src/App                             # optional, default project root
      url: http://localhost:5000                   # optional, port check only
```

- **The built executable, never `dotnet run`**: that spawns a child and
  rebuilds on start, while the built exe is what the worker just
  produced and starts at once. Make sure the build produces that path.
- `command` and `workdir` both resolve against the project root.
- **Extensionless commands** keep one config valid on every OS. mf-run
  tries the literal path first, then `.cmd`, `.bat`, `.exe` (Windows) or
  `.sh` (Unix). Give a script both a `.cmd` and a `.sh`. The `.sh` is
  exec'd directly, so commit it executable:
  `git update-index --chmod=+x path/to/script.sh`.
- `url` only adds a "port listening" check to the cockpit's Run card.

By hand: `mf-run start|stop|restart|status [service]`.

## `run.stable`

An optional published build that a worker run never stops, on its own
port, so the human can keep using the app while the next cmd is built.
It is promoted after every cmd that ends `done` without a `branch:`,
and from the cockpit's **Promote** button (or `mf-run promote`).

```yaml
run:
  stable:
    publish: tools/mf-publish-stable   # resolved like a service command
    command: App                       # resolved inside the published dir
    args: ["--urls", "http://localhost:7300"]
    url: http://localhost:7300         # optional, port check
    link: https://host/app/            # optional, shown verbatim in the cockpit
    timeout_minutes: 15                # publish limit, default 15
```

The **`publish` script** gets one argument, the absolute output
directory, and runs in the project root. It must:

- publish a self-contained, runnable build into that directory (frontend
  included), so that `command` exists there;
- exit 0 on success and non-zero on failure;
- write nothing into the working tree. The worker's next run needs a
  clean tree.

Ship a `.cmd` and a `.sh`, as for services. The build runs with the
published directory as its workdir, not the repo. A failed publish, or a
build that does not start, leaves the previous instance running.
`.magnaflow/run/stable-publish.log` says why.
