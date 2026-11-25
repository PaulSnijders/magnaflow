// @ts-check
// scratchpad.html (docs/specs/cockpit/scratchpad.md). Page pattern (docs/specs/cockpit/index.md#page-rendering):
// server data and the note live in `state`. The note's text is page state rather than useState on
// purpose: autosave, blur, visibilitychange and the 409 conflict handler all read or reset it from
// outside the component. The upload rows are page state too (XHR progress updates them). The drop
// highlight and a row's in-flight Delete are UI state in the Files card.
import { html, render, useState, useRef, SummaryBar, ctrlEnter, startProjectChrome } from "../ui.js";
import * as Cockpit from "../app.js";

/** @typedef {import("../types.js").Scratchpad} Scratchpad */
/** @typedef {import("../types.js").ScratchpadSaveResult} ScratchpadSaveResult */
/** @typedef {import("../types.js").ScratchFile} ScratchFile */
/** @typedef {import("../types.js").ProjectSummary} ProjectSummary */
/** @typedef {import("../types.js").LiveState} LiveState */
/** @typedef {{key: number, name: string, text: string, error: boolean}} UploadRow */

const project = Cockpit.qs("p") || "";
const base = `/api/projects/${encodeURIComponent(project)}`;
const filesBase = `${base}/files`;
document.title = "scratchpad · " + project;

const state = {
  /** @type {string[]} */ projects: [],
  /** @type {ProjectSummary | null} */ summary: null,
  /** @type {LiveState | null} */ live: null,
  // The note.
  /** @type {string | null} */ baseHash: null,
  /** @type {string | null} */ savedAt: null,
  text: "",
  placeholder: "Loading…",
  loaded: false,
  dirty: false,
  /** "saved" | "saving" | "unsaved" | "error" */ status: /** @type {string | null} */ (null),
  statusExtra: "",
  // Files.
  /** @type {ScratchFile[] | null} */ files: null,
  /** @type {string | null} */ filesError: null,
  /** @type {UploadRow[]} */ uploads: [],
};
let saving = false;
/** @type {ReturnType<typeof setTimeout> | undefined} */
let saveTimer;
let uploadSeq = 0;

const mount = /** @type {HTMLElement} */ (document.getElementById("app"));
function rerender() {
  render(html`<${Page} state=${state}/>`, mount);
}

function clockOf(/** @type {string} */ iso) {
  try { return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }); } catch { return ""; }
}

function setStatus(/** @type {string} */ status, extra = "") {
  state.status = status;
  state.statusExtra = extra;
  rerender();
}

async function loadScratchpad() {
  try {
    /** @type {Scratchpad} */
    const data = await Cockpit.api(`${base}/scratchpad`);
    state.baseHash = data.hash;
    state.savedAt = data.savedAt;
    state.text = data.content;
    state.loaded = true;
    state.dirty = false;
    setStatus("saved");
  } catch (err) {
    state.placeholder = /** @type {Error} */ (err).message;
    setStatus("error", /** @type {Error} */ (err).message);
  }
}

// The whole safety model is the hash check (ontwerp-v0.5.md item 8): the file lives outside every
// watched directory, so there is no SSE push — concurrency is handled entirely here on PUT.
async function save(force = false) {
  if (saving) return;
  if (!state.dirty && !force) return;
  saving = true;
  setStatus("saving");
  try {
    /** @type {ScratchpadSaveResult} */
    const res = await Cockpit.api(`${base}/scratchpad`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content: state.text, baseHash: state.baseHash }),
    });
    state.baseHash = res.hash;
    state.savedAt = res.savedAt;
    state.dirty = false;
    setStatus("saved");
  } catch (err) {
    if (/** @type {Error} */ (err).message.startsWith("409")) {
      await handleConflict();
    } else {
      setStatus("error", /** @type {Error} */ (err).message);
    }
  } finally {
    saving = false;
  }
}

// A newer version on the server (someone edited from another machine): offer reload-theirs vs
// keep-mine, never silent loss.
async function handleConflict() {
  const keepMine = !confirm(
    "A newer version of this scratchpad is on the server.\n\n" +
    "OK = load the server's version (discard your unsaved changes)\n" +
    "Cancel = keep your version (overwrite the server's)");
  if (!keepMine) {
    await loadScratchpad();
    return;
  }
  // Overwrite: adopt the server's current hash, then save my content over it. `saving` is still
  // set by the save that hit the 409, so it is released for this nested save.
  try {
    /** @type {Scratchpad} */
    const server = await Cockpit.api(`${base}/scratchpad`);
    state.baseHash = server.hash;
    state.dirty = true;
    saving = false;
    await save(true);
  } catch (err) {
    setStatus("error", /** @type {Error} */ (err).message);
  }
}

