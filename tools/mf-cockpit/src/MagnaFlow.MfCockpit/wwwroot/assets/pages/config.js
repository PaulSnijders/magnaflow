// @ts-check
// config.html (docs/specs/cockpit/config.md). Page pattern (docs/specs/cockpit/index.md#page-rendering):
// server data and the editor live in `state`. The editor's text is page state rather than useState
// on purpose: it is the dirty-guarded source of truth that the live guard, Save, Reload and
// Overwrite all read or reset from outside the component. A refetch never overwrites it while it
// is dirty; it shows the "changed on disk" note instead.
import { html, render, useState, SummaryBar, ctrlEnter, startProjectChrome } from "../ui.js";
import * as Cockpit from "../app.js";

/** @typedef {import("../types.js").ProjectConfig} ProjectConfig */
/** @typedef {import("../types.js").ConfigSummary} ConfigSummary */
/** @typedef {import("../types.js").ConfigSaveResult} ConfigSaveResult */
/** @typedef {import("../types.js").CockpitConfig} CockpitConfig */
/** @typedef {import("../types.js").LaneCommand} LaneCommand */
/** @typedef {import("../types.js").WatchStatus} WatchStatus */
/** @typedef {import("../types.js").LogTail} LogTail */
/** @typedef {import("../types.js").ProjectSummary} ProjectSummary */
/** @typedef {import("../types.js").LiveState} LiveState */

const project = Cockpit.qs("p") || "";
const base = `/api/projects/${encodeURIComponent(project)}`;
document.title = "config · " + project;

const state = {
  /** @type {string[]} */ projects: [],
  /** @type {ProjectSummary | null} */ summary: null,
  /** @type {LiveState | null} */ live: null,
  /** @type {CockpitConfig | null} */ cockpit: null,
  /** @type {string | null} */ cockpitError: null,
  /** @type {string} */ cockpitLog: "",
  /** @type {ConfigSummary | null} */ configSummary: null,
  /** @type {string | null} */ configSummaryError: null,
  running: false,
  // The editor.
  /** @type {string | null} */ baseHash: null,
  text: "Loading…",
  placeholder: "",
  loaded: false,
  dirty: false,
  saving: false,
  diskNote: false,
  error: "",
  warnings: "",
  // Remove project.
  /** @type {string | null} */ watchNote: null,
};

const mount = /** @type {HTMLElement} */ (document.getElementById("app"));
function rerender() {
  render(html`<${Page} state=${state}/>`, mount);
}

// The cockpit's own state (docs/prompts/0001-cmd-cockpit-spawn-errors.md "Dashboard
// visibility"): which magnaflow.yml it loaded and what it resolved run.command/chat.command to,
// so a spawn failure elsewhere on this page ("could not start '...'") is self-explanatory instead
// of requiring a shell into the machine running mf-cockpit.
async function loadCockpitSummary() {
  try {
    state.cockpit = await Cockpit.api("/api/config");
  } catch (err) {
    state.cockpitError = /** @type {Error} */ (err).message;
  }
  rerender();
}

async function loadCockpitLog() {
  try {
    /** @type {LogTail} */
    const tail = await Cockpit.api("/api/cockpit/log?tail=100");
    state.cockpitLog = tail.exists ? (tail.lines.join("\n") || "(empty)") : "(no log yet)";
  } catch (err) {
    state.cockpitLog = /** @type {Error} */ (err).message;
  }
  rerender();
}

async function loadConfig() {
  try {
    /** @type {ProjectConfig} */
    const cfg = await Cockpit.api(`${base}/config`);
    state.baseHash = cfg.hash;
    state.text = cfg.content;
    state.loaded = true;
    state.dirty = false;
    state.diskNote = false;
    state.configSummary = cfg.summary;
    state.configSummaryError = null;
  } catch (err) {
    const message = /** @type {Error} */ (err).message;
    state.text = "";
    state.placeholder = message;
    state.error = message;
    state.configSummaryError = message;
  }
  rerender();
}

async function checkRunningBanner() {
  try {
    /** @type {LaneCommand[]} */
    const commands = await Cockpit.api(`${base}/commands`);
    state.running = commands.some((c) => c.status === "running");
    rerender();
  } catch {
    // informational only — never blocks the editor.
  }
}

