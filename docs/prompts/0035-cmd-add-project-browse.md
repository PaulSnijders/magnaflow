---
title: "Add project: browse for the path of an existing project"
status: running
created: 2026-11-25
---

## Context

Adding an existing project from the cockpit's front door
(`index.html`, Add project → Existing) needs a Path, typed from memory.
The product owner often opens the cockpit from another machine (over
Tailscale), so the browser's own file dialog is no use: the path lives
on the cockpit's machine. The wish: a server-side folder picker in the
dialog.

The spec is written ahead: `docs/specs/cockpit/index.md`, section
**Add project**, paragraph **Browse**. Read it first; it is the
contract (start folder, roots, hidden folders, `git` badge, the
endpoint shape and its status codes).

All work is in `tools/mf-cockpit/`.

## Task

1. **Core** — new `Projects/DirectoryBrowser.cs`, registered as a
   singleton in `Program.cs`. Inputs it needs from config:
   `NewProjectConfig.Root` (the start fallback) — inject it, plus a
   home-directory value (`Environment.GetFolderPath(UserProfile)`) as a
   constructor parameter so tests can set it.
   - `StartPath(string? hint)`: `hint` resolved the way Existing
     resolves a path (absolute as is; relative against `Root` when set)
     if that is an existing directory; else `Root` if it exists; else
     home.
   - `List(string path)` → `{path, parent, roots, dirs}` or an error
     kind (`BadPath` 400, `NotFound` 404, `Denied` 403). `path` must be
     `Path.IsPathFullyQualified` (else BadPath); normalize with
     `Path.GetFullPath`. `parent` is `Directory.GetParent(path)?.FullName`
     (null at a root). `roots`: on Windows the ready drives'
     `RootDirectory.FullName` (`DriveInfo.GetDrives()` filtered on
     `IsReady`, wrapped in try/catch per drive); elsewhere `["/"]`.
   - `dirs`: `new DirectoryInfo(path).EnumerateDirectories()`, skip
     `FileAttributes.Hidden` and names starting with `.`, sort with
     `StringComparer.OrdinalIgnoreCase`. `git` is
     `Directory.Exists(sub/.git) || File.Exists(sub/.git)` (worktrees
     and submodules have a `.git` file); an exception on that probe
     means `git: false`, the entry stays. `UnauthorizedAccessException`
     on enumerating `path` itself → Denied; `DirectoryNotFoundException`
     → NotFound.
2. **Endpoint** — new `Api/BrowseEndpoints.cs`, `GET /api/browse`,
   mapped next to `MapNewProjectApi()`. No `path` query → list
   `StartPath(hint)` where `hint` is the optional `hint` query (the
   dialog sends the Path field's current value). With `path` → list
   it. Errors as `{error}` with the status from the spec. Add the DTOs
   to `Models/` next to `NewProjectInfoDto`.
3. **Dialog** — `wwwroot/index.html` (and the script in it), styled with
   the existing classes in `assets/style.css` (add only what is
   missing, in that file).
   - A **Browse…** button (`secondary`) on the Path row of the Existing
     tab. It toggles an inline panel under the field, inside the
     dialog — no second `<dialog>`.
   - Panel: the current path (monospace, wraps), an **Up** button
     (disabled when `parent` is null), the roots as small buttons, the
     folder list (scrollable, max height so the dialog stays on
     screen; a `git` badge on repos; muted `No subfolders.` when
     empty), the error line, and a **Use this folder** button.
   - Click a folder → load it. Use this folder → set Path to `path`;
     if Name is empty, set it to the last segment of `path`; close the
     panel. Opening the dialog resets the panel (closed).
   - Ctrl+Enter keeps submitting the form, also with the panel open;
     Enter inside the panel must not submit it.
   - Must work at phone width: buttons wrap, long names break.
4. **README** — `tools/mf-cockpit/README.md`: one line where Add project
   is described.

## Must not change

- `POST /api/projects` and all its validation; the Path field stays a
  free text field and the source of truth for the request.
- The New tab.
- No file names, sizes or contents in any response; no write of any
  kind from `/api/browse`.
- No new NuGet or JS dependency.

## Verify

- xUnit, `DirectoryBrowserTests` on a temp tree: sorting
  (`b`, `A`, `c` → `A b c`); `.hidden` left out; a dir with a `.git`
  directory and one with a `.git` file both `git: true`; a file in the
  folder never listed; relative path → BadPath; missing → NotFound;
  `parent` null for `Path.GetPathRoot(temp)`; `StartPath` order
  (valid hint → root → home, relative hint against root). Skip a
  Denied test unless it is easy on both OSes.
- xUnit, `BrowseApiIntegrationTests` on the existing `CockpitFactory`:
  `GET /api/browse?path=<temp>` → 200 with the dirs; relative → 400;
  missing → 404; no `path` with a configured root → that root.
- Existing `NewProjectApiIntegrationTests` stay green unchanged.
- **Human, after the self-update / `install.ps1`**: from another
  machine, open the cockpit, Add project → Existing → Browse…, walk to
  a repo (badge visible), Use this folder, check Name and Path are
  filled, add it; on Windows switch drives via the roots; check a
  phone-width window.

## Not in scope

- Creating a folder from the picker — New covers that.
- Browsing for the New tab or for `new_project.root` — root is
  machine config, edited on the config page.
- Restricting browsing to a subtree — the network stays the perimeter
  (decision 0013); the endpoint only reveals folder names.
