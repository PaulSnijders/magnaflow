---
title: "Scratchpad file drop: upload, download, delete"
status: done
created: 2026-11-13
attempts: 1
---

## Context

The cockpit scratchpad (`scratchpad.html?p=<name>`) is used daily to
move text from one machine to another through the cockpit host. The
product owner also wants to move small files that way. Under the note
comes a file drop: an Upload button, and a list of files each with
Download and Delete. It is for temporary transfer, so Delete acts at
once, without a confirm.

The spec is already written ahead: `docs/specs/cockpit/scratchpad.md`,
section **Files** (storage, page, API, name rules, temp-file write).
`docs/specs/cockpit/config.md#remove-project` now says the files are
kept like the note. Read both first; they are the contract.

All work is in `tools/mf-cockpit/`.

## Task

1. **Store** — new `Config/ScratchpadFileStore.cs` (or extend
   `ScratchpadStore`; your call, keep it small), registered as a
   singleton in `Program.cs`.
   - Directory: `Path.Combine(ScratchpadStore.Directory, <safe> + ".files")`,
     where `<safe>` is exactly the name `ScratchpadStore.PathFor` uses
     (sanitizer + `_<12 hex>` fallback). Factor that piece out of
     `PathFor` so both share it; `PathFor`'s result must not change.
   - `SanitizeFileName(string)`: take the last segment after `/` or
     `\`; replace `< > : " / \ | ? *` and chars below 0x20 with `_`;
     trim trailing dots and spaces (Windows drops them); refuse (null)
     when the result is empty, all dots, or a reserved device name
     (reuse `DirNameSanitizer.IsReservedName` on the part before the
     first dot). Cap the name at 200 chars, keeping the extension.
   - `List(project)` → entries `{name, size, uploadedAt}` (last write
     time UTC, ISO "o"), newest first; skip the temp files; absent
     directory → empty.
   - `SaveAsync(project, name, Stream body, CancellationToken)`: create
     the directory, stream into `.upload-<guid>.tmp` in the same
     directory, enforce the 200 MB cap while copying (stop and delete
     the temp file when exceeded → TooLarge), then
     `File.Move(tmp, final, overwrite: true)`. On any exception or
     cancellation delete the temp file. Return the entry.
   - `OpenRead(project, name)` / `Delete(project, name)`: sanitize the
     name the same way and resolve it with `PathGuard.ResolveInside`
     against the files directory as a second guard; a temp file name
     counts as not found.
   - Constant `MaxFileBytes = 200L * 1024 * 1024`.
2. **Endpoints** — new `Api/ScratchpadFileEndpoints.cs`, mapped next to
   `MapScratchpadApi()`. Unknown project → 404 like the note endpoints.
   - `GET /api/projects/{name}/files`
   - `PUT /api/projects/{name}/files/{file}`: read `Request.Body` as a
     raw stream (no multipart, no `IFormFile`, so no antiforgery).
     Raise the per-request body limit for this endpoint only
     (`IHttpMaxRequestBodySizeFeature` or `RequestSizeLimit` metadata)
     to slightly above `MaxFileBytes`; Kestrel's default 30 MB must
     not be the effective cap. Over the cap → 413 with
     `{error: "file exceeds the 200 MB cap"}`; bad name → 400.
   - `GET /api/projects/{name}/files/{file}`:
     `Results.File(stream, "application/octet-stream", fileDownloadName: name)`
     so the browser downloads under the stored name (RFC 5987 encoding
     of non-ASCII names comes with it).
   - `DELETE /api/projects/{name}/files/{file}` → 204 / 404.
3. **Page** — `wwwroot/scratchpad.html`, a second `card` below the note
   card, headed `Files`, styled with the existing classes in
   `assets/style.css` (add only what is missing, in that file).
   - An **Upload** button (hidden `<input type="file" multiple>`) and a
     **Refresh** button in the card header. Dropping files on the card
     uploads them too; show a visible drop highlight while dragging.
   - Uploads go one after another with `XMLHttpRequest` `PUT` (fetch
     has no upload progress) to
     `/api/projects/{p}/files/{encodeURIComponent(file.name)}`; each
     shows a row `name · 42%` that turns into the error text on
     failure (413/400 message from the body) and disappears on success.
   - The list: name, human size (B/KB/MB), upload time (local clock;
     date too when not today), a **Download** link (plain `<a href>`
     to the GET endpoint with the `download` attribute) and a
     **Delete** button that calls DELETE at once and reloads the list.
     Empty list: one muted line `No files.`
   - Reload the list after each upload/delete, on Refresh, and on
     `visibilitychange` to visible. Update the card's intro line (and
     the note card's intro, which says "not in git, never read by any
     tool") so it is clear files follow the same rule and are meant
     for temporary transfer.
   - Must work on a phone-width screen (it is opened from other
     machines): buttons wrap, long names break.
4. **README** — `tools/mf-cockpit/README.md`: the scratchpad line and
   the "one store outside git" paragraph mention the files.

## Must not change

- The note: its file, its API, the hash/409 contract, the 256 KB cap.
- `ScratchpadStore.PathFor` results for every existing project name.
- No new NuGet or JS dependency. No SSE event for files.
- Nothing about files lands inside a project's working copy.

## Verify

- xUnit, store (`ScratchpadFileStoreTests`): sanitize table (`a/b.txt`
  → `b.txt`, `..\x` → `x`, `CON.txt` → refused, `...` → refused,
  `a:b?.pdf` → `a_b_.pdf`, trailing dot trimmed); save + list order;
  same name overwrites; over-cap save leaves no file and no temp file;
  a cancelled save leaves no temp file; delete; temp files are not
  listed and not downloadable; two projects that sanitize alike share
  the directory (documented, like the note).
- xUnit, API (`ScratchpadFilesApiIntegrationTests`, on the existing
  `CockpitFactory`): upload → list → download bytes equal → delete →
  404; unknown project 404; a body above 30 MB but below the cap is
  accepted (proves the Kestrel limit was raised — use a generated
  stream, not a fixture file); over the cap → 413.
- Existing `ScratchpadStoreTests` and `ScratchpadApiIntegrationTests`
  stay green unchanged.
- **Human, after the self-update / `install.ps1`**: open the scratchpad
  on machine A, drop two files (one ~50 MB), open the same page on
  machine B, download both, delete one; check a phone-width window.

## Not in scope

- Auth or per-file links to share outside the cockpit — VPN/LAN stays
  the perimeter (decision 0013).
- Automatic expiry of old files — delete by hand is the wish.
- A total disk quota — one per-file cap is enough for a transfer area.
- Live push of the file list to other browsers — reload on visibility
  covers switching machines.
