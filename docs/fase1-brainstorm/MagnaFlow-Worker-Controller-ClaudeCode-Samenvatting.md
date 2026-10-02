# MagnaFlow -- Worker Controller (Claude Code)

## Conclusions

### Driving Claude Code

-   Use **Claude Code headless via the CLI** (`claude -p`).
-   Avoid computer or browser automation in the first version.
-   Let the Worker Controller simply orchestrate the existing
    tooling.
-   Use a **Claude Max account** via OAuth/login, so no
    API costs are needed.
-   Only use an API key when you deliberately want to pay via the
    API.

### Desired workflow

1.  Pick the first pending task from `.magnaflow/tasks/`.
2.  Create a Git branch.
3.  Build the prompt from the specifications.
4.  Start Claude Code.
5.  Wait until the task is done.
6.  Run build and tests.
7.  Feed any errors back to Claude automatically.
8.  Repeat a limited number of retries.
9.  Commit the changes.
10. Write a summary and status back.

## Development language

After comparing several options, **C#/.NET** was chosen.

### Why C#

-   Works excellently on Windows, Linux and macOS.
-   Strong support for console applications.
-   Good async support.
-   Easy to start external processes (Claude, Git, build, tests).
-   Robust JSON and YAML libraries.
-   Fits well with existing knowledge and projects.

### Alternatives

-   **Python**: ideal for quick prototypes, but less suitable as a
    long-term foundation.
-   **Go**: excellent second choice for CLI tools.
-   **Rust**: technically very strong, but higher development cost.
-   **C++**: needlessly complex for this type of application.

## Possible libraries

-   Spectre.Console.Cli
-   CliWrap
-   YamlDotNet

## Expected CLI

> **Updated:** pinned down in
> `docs/fase2-worker-controller/workflow-v0.1.md`.

``` text
mf-worker run <task-id>
mf-worker next
mf-worker run-all
mf-worker status
```

`start` (watch mode) is dropped: polling will later be done by a separate
dispatcher, the controller stays one-shot. `retry` is dropped: retries live
inside a run (`attempts`/`max_attempts` in the task frontmatter).

## Architecture

The Worker Controller deliberately stays small.

It is not an AI agent, but a reliable orchestration layer that drives
existing tools such as Claude Code, Git and build systems.

That fits well with the MagnaFlow philosophy: combine as much
existing open-source tooling as possible instead of reinventing
it.
