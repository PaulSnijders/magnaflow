---
title: "Add project: browse for the path of an existing project"
cmd: 0035-cmd-add-project-browse.md
done: 2026-11-25
summary: "Add project → Existing now has a Browse… folder picker over the cockpit's own machine (GET /api/browse, folder names only, read-only). Also fixed a pre-existing SDK 11 test-project build failure. Check it by hand after the self-update: from another machine, at phone width, and switching drives on Windows."
---

# Result: 0035 Add project, Browse

## What was done

- `tools/mf-cockpit/src/MagnaFlow.MfCockpit/Projects/DirectoryBrowser.cs`: new class.
  - Takes `NewProjectConfig` and a home directory. `Program.cs` registers it as a singleton with `Environment.GetFolderPath(UserProfile)`.
  - `StartPath(hint)` returns, in order: the hint, resolved like Existing (rooted as is, relative against `Root`), if it is an existing directory; then `Root`, if it exists; then home.
  - `List(path)` returns a `Result(Listing, ErrorKind, Message)`, where the error kind is `None`, `BadPath`, `NotFound` or `Denied`.
- `Api/BrowseEndpoints.cs`: `GET /api/browse`, mapped right after `MapNewProjectApi()`. Errors map to 400, 404 or 403 with `{error}`.
- `Models/Dtos.cs`: `BrowseListingDto(Path, Parent, Roots, Dirs)` and `BrowseDirDto(Name, Path, Git)`, next to `NewProjectInfoDto`.
- `wwwroot/index.html`:
  - The Path row now holds the input plus a **Browse…** button (`secondary`).
  - Below it is an inline panel with the current path, **Up**, the root buttons, the scrollable folder list (`git` badge, "No subfolders."), an error line, and **Use this folder**.
  - Opening the dialog resets the panel to closed. Browse… toggles it.
- `assets/style.css`: only new `.browse-*` rules. The rest reuses `secondary`, `pill done` (the git badge), `muted` and `dialog-error`.
- `README.md`: one line in the Machine config bullet.
- Tests:
  - `DirectoryBrowserTests` (12 cases) covers sorting, `.hidden`, a `.git` directory and a `.git` file, files never listed, relative or empty path → BadPath, missing → NotFound, a file path → NotFound, parent null at the root, and the `StartPath` order.
  - `BrowseApiIntegrationTests` (5 cases) covers 200 with dirs and git, relative → 400 with `error`, missing → 404, no path → the configured root, and a relative hint resolved against root.
- I made no spec change. `docs/specs/cockpit/index.md` "Browse" was written ahead and the code follows it as written.

## Build and test

- **Problem:** this machine builds with .NET SDK **11.0.100** (the repo has no `global.json`). On SDK 11, `dotnet build tools/mf-cockpit/MfCockpit.slnx` failed in the test project with "scoped css bundles do not have matching endpoints: _framework/Microsoft.NET.Sdk.BlazorWeb.HotReload…".
  - **Cause:** SDK 11 generates Hot Reload browser-tools static web assets for every Debug build where `EnableHotReloadInRuntimeConfigDevFile` is unset. The test project uses `Microsoft.NET.Sdk.Web` and references the cockpit, and its copy of that JS module has no endpoint.
  - This was **pre-existing**: a `git archive HEAD` copy without this cmd's changes failed the same way.
- **Fix:** `<EnableHotReloadInRuntimeConfigDevFile>false</EnableHotReloadInRuntimeConfigDevFile>` in `tests/MagnaFlow.MfCockpit.Tests/MagnaFlow.MfCockpit.Tests.csproj`, with a comment explaining why. Tests never hot-reload, and the cockpit's own project is untouched.
  - I did not add a `global.json`. Pinning the SDK repo-wide would break machines that only have SDK 11.
  - If another Web-SDK project shows the same error, the same property is the fix.
- **Result:** all four build commands and all four test commands from the project config pass on SDK 11: worker-controller 266, mf-watch 131, mf-run 84, cockpit 414. The existing `NewProjectApiIntegrationTests` are unchanged and green.
- I checked the inline script with `node --check`.
- I did not open the dialog in a real browser. The human check from the cmd's Verify section is still open: another machine, a repo badge, Use this folder, Windows drives, phone width.

## Decisions taken while implementing

- **Empty `path=` is a 400.** The endpoint checks whether the `path` query is present, not whether it has a value, so `?path=` is BadPath (the spec says "relative or empty `path` is a 400"). Only a request with no `path` key at all falls back to the start folder.
- **A path that is a file gives NotFound (404).** `Directory.Exists` is checked first. This means the response never confirms that a file exists.
- **A malformed hint or root is ignored.** If `GetFullPath` throws on the hint or on `Root`, that step is skipped rather than turned into a 500.
- **Reading the Hidden attribute:** if reading a subfolder's attributes throws, that entry is listed, not hidden (the spec says "a subfolder that cannot be read is still listed"). A failing `.git` probe gives `git: false`.
- **Path resolution is duplicated.** The rooted-or-relative-to-root logic is copied into `DirectoryBrowser` instead of being shared with `NewProjectEndpoints.ResolvePath`, so `POST /api/projects` stays byte-for-byte unchanged, as the cmd requires.
- **Use this folder:** Name gets the last path segment, splitting on both `/` and `\`. At `/` or a drive root (`C:\`) there is no segment, so Name stays empty and the existing "name and path are required" check catches it. Focus returns to Path.
- **Keyboard:** every panel control is a `type="button"` button and the panel has no inputs, so Enter in the panel never submits the form implicitly. A keydown handler on the panel turns Ctrl/Cmd+Enter into `requestSubmit()` and calls `preventDefault` on a plain Enter that doesn't come from a button.
- **Errors in the panel** show `err.body.error` (the parsed `{error}` from `Cockpit.api`) and fall back to the status text.
- **The git badge** reuses `.pill.done`, so I didn't add a new badge class.
- **List height:** the folder list has `max-height: min(16rem, 35vh)`, so the dialog stays on a phone screen.

## Skipped or uncertain

- I did not write a Denied (403) test. It isn't easy to set up the same way on both OSes, and the cmd allowed skipping it. The mapping is in the code.
- Not tested on Windows: the ready-drive roots and `GetParent` returning null at `C:\`. On Linux I only tested the `/` root.
- The Existing-mode warning in `POST /api/projects` still checks only for a `.git` *directory*. So a worktree the picker badges `git` will still get a "no .git directory" warning when it's added. That is out of scope, because POST must not change.

## Self-answered questions

- *What does `?path=` with an empty value return?* A 400, as the spec says. Only an absent `path` falls back to the start folder.
- *What does a path that points to a file return?* A 404, never anything about the file.
- *What goes into Name when the chosen folder is a root?* Nothing. Name stays empty and the existing required-field check catches it.
- *Share the path resolution with `NewProjectEndpoints`?* No. It is duplicated so that POST stays untouched.
- *Which badge style for `git`?* The existing `.pill.done`.
- *Does the spec need an update?* No. The spec was written ahead and the code matches it.