function saveNow() {
  clearTimeout(saveTimer);
  save(true);
}

function StatusLine({ status, extra, savedAt }) {
  const style = "text-transform:none; letter-spacing:normal; font-weight:normal;";
  let text = "", cls = "muted";
  if (status === "saving") text = "· saving…";
  else if (status === "unsaved") { text = "· unsaved"; cls = ""; }
  else if (status === "error") { text = "· " + (extra || "save failed"); cls = "run-error"; }
  else if (status === "saved") text = savedAt ? "· saved " + clockOf(savedAt) : "· empty";
  return html`<span class=${cls} id="scratch-status" style=${style}>${text}</span>`;
}

function NoteCard({ s }) {
  const onInput = (/** @type {any} */ e) => {
    state.text = e.currentTarget.value;
    state.dirty = true;
    setStatus("unsaved");
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => save(), 1500);
  };
  // Ctrl+Enter via the shared helper (ontwerp-v0.5.md item 9); Ctrl+S is the editor-save convention.
  const onCtrlEnter = ctrlEnter(saveNow);
  const onKeyDown = (/** @type {KeyboardEvent} */ e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === "s") { e.preventDefault(); saveNow(); return; }
    onCtrlEnter(e);
  };
  return html`
    <div class="card">
      <h2>
        Scratchpad
        ${" "}<${StatusLine} status=${s.status} extra=${s.statusExtra} savedAt=${s.savedAt}/>
      </h2>
      <p class="muted" style="margin-top:0;">
        A shared notepad for this project, kept on the cockpit host — the same note from every machine
        that opens this dashboard. Like the files below: not in git, never read by any tool. Autosaves;
        Ctrl+S or Ctrl+Enter saves now.
      </p>
      <textarea id="scratch-editor" rows="24" spellcheck="false" disabled=${!s.loaded} placeholder=${s.placeholder}
        value=${s.text} onInput=${onInput} onBlur=${() => save()} onKeyDown=${onKeyDown}></textarea>
    </div>`;
}

// ---- File drop (docs/specs/cockpit/scratchpad.md#files) ----------------------------------------
// No SSE for files (the directory is outside every watched tree): the list reloads after each
// upload/delete, on Refresh, and when the tab becomes visible again (switching machines).

function humanSize(/** @type {number} */ n) {
  if (n < 1024) return n + " B";
  if (n < 1024 * 1024) return (n / 1024).toFixed(n < 10 * 1024 ? 1 : 0) + " KB";
  return (n / (1024 * 1024)).toFixed(n < 10 * 1024 * 1024 ? 1 : 0) + " MB";
}

function whenOf(/** @type {string} */ iso) {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "";
  const time = d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
  return d.toDateString() === new Date().toDateString() ? time : d.toLocaleDateString() + " " + time;
}

async function loadFiles() {
  try {
    state.files = await Cockpit.api(filesBase);
    state.filesError = null;
  } catch (err) {
    state.filesError = /** @type {Error} */ (err).message;
  }
  rerender();
}

// One XHR PUT per file (fetch has no upload progress); the row shows name · NN% and is removed on
// success, or turns into the error text on failure.
function uploadOne(/** @type {File} */ file) {
  return new Promise((resolve) => {
    /** @type {UploadRow} */
    const row = { key: ++uploadSeq, name: file.name, text: "0%", error: false };
    state.uploads.push(row);
    rerender();
    const label = (/** @type {string} */ text) => { row.text = text; rerender(); };

    const xhr = new XMLHttpRequest();
    xhr.open("PUT", filesBase + "/" + encodeURIComponent(file.name));
    xhr.setRequestHeader("Content-Type", "application/octet-stream");
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) label(Math.floor((e.loaded / e.total) * 100) + "%");
    };
    const fail = (/** @type {string} */ text) => {
      row.error = true;
      label(text);
      resolve(false);
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        state.uploads = state.uploads.filter((r) => r !== row);
        rerender();
        resolve(true);
        return;
      }
      let detail = "";
      try { detail = JSON.parse(xhr.responseText).error || ""; } catch { /* not JSON */ }
      fail(xhr.status + (detail ? ": " + detail : " " + xhr.statusText));
    };
    xhr.onerror = () => fail("upload failed (connection error)");
    xhr.onabort = () => fail("upload aborted");
    xhr.send(file);
  });
}

async function uploadAll(/** @type {FileList | File[]} */ fileList) {
  for (const file of Array.from(fileList)) {
    await uploadOne(file);
    loadFiles();
  }
}

