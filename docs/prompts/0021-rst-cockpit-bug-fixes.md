---
title: "mf-cockpit: safe and working links, endpoint guards, config overwrite, Add project fixes, live updates for new projects"
cmd: 0021-cmd-cockpit-bug-fixes.md
done: 2026-10-05
summary: "All cockpit BUG lines fixed: safe markdown links and working spec cross-links, follow-up 409 for unfinished parents, Reload/Overwrite on the config conflict, a clearer Add project dialog with legacy/flat-config 400s, and live watchers for newly added projects. Note: a magnaflow.yml with cockpit fields flat at the root is now refused on Add project, so move them under cockpit: by hand if you still have one."
---

# Report

All six steps are done. Build is green and all 311 cockpit tests pass (14 of them new).
`spec_lint` is green. The front-end changes were verified live (see below), because the
xUnit project cannot reach them.

## What changed

1. **Markdown links**
   - `md.js`: `renderMarkdown(src, opts)` unescapes each link target and checks it.
     A target with a scheme is kept only for `http:`, `https:` and `mailto:`. Any other
     scheme renders as the plain label. A relative target goes through an optional
     `opts.rewriteLink(target)` hook, which returns an href or `null` (plain text).
   - `specs.html`: passes `specLink(path, target)`. A relative `.md` target is resolved
     against the open spec's folder, keeps any `#anchor`, and becomes
     `specs.html?p=…&path=…`. It is plain text if it climbs above `docs/specs/` or is
     rooted. Non-`.md` relative targets stay as written.
   - Chat and command pages get the scheme check through the same renderer, with no
     change on their side.
2. **Follow-up guard**: `CommandsEndpoints` scans the lane before calling `DraftWriter`.
   A parent that is not `done` or `aborted` gets a 409:
   `"'<id>' is <status> — a follow-up can only be created for a done or aborted command"`.
   An unknown parent still gets the writer's existing 409.
3. **Config conflict**: the "changed on disk" note now has static text plus **Reload**
   (`loadConfig()`) and **Overwrite** buttons. Overwrite GETs the config, adopts only its
   `hash`, then clicks Save. A plain Save still gets the 409.
4. **Add project**
   - **Separator**: `NewProjectInfoDto` gained `Separator`
     (`Path.DirectorySeparatorChar`), and the preview uses it (trailing separators on
     root are trimmed).
   - **Error body**: `Cockpit.api` now attaches the parsed JSON error body as `err.body`.
   - **Dialog on failure**: shows `error`, then `(left in place: <path>)`, `output` in a
     scrollable `<pre>`, and any `warnings`.
   - **Dialog on success**: if the response has warnings, the dialog stays open, lists
     them, and the submit button becomes "Open project". Without warnings it navigates
     as before.
   - **Up-front refusals**: new `MagnaflowYmlAppender.CheckWritable(config)` refuses two
     configs: the legacy filename (the message now shared with Remove project through
     `LegacyFileMessage`), and a flat root (new `Outcome.FlatRootRefused`). Both POST
     modes call it before anything is scaffolded and return 400. `AppendProjectAsync`
     also calls it, and `AppendFailed` maps both refusals to 400.
   - **Flat-root detection**: `CockpitConfig.HasFlatCockpitFields(path)` reports true
     when there is no `cockpit:` key but there are root-level
     `port/bind/projects/chat/run/new_project` fields.
   - **Append verification**: the appender now also requires every project read before
     the edit to still be read afterwards. Otherwise it restores the file and reports
     `VerifyFailed`.
5. **Live watcher**: `IProjectWatcherRegistry.Add(name, path)` is implemented in
   `ProjectWatchersHostedService`. It does nothing after shutdown has begun, and if the
   name already has a watcher it disposes the new one. Both Add handlers call it right
   after `registry.Add`.
6. **Specs**: there are no BUG lines left in `cockpit/chat.md`, `specs.md`, `command.md`,
   `config.md`, `index.md` or `concepts/machine-config.md`. New sections or paragraphs
   describe:
   - link safety (`chat.md#link-safety`)
   - cross-links (`specs.md`)
   - the 409 (`command.md`)
   - Reload/Overwrite (`config.md`)
   - dialog results, separator, refusals and the runtime watcher (`index.md`)
   - the flat-root rule and the wider verification (`machine-config.md`)

   I added a "Recent spec updates" line to `STATUS.md`. Its "Open prompts" list still
   says 0021 is `ready`; the next `/spec-drift` regenerates it.

## Tests

- `ApiIntegrationTests`: follow-up returns 409 for draft/ready/running/questions (no
  draft file written) and 200 for done/aborted.
- `MagnaflowYmlAppenderTests`:
  - a flat root is refused and the file is untouched
  - a root holding only `watch:` is not flat
  - an append keeps every existing project
  - a composer that drops a sibling is restored and reports `VerifyFailed`