async function save() {
  state.error = "";
  state.warnings = "";
  state.saving = true;
  rerender();
  try {
    /** @type {ConfigSaveResult} */
    const result = await Cockpit.api(`${base}/config`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content: state.text, baseHash: state.baseHash }),
    });
    state.baseHash = result.hash;
    if (result.warnings && result.warnings.length) state.warnings = "Warning: " + result.warnings.join("; ");
    await loadConfig();
  } catch (err) {
    state.error = /** @type {Error} */ (err).message;
  } finally {
    state.saving = false;
    rerender();
  }
}

const saveDisabled = () => !state.loaded || state.saving;
function saveIfEnabled() { if (!saveDisabled()) save(); }

// The conflict's two ways out. Save itself keeps its concurrency check (409 on a stale baseHash);
// Overwrite deliberately adopts the current disk hash first, then saves the editor's content.
async function overwrite() {
  state.error = "";
  rerender();
  try {
    /** @type {ProjectConfig} */
    const cfg = await Cockpit.api(`${base}/config`);
    state.baseHash = cfg.hash;
    saveIfEnabled();
  } catch (err) {
    state.error = /** @type {Error} */ (err).message;
    rerender();
  }
}

// Remove project — write #7 (ontwerp-v0.5.md item 4). Unregisters only; never deletes. If mf-watch
// is alive on this copy we do NOT block (the service outlives the cockpit forgetting the project)
// but the dialog says so and points at the Watcher card's Stop button.
async function watcherAliveNote() {
  try {
    /** @type {WatchStatus} */
    const watch = await Cockpit.api(`${base}/watch?tail=1`);
    state.watchNote = watch.running
      ? "Note: mf-watch is running for this project. Unregistering here does not stop it — " +
        "use the Watcher card's Stop button on the project page first if you want it stopped."
      : null;
    rerender();
    return watch.running;
  } catch { return false; }
}

function CockpitCard({ cockpit, error, log }) {
  return html`
    <div class="card">
      <h2>Cockpit</h2>
      <dl class="config-summary" id="cockpit-summary">${cockpit
        ? [html`<dt>Config file</dt>`, html`<dd><code>${cockpit.configPath}</code></dd>`,
          html`<dt>run.command</dt>`, html`<dd><code>${cockpit.runCommand}</code></dd>`,
          html`<dt>chat.command</dt>`, html`<dd><code>${cockpit.chatCommand}</code></dd>`]
        : html`<dt class="muted">${error || "Loading…"}</dt>`}</dl>
      <pre class="log-tail" id="cockpit-log">${log}</pre>
    </div>`;
}

const dash = html`<span class="muted">-</span>`;
const lines = (/** @type {string[]} */ items) => items.length ? items.map((x, i) => [i ? html`<br/>` : null, x]) : dash;

function SummaryCard({ summary, error }) {
  return html`
    <div class="card">
      <h2>Summary</h2>
      <dl class="config-summary" id="config-summary">${summary
        ? [html`<dt>Build</dt>`, html`<dd>${lines(summary.buildCommands)}</dd>`,
          html`<dt>Test</dt>`, html`<dd>${lines(summary.testCommands)}</dd>`,
          html`<dt>Max attempts</dt>`, html`<dd>${summary.maxAttempts}</dd>`,
          html`<dt>Run services</dt>`, html`<dd>${summary.runServices.length ? summary.runServices.join(", ") : dash}</dd>`]
        : html`<dt class="muted">${error || "Loading…"}</dt>`}</dl>
    </div>`;
}

