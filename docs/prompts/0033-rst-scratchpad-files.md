---
title: "Scratchpad file drop: upload, download, delete"
cmd: 0033-cmd-scratchpad-files.md
done: 2026-11-13
summary: The scratchpad page now has a Files card to upload, download and delete files (up to 200 MB each) through the cockpit host. After the self-update, try it from two machines and a phone-width window.
---

# Result

Implemented as specified. All 393 cockpit tests pass, including the
unchanged `ScratchpadStoreTests` and `ScratchpadApiIntegrationTests`.
`spec_lint` reports 0 problems.

## What changed (all in `tools/mf-cockpit/` plus one spec line)

- `Config/ScratchpadStore.cs`: the sanitizer + `_<12 hex>` fallback is now
  in a public static `SafeNameFor(projectName)`. `PathFor` is
  `Directory/<SafeNameFor>.md`, so its results are unchanged (the existing
  filename tests prove it).
- `Config/ScratchpadFileStore.cs` (new, singleton):
  - `DirectoryFor`, `SanitizeFileName`, `List`, `SaveAsync`, `OpenRead`,
    `Delete`, and `MaxFileBytes = 200 MB`.
  - An `init`-only `MaxBytes` (default `MaxFileBytes`) lets the store
    tests check the cap without writing 200 MB.
- `Api/ScratchpadFileEndpoints.cs` (new), mapped after `MapScratchpadApi()`:
  - `PUT` raises `IHttpMaxRequestBodySizeFeature` to `MaxFileBytes + 1 MB`
    and also carries `RequestSizeLimitAttribute` metadata with that value.
  - It answers 413 straight away when `Content-Length` > cap, without
    reading the body.
  - It answers 400 for a bad name.
  - A Kestrel `BadHttpRequestException(413)` is mapped to the same 413
    JSON body.
- `Models/Dtos.cs`: `ScratchpadFileDto(Name, Size, UploadedAt)`.
- `wwwroot/scratchpad.html`: a Files card.
  - Upload + Refresh in the header, drag-and-drop with a highlight.
  - Sequential XHR PUT uploads with a `name · NN%` row.
  - The list with Download (`<a download>` styled as a button) and an
    immediate Delete.
  - `No files.` when the list is empty.
  - Reloads after upload/delete, on Refresh, and on `visibilitychange`
    to visible.
  - The note's intro line now says the files follow the same rule.
- `wwwroot/assets/style.css`: an appended block for the drop highlight,
  the file list, wrapping buttons and breaking long names. `a.btn` gets
  `text-decoration:none`.
- `README.md`: the page line and the "one store outside git" paragraph
  mention the files and their directory.
- `docs/specs/cockpit/scratchpad.md#files`: added the two name rules the
  spec did not state yet (trailing dots/spaces trimmed, 200-character cap
  keeping the extension). Everything else already matched.

## Verification done

- Store tests: the sanitize table, plus extra cases (`notes. . `, tab,
  `.bashrc`, non-ASCII, `nul.tar.gz`, `COM1`, the temp-file shape, the
  200-char cap). Also covered:
  - save + list order;
  - overwrite;
  - bad name;
  - over cap and exactly at cap;
  - cancelled save;
  - a body that throws mid-way (keeps the old version);
  - delete;
  - temp files not listed, downloadable or deletable;
  - unsanitized names don't resolve;
  - the shared directory for alike names.
- API tests: the round trip, including the `Content-Disposition` check,
  a non-ASCII `filename*`, the working copy staying clean, unknown project
  404 on all four endpoints, and a bad name 400.
- The 31 MB-accepted and over-cap-413 tests run on **real Kestrel**
  (`WebApplicationFactory.UseKestrel`). TestServer enforces no body limit
  at all, so it could not prove anything. I checked that the 31 MB test
  **fails** with both limit raises removed and passes with only the
  metadata. Each of the two raises works on its own; both are kept.
- Smoke run of the real app plus headless Chrome screenshots at 1200 px
  and 390 px wide. Buttons wrap under the name on the phone width and a
  long name breaks.

## Decisions taken while implementing

- **Kestrel tests use an explicit `Listen(Loopback, 0)`.**
  `UseKestrel(0)` does *not* override Program's `UseUrls` (the configured
  port, 5210 by default). On this machine that is the live cockpit's port,
  so the first full run failed with "address already in use".
  `UseKestrel(o => o.Listen(...))` does override it. Comment in
  `StartOnKestrel`.
- **Each API test class gets its own config directory.** The existing
  scratchpad tests share `mf-cockpit-tests/` and delete
  `mf-cockpit-tests/scratchpad`. Sharing that would let the classes
  delete each other's files when run in parallel.
- **GET/DELETE match the stored name exactly.** They only resolve a name
  that sanitizes to itself; anything else is 404. PUT is the only place
  that rewrites a name.
- **When the name is cut to 200 characters,** the extension is kept only
  if it is ≤ 20 characters (otherwise a "name" like `a.<300 chars>` would
  keep its whole tail).
- **Names shaped like an upload temp file** (`.upload-*.tmp`) are refused
  by the sanitizer itself (400 on PUT). That makes "a temp file name
  counts as not found" hold for every operation.
- **Delete errors on the page:** a 404 just reloads the list; other
  errors show an `alert`.

## Uncertain / not done

- A file name containing a literal `/` cannot reach the server through
  the route. Kestrel keeps `%2F` encoded in a route value, so
  `PUT .../files/a%2Fb.txt` stores `a%2Fb.txt`, not `b.txt`. Browsers
  never put `/` in `File.name`, so this only affects hand-made requests.
  The store's "last segment" rule is covered by its unit tests.
- An already existing quirk, untouched: an empty note keeps the textarea
  placeholder `Loading…` after loading. It is visible in the screenshots
  and is out of scope.
- **Human check still open:** after the self-update, two machines (one
  ~50 MB file), download both, delete one, and a phone-width window.

## Self-answered questions

- **How to prove the 30 MB Kestrel limit was raised, given TestServer has
  no limit?** Run those two tests on real Kestrel via
  `WebApplicationFactory.UseKestrel` on an ephemeral port. Still on
  `CockpitFactory`, with a generated stream.
- **Feature or metadata for the body limit?** Both: the feature set in the
  handler, plus `RequestSizeLimitAttribute` metadata, each at cap + 1 MB.
- **What does an over-cap upload with a declared `Content-Length` do?**
  An early 413 with the JSON body, without reading. A chunked over-cap
  body is stopped by the store's running count (→ 413). Past cap + 1 MB,
  Kestrel's own 413 is mapped to the same body.
- **How to test the 200 MB cap in the store without writing 200 MB?** An
  `init`-only `MaxBytes` on the store; the API test covers the real
  constant.
- **The Delete error UI?** Not specified. 404 silently reloads; other
  errors show an `alert` and reload.
- **Download as a link or a button?** The command says `<a href download>`
  and the spec says "Download button". I used a link styled `btn
  secondary`.