- `NewProjectApiIntegrationTests`:
  - legacy filename gets a 400 with the Remove-project wording, for both modes, with
    nothing scaffolded
  - a flat `magnaflow.yml` gets a 400, the file is untouched and nothing is registered
  - a project added at runtime emits a `lane` SSE event without a restart. This uses the
    real watcher through `CockpitFactory` and `/api/events`, the seam the existing SSE
    test uses.
- `CockpitFactory.WriteConfig*` now writes the sectioned `cockpit:` format. It used to
  write flat root-level configs, which the new refusal would have broken for every
  Add-project test. The parsed config is identical.

## Live verification (front-end)

I ran the cockpit against a scratch config on port 5299 and drove it with headless
Chrome through playwright-core:

- **Specs page**:
  - `../concepts/design.md#why` became `specs.html?p=p1&path=concepts%2Fdesign.md#why`;
    following it opened the Design spec.
  - `chat.md` resolved to `cockpit/chat.md`.
  - `../../README.md` and `javascript:alert(1)` rendered as plain text.
  - `https://` stayed a link.
- **Renderer** (in page context): `javascript:` became plain text; `https:` and
  `mailto:` became links.
- **Config page**, after an on-disk change:
  - the note showed the new text with both buttons
  - plain Save gave a 409
  - Overwrite wrote the editor content and hid the note
  - Reload loaded the disk version and hid the note
- **Add project, New tab**: the preview showed `/tmp/mf21/root/My-Thing`, and
  `/api/new-project` returned `"separator":"/"`.
- **Add project, Existing without `.git`**: the warning was listed and the button read
  "Open project"; clicking it opened `project.html?p=p2`.
- **SSE for that runtime-added project**: writing a cmd file emitted
  `{"project":"p2","kind":"lane"}`.
- **Failing command template**: showed `422 …: template 'broken' exited 3 (left in place: /tmp/mf21/root/bad-one)`
  and the captured output `template says: something broke`.
- **Flat `magnaflow.yml`** via curl: 400 with the "move them under a top-level
  'cockpit:' section" message, and the file was unchanged.

## Decisions taken while implementing

- **Endpoint holds the follow-up guard, not `DraftWriter`**, so the writer's tests and
  the ancestor-letter logic stay as they are.
- **Refusals run before scaffolding.** Otherwise a refused New run would leave an orphan
  directory.
- **`new_project` at the root counts as a flat cockpit field** for the refusal, because
  a new `cockpit:` section would hide it too, even though `HasAnyLegacyField` (the load
  notice) does not count it. `watch:` is mf-watch's root section, so it does not count.
- **The legacy-filename API test swaps the `CockpitConfig` singleton** (and
  `NewProjectConfig`) through `CockpitFactory`'s `configureServices`. `MF_COCKPIT_CONFIG`
  is an explicit path, and `LocatePath` never reports an explicit path as legacy.
- **Rooted `.md` links in specs (`/x.md`) render as plain text.** They cannot be
  resolved inside `docs/specs/`.
- **A runtime watcher covers only directories that exist when it starts**, the same as
  at startup. That is noted in `index.md`. A project registered via Existing before its
  first `docs/prompts/` exists gets no lane events until a restart. This is the
  pre-existing `ProjectWatcher` limitation, not something this cmd changes.

## Not done / uncertain

- Spec cross-links still open in a new tab (`target="_blank"`, as all rendered links
  do). The cmd did not ask for same-tab navigation.
- The link regex still ends a target at its first `)`, so `[x](javascript:alert(1))`
  renders as `x)`. That is harmless and pre-existing; I left it alone.
- A failed append that is not a refusal is still a 500 with a ProblemDetails `detail`,
  which `Cockpit.api` does not show (it reads only `error`). That is outside this cmd's
  BUG lines.

## Self-answered questions

- **Where does the follow-up status check live?** In the endpoint. The writer stays
  status-agnostic.
- **What counts as "flat root-level cockpit fields"?** A root without a `cockpit:` key
  that has any of `port`, `bind`, `projects`, `chat`, `run` or `new_project`. `watch:`
  is excluded because mf-watch owns that root section.
- **When is the flat or legacy refusal checked for mode `new`?** Before validation and
  scaffolding, so nothing is created on disk.
- **How does the dialog show warnings after a success, given that it navigates away
  today?** It stays open on warnings and offers "Open project". It navigates straight
  away when there are none.
- **Which relative links do spec pages rewrite?** Only `.md` targets, as the cmd says.
  `.md` targets that escape `docs/specs/` or are rooted become plain text, and other
  relative targets are left as written.
- **How does the server expose its separator?** As a new `separator` field on
  `GET /api/new-project`.
- **How can a test reach the legacy filename through the factory?** By replacing the
  `CockpitConfig` singleton.
- **The test helper wrote flat configs. Should the helper change, or should tests opt
  in to the sectioned format?** The helper now writes the sectioned format, which is
  the current one.