function EditorCard({ s }) {
  const onInput = (/** @type {any} */ e) => { state.text = e.currentTarget.value; state.dirty = true; rerender(); };
  // Ctrl+Enter / Ctrl+S save (ontwerp-v0.5.md item 9).
  const onCtrlEnter = ctrlEnter(saveIfEnabled);
  const onKeyDown = (/** @type {KeyboardEvent} */ e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === "s") { e.preventDefault(); saveIfEnabled(); return; }
    onCtrlEnter(e);
  };
  return html`
    <div class="card">
      <h2>.magnaflow/config.yml</h2>
      <div id="config-warnings" class="muted">${s.warnings}</div>
      ${s.diskNote ? html`<div id="config-disk-note" class="muted">
        config.yml changed on disk — your unsaved edits are kept. Reload to discard them, or Overwrite
        to save them over the version on disk.
        <button type="button" id="config-reload-btn" class="secondary" onClick=${() => loadConfig()}>Reload</button>
        <button type="button" id="config-overwrite-btn" onClick=${overwrite}>Overwrite</button>
      </div>` : null}
      <textarea id="config-editor" rows="24" spellcheck="false" disabled=${!s.loaded}
        placeholder=${s.placeholder} value=${s.text} onInput=${onInput} onKeyDown=${onKeyDown}></textarea>
      <div class="row" style="margin-top:0.75rem; align-items:center;">
        <button id="config-save-btn" disabled=${saveDisabled()} title="Ctrl+Enter or Ctrl+S to save" onClick=${save}>Save</button>
        <span id="config-error" class="muted">${s.error}</span>
      </div>
    </div>`;
}

function RemoveCard({ watchNote }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const onClick = async () => {
    setError("");
    const watcherAlive = await watcherAliveNote();
    let message = `Unregister "${project}" from the cockpit?\n\n` +
      "This only removes it from magnaflow.yml. The working copy, its git history and its " +
      ".magnaflow/ stay on disk untouched — nothing is deleted. You can re-add it later via " +
      "Add project → Existing.";
    if (watcherAlive) {
      message += "\n\nmf-watch is still running for this project and will keep running — stop it " +
        "from the Watcher card first if you want it stopped.";
    }
    if (!confirm(message)) return;
    setBusy(true);
    try {
      await Cockpit.api(base, { method: "DELETE" });
      location.href = "index.html";
    } catch (err) {
      setError(/** @type {Error} */ (err).message);
      setBusy(false);
    }
  };
  return html`
    <div class="card danger-zone">
      <h2>Remove project</h2>
      <p class="muted" style="margin-top:0;">
        Unregisters <strong id="remove-name">${project}</strong> from the cockpit. Its working copy, git history
        and <code>.magnaflow/</code> stay on disk untouched — nothing is deleted. Re-add it later with
        <em>Add project → Existing</em>.
      </p>
      ${watchNote ? html`<div id="remove-watch-note" class="muted">${watchNote}</div>` : null}
      <div class="row" style="align-items:center;">
        <button id="remove-project-btn" class="danger" style="flex:0 0 auto;" disabled=${busy} onClick=${onClick}>Remove project</button>
        <span id="remove-error" class="muted">${error}</span>
      </div>
    </div>`;
}

function Page({ state: s }) {
  return html`
    <${SummaryBar} project=${project} projects=${s.projects} summary=${s.summary} live=${s.live}/>
    <main>
      ${s.running ? html`<div class="busy-banner" id="running-banner">
        A command in this project is currently <strong>running</strong> — the worker already read
        the config for this run; a save here takes effect on the <em>next</em> run.
      </div>` : null}
      <${CockpitCard} cockpit=${s.cockpit} error=${s.cockpitError} log=${s.cockpitLog}/>
      <${SummaryCard} summary=${s.configSummary} error=${s.configSummaryError}/>
      <${EditorCard} s=${s}/>
      <${RemoveCard} watchNote=${s.watchNote}/>
    </main>`;
}

// ---- boot ----

const refreshSummary = startProjectChrome({ project, label: "config", chrome: state, rerender });
rerender();

// Never clobber unsaved input (ontwerp-v0.5.md item 1): while the editor is dirty, skip reloading
// it from disk and show a "changed on disk" notice instead. config.yml changes surface as the
// coarse "evidence" kind (it lives under .magnaflow/).
Cockpit.live({
  project,
  onState: (/** @type {LiveState} */ live) => { state.live = live; rerender(); },
  handlers: [
    { kind: "lane", refetch: () => checkRunningBanner() },
    { kind: "lane", refetch: refreshSummary },
    { kind: "watch", refetch: refreshSummary },
    {
      kind: "evidence",
      refetch: loadConfig,
      guard: () => state.dirty,
      onGuardedSkip: () => { state.diskNote = true; rerender(); },
    },
  ],
});

loadConfig();
checkRunningBanner();
loadCockpitSummary();
loadCockpitLog();
watcherAliveNote();