// Delete acts at once, no confirm: this is a transfer area, not storage.
/** @param {{file: ScratchFile}} props */
function FileRow({ file: f }) {
  const [busy, setBusy] = useState(false);
  const href = filesBase + "/" + encodeURIComponent(f.name);
  const onDelete = async () => {
    setBusy(true);
    try {
      await Cockpit.api(href, { method: "DELETE" });
    } catch (err) {
      if (!/** @type {Error} */ (err).message.startsWith("404")) alert(/** @type {Error} */ (err).message);
    }
    loadFiles();
  };
  return html`<li>
    <span class="file-name">${f.name}</span>
    <span class="muted file-meta">${humanSize(f.size)} · ${whenOf(f.uploadedAt)}</span>
    <span class="row-inline file-actions">
      <a class="btn secondary" href=${href} download=${f.name}>Download</a>
      <button type="button" class="danger" disabled=${busy} onClick=${onDelete}>Delete</button>
    </span>
  </li>`;
}

function FilesCard({ files, error, uploads }) {
  const [dragOver, setDragOver] = useState(false);
  const depth = useRef(0);
  const input = useRef(/** @type {HTMLInputElement | null} */ (null));

  // Drag & drop onto the card. dragenter/dragleave fire per child element, so count depth.
  const hasFiles = (/** @type {DragEvent} */ e) => Array.from(e.dataTransfer?.types || []).includes("Files");
  const onDragEnter = (/** @type {DragEvent} */ e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth.current++;
    setDragOver(true);
  };
  const onDragOver = (/** @type {DragEvent} */ e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = "copy";
  };
  const onDragLeave = () => {
    depth.current = Math.max(0, depth.current - 1);
    if (depth.current === 0) setDragOver(false);
  };
  const onDrop = (/** @type {DragEvent} */ e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth.current = 0;
    setDragOver(false);
    if (e.dataTransfer) uploadAll(e.dataTransfer.files);
  };
  const onPicked = () => {
    const el = input.current;
    if (!el || !el.files) return;
    const picked = Array.from(el.files);
    el.value = "";
    uploadAll(picked);
  };

  let list;
  if (error) list = html`<p class="run-error">${error}</p>`;
  else if (!files) list = html`<p class="muted">Loading…</p>`;
  else if (!files.length) list = html`<p class="muted">No files.</p>`;
  else list = html`<ul class="file-list">${files.map((/** @type {ScratchFile} */ f) => html`<${FileRow} key=${f.name} file=${f}/>`)}</ul>`;

  return html`
    <div class=${"card file-drop" + (dragOver ? " drag-over" : "")} id="files-card"
      onDragEnter=${onDragEnter} onDragOver=${onDragOver} onDragLeave=${onDragLeave} onDrop=${onDrop}>
      <h2>
        Files
        ${" "}<span class="row-inline">
          <button type="button" id="files-upload" onClick=${() => input.current && input.current.click()}>Upload</button>
          <button type="button" class="secondary" id="files-refresh" onClick=${() => loadFiles()}>Refresh</button>
        </span>
      </h2>
      <input type="file" id="files-input" multiple hidden ref=${input} onChange=${onPicked}/>
      <p class="muted" style="margin-top:0;">
        A drop for moving files between machines, kept on the cockpit host next to the note — the same
        rule: not in git, never read by any tool. Meant for temporary transfer: delete what you have
        fetched. Up to 200 MB per file; drop files anywhere on this card to upload.
      </p>
      <div id="files-uploads">${uploads.map((/** @type {UploadRow} */ r) =>
        html`<div key=${r.key} class=${"file-upload" + (r.error ? " run-error" : "")}>${r.name + " · " + r.text}</div>`)}</div>
      <div id="files-list">${list}</div>
    </div>`;
}

function Page({ state: s }) {
  return html`
    <${SummaryBar} project=${project} projects=${s.projects} summary=${s.summary} live=${s.live}/>
    <main>
      <${NoteCard} s=${s}/>
      <${FilesCard} files=${s.files} error=${s.filesError} uploads=${s.uploads}/>
    </main>`;
}

// ---- boot ----

const refreshSummary = startProjectChrome({ project, label: "scratchpad", chrome: state, rerender });
rerender();

document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "hidden") save();
  if (document.visibilityState === "visible") loadFiles();
});

// The scratchpad file lives outside every watched directory, so no SSE fires for it; concurrency
// is the hash check on save. The top bar still reflects this project's lane/watch changes
// (ontwerp-v0.5.md items 1–2).
Cockpit.live({
  project,
  onState: (/** @type {LiveState} */ live) => { state.live = live; rerender(); },
  handlers: [
    { kind: "lane", refetch: refreshSummary },
    { kind: "watch", refetch: refreshSummary },
  ],
});

loadScratchpad();
loadFiles();
