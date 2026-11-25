// @ts-check
// index.html, the front door (docs/specs/cockpit/index.md). Same page pattern as project.js:
// server data in `state`, loaders assign and rerender(); dialog contents, tabs and unsaved input
// are useState in the component that owns them.
import {
  html, render, useState, useEffect, useLayoutEffect, useRef, StatusPill, LiveBadge, SoundToggle, Dialog, ctrlEnter,
} from "../ui.js";
import * as Cockpit from "../app.js";

/** @typedef {import("../types.js").ProjectSummary} ProjectSummary */
/** @typedef {import("../types.js").LiveState} LiveState */
/** @typedef {import("../types.js").RunStatus} RunStatus */
/** @typedef {import("../types.js").GitInfo} GitInfo */
/** @typedef {import("../types.js").NewProjectInfo} NewProjectInfo */
/** @typedef {import("../types.js").BrowseListing} BrowseListing */
/** @typedef {import("../types.js").AddProjectResult} AddProjectResult */
/** @typedef {{text: string, cls?: string, title?: string}} Cell */

const state = {
  /** @type {ProjectSummary[] | null} */ projects: null,
  /** Only the first load's failure is shown (in the attention list), as before. @type {string | null} */
  loadError: null,
  /** Branch cell per project, fetched after the table. @type {Record<string, Cell>} */
  branchFor: {},
  /** Run cell per project, fetched after the table. @type {Record<string, Cell>} */
  runFor: {},
  /** @type {LiveState | null} */ live: null,
};

const mount = /** @type {HTMLElement} */ (document.getElementById("app"));
const headerNav = /** @type {HTMLElement} */ (document.getElementById("header-nav"));
function rerender() {
  render(html`<${Page} state=${state}/>`, mount);
  // Index header: sound toggle before the live badge (ontwerp-sound-cues.md). The project pages
  // carry theirs in the summary bar.
  render(html`<${SoundToggle}/><${LiveBadge} live=${state.live}/>`, headerNav);
}

// Announce terminal transitions of each project's newest command (the `latest` field, v0.5 item
// 6). The tracker is silent on the first load and only chimes on an observed change.
const cues = Cockpit.cueTracker();

async function load() {
  const projects = /** @type {ProjectSummary[]} */ (await Cockpit.api("/api/projects"));
  state.projects = projects;
  rerender();
  cues.observe(projects
    .filter((p) => p.latest)
    .map((p) => ({ project: p.name, id: p.latest?.id, status: p.latest?.status })));
  // Slow half, fetched separately per project so an expensive `git status` (or an mf-run status
  // spawn) never delays the fast counts/attention data above.
  projects.forEach((p) => {
    loadGitBranch(p.name);
    loadRunChip(p.name);
  });
}

async function loadGitBranch(/** @type {string} */ name) {
  try {
    const info = /** @type {GitInfo} */ (await Cockpit.api(`/api/projects/${encodeURIComponent(name)}/git`));
    state.branchFor[name] = { text: (info.branch || "-") + (info.dirty ? " *" : "") };
  } catch {
    state.branchFor[name] = { text: "?" };
  }
  rerender();
}

async function loadRunChip(/** @type {string} */ name) {
  try {
    const status = /** @type {RunStatus} */ (await Cockpit.api(`/api/projects/${encodeURIComponent(name)}/run`));
    if (!status.configured || !status.services.length) {
      state.runFor[name] = { text: "-", cls: "muted" };
    } else {
      const running = status.services.filter((s) => s.running).length;
      // Reachable URLs, host-rewritten (ontwerp-v0.5.md item 3): a hover hint from the index so
      // localhost-bound services still show the address that works from this browser.
      const urls = status.services.filter((s) => s.url).map((s) => `${s.name}: ${Cockpit.displayUrl(s.url)}`);
      state.runFor[name] = {
        text: `${running}/${status.services.length}`,
        cls: running === status.services.length ? "" : "muted",
        title: urls.length ? urls.join("\n") : undefined,
      };
    }
  } catch {
    state.runFor[name] = { text: "?", cls: "muted" };
  }
  rerender();
}

// ---- cards ----

function AttentionCard({ /** @type {ProjectSummary[] | null} */ projects, /** @type {string | null} */ error }) {
  let items;
  if (!projects) {
    items = html`<li class="muted">${error || "Loading…"}</li>`;
  } else {
    const all = projects.flatMap((p) => p.attention.map((a) => ({ ...a, project: p.name })));
    items = all.length
      ? all.map((a) => html`
        <li>
          <${StatusPill} status=${a.status}/>
          ${" "}<a href=${`command.html?p=${encodeURIComponent(a.project)}&id=${encodeURIComponent(a.id)}`}>${a.project} / ${a.title}</a>
          ${" "}<span class="muted">${a.reason}</span>
        </li>`)
      : html`<li class="muted">Nothing needs attention.</li>`;
  }
  return html`
    <div class="card">
      <h2>Attention</h2>
      <ul class="attention-list" id="attention-list">${items}</ul>
    </div>`;
}

// The highest-id lane command (ontwerp-v0.5.md item 6): id · truncated title · status pill.
function LatestCell({ /** @type {string} */ name, /** @type {import("../types.js").CommandRef | null} */ latest }) {
  if (!latest) return html`<span class="muted">-</span>`;
  const title = latest.title.length > 32 ? latest.title.slice(0, 31) + "…" : latest.title;
  return [
    html`<a href=${`command.html?p=${encodeURIComponent(name)}&id=${encodeURIComponent(latest.id)}`}>${latest.id}</a>`,
    " ", html`<span class="muted">${title}</span>`, " ", html`<${StatusPill} status=${latest.status}/>`,
  ];
}

/** @param {{projects: ProjectSummary[] | null, branchFor: Record<string, Cell>, runFor: Record<string, Cell>, onAdd: () => void}} props */
function ProjectsCard({ projects, branchFor, runFor, onAdd }) {
  let rows = null;
  if (projects && !projects.length) {
    rows = html`<tr><td colspan="11" class="muted">No projects yet — <button type="button" id="add-project-empty-btn" class="secondary" onClick=${onAdd}>+ Add project</button></td></tr>`;
  } else if (projects) {
    rows = projects.map((p) => {
      const branch = branchFor[p.name];
      const run = runFor[p.name];
      return html`
        <tr key=${p.name}>
          <td><a href=${"project.html?p=" + encodeURIComponent(p.name)}>${p.name}</a>${p.exists ? null : [" ", html`<span class="muted">(path missing)</span>`]}</td>
          <td class="muted">${branch ? branch.text : "…"}</td>
          <td>${p.counts.draft}</td>
          <td>${p.counts.ready}</td>
          <td>${p.counts.running}</td>
          <td>${p.counts.questions}</td>
          <td>${p.counts.done}</td>
          <td>${p.counts.aborted}</td>
          <td>${p.watcherAlive ? html`<span class="pill ready">alive</span>` : html`<span class="muted">-</span>`}</td>
          <td class=${run ? run.cls : "muted"} title=${run ? run.title : undefined}>${run ? run.text : "…"}</td>
          <td><${LatestCell} name=${p.name} latest=${p.latest}/></td>
        </tr>`;
    });
  }
  return html`
    <div class="card">
      <h2>
        Projects
        <span class="row-inline"><button id="add-project-btn" class="secondary" onClick=${onAdd}>+ Add project</button></span>
      </h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Project</th><th>Branch</th><th>Draft</th><th>Ready</th><th>Running</th>
              <th>Questions</th><th>Done</th><th>Aborted</th><th>Watcher</th><th>Run</th><th>Latest</th>
            </tr>
          </thead>
          <tbody id="projects-body">${rows}</tbody>
        </table>
      </div>
    </div>`;
}

// ---- Add project dialog ----

function sanitizeDirName(/** @type {string} */ name) {
  const stripped = (name || "").trim().replace(/[<>:"/\\|?*\x00-\x1f]/g, "");
  return stripped.replace(/\s+/g, "-").replace(/-{2,}/g, "-").replace(/^-+|-+$/g, "");
}

function lastPathSegment(/** @type {string} */ path) {
  const parts = path.split(/[\\/]+/).filter(Boolean);
  const last = parts.length ? parts[parts.length - 1] : "";
  return /^[A-Za-z]:$/.test(last) && parts.length === 1 ? "" : last; // a drive root has no name
}

/**
 * Path field plus Browse (docs/specs/cockpit/index.md "Browse"): a server-side folder picker over
 * the cockpit's own machine, inline under Path. The Path field stays the request's source of truth.
 * @param {{path: string, setPath: (v: string) => void, name: string, setName: (v: string) => void,
 *   onKeyDown: (e: KeyboardEvent) => void, submit: () => void}} props
 */
function PathPicker({ path, setPath, name, setName, onKeyDown, submit }) {
  const [open, setOpen] = useState(false);
  const [listing, setListing] = useState(/** @type {BrowseListing | null} */ (null));
  const [error, setError] = useState("");
  const pathRef = useRef(/** @type {HTMLInputElement | null} */ (null));
  const listRef = useRef(/** @type {HTMLUListElement | null} */ (null));
  const focusPath = useRef(false);

  useLayoutEffect(() => { if (listRef.current) listRef.current.scrollTop = 0; }, [listing]);
  useEffect(() => {
    if (focusPath.current && pathRef.current) { focusPath.current = false; pathRef.current.focus(); }
  });

  const load = async (/** @type {string} */ query) => {
    setError("");
    try {
      setListing(await Cockpit.api("/api/browse?" + query));
    } catch (err) {
      const e = /** @type {any} */ (err);
      setError((e.body && e.body.error) || e.message);
    }
  };
  const browseTo = (/** @type {string} */ p) => load("path=" + encodeURIComponent(p));

  const close = () => { setOpen(false); setListing(null); setError(""); };
  const toggle = () => {
    if (open) { close(); return; }
    setOpen(true);
    load("hint=" + encodeURIComponent(path.trim()));
  };
  const use = () => {
    if (!listing) return;
    setPath(listing.path);
    if (!name.trim()) setName(lastPathSegment(listing.path));
    close();
    focusPath.current = true;
  };
  // Ctrl+Enter submits from inside the panel too; a plain Enter there never submits the form
  // (on a panel button it just clicks that button).
  const panelKey = (/** @type {KeyboardEvent} */ e) => {
    if (e.key !== "Enter") return;
    if (e.ctrlKey || e.metaKey) {
      e.preventDefault();
      submit();
    } else if (!(/** @type {HTMLElement} */ (e.target)).closest("button")) {
      e.preventDefault();
    }
  };

  return html`
    <div class="field">
      <label for="existing-path">Path</label>
      <div class="browse-path-row">
        <input type="text" id="existing-path" ref=${pathRef} placeholder="absolute, or relative to new_project.root"
          value=${path} onInput=${(/** @type {any} */ e) => setPath(e.currentTarget.value)} onKeyDown=${onKeyDown}/>
        <button type="button" id="browse-toggle" class="secondary" aria-expanded=${String(open)} aria-controls="browse-panel" onClick=${toggle}>Browse…</button>
      </div>
      <div id="browse-panel" class="browse-panel" style=${open ? "" : "display:none;"} onKeyDown=${panelKey}>
        <div id="browse-current" class="browse-current">${listing ? listing.path : "-"}</div>
        <div class="browse-toolbar">
          <button type="button" id="browse-up" class="secondary browse-small" disabled=${!(listing && listing.parent)}
            onClick=${() => listing && listing.parent && browseTo(listing.parent)}>Up</button>
          <span id="browse-roots" class="browse-roots">${listing ? listing.roots.map((r) => html`
            <button type="button" class="secondary browse-small" onClick=${() => browseTo(r)}>${r}</button>`) : null}</span>
        </div>
        <ul id="browse-list" class="browse-list" ref=${listRef}>${listing
          ? (listing.dirs.length
            ? listing.dirs.map((d) => html`
              <li key=${d.path}><button type="button" class="browse-dir" onClick=${() => browseTo(d.path)}><span class="browse-name">${d.name}</span>${d.git ? [" ", html`<span class="pill done">git</span>`] : null}</button></li>`)
            : html`<li class="muted browse-empty">No subfolders.</li>`)
          : null}</ul>
        <div id="browse-error" class="dialog-error">${error}</div>
        <div class="browse-toolbar browse-actions">
          <button type="button" id="browse-use" class="secondary" disabled=${!listing} onClick=${use}>Use this folder</button>
        </div>
      </div>
    </div>`;
}

/**
 * "+ Add project" (ontwerp-v0.4.md "The dialog"). Remounted on every open (the page bumps its
 * key), so each open starts on the Existing tab with empty fields, as the reset did before.
 * @param {{open: boolean, onClose: () => void}} props
 */
function AddProjectDialog({ open, onClose }) {
  const [tab, setTab] = useState("existing");
  const [existingName, setExistingName] = useState("");
  const [existingPath, setExistingPath] = useState("");
  const [newName, setNewName] = useState("");
  const [template, setTemplate] = useState("");
  const [info, setInfo] = useState(/** @type {NewProjectInfo | null} */ (null));
  const [infoError, setInfoError] = useState("");
  const [error, setError] = useState("");
  const [output, setOutput] = useState("");
  const [warnings, setWarnings] = useState(/** @type {{lead: string, items: string[]} | null} */ (null));
  // Set after a successful add that came with warnings: the dialog stays open to show them and the
  // submit button becomes "Open project".
  const [added, setAdded] = useState(/** @type {string | null} */ (null));
  const [submitting, setSubmitting] = useState(false);
  const formRef = useRef(/** @type {HTMLFormElement | null} */ (null));

  useEffect(() => {
    if (!open) return;
    Cockpit.api("/api/new-project")
      .then((/** @type {NewProjectInfo} */ i) => { setInfo(i); setInfoError(""); })
      .catch((/** @type {Error} */ err) => setInfoError(err.message));
  }, [open]);

  const submitForm = () => { if (formRef.current) formRef.current.requestSubmit(); };
  // Ctrl+Enter submits (ontwerp-v0.5.md item 9); the native <dialog> already closes on Esc.
  const onKey = ctrlEnter(submitForm);

  const submit = async (/** @type {Event} */ e) => {
    e.preventDefault();
    if (added) {
      location.href = "project.html?p=" + encodeURIComponent(added);
      return;
    }
    setAdded(null);
    setError("");
    setOutput("");
    setWarnings(null);

    let body;
    if (tab === "existing") {
      const name = existingName.trim();
      const path = existingPath.trim();
      if (!name || !path) { setError("name and path are required"); return; }
      body = { mode: "existing", name, path };
    } else {
      const name = newName.trim();
      if (!name) { setError("name is required"); return; }
      body = { mode: "new", name, template: template || null };
    }

    setSubmitting(true);
    try {
      /** @type {AddProjectResult} */
      const result = await Cockpit.api("/api/projects", {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
      });
      if (result.warnings && result.warnings.length) {
        // Registered, but with something to know (no .git, spec kit not copied): show it first.
        setAdded(result.name);
        setWarnings({ lead: `Added "${result.name}" with warnings:`, items: result.warnings });
        return;
      }
      onClose();
      location.href = "project.html?p=" + encodeURIComponent(result.name);
    } catch (err) {
      // A failed scaffold (422) also returns the template's captured output and the directory it
      // left in place; show both rather than discarding them.
      const b = /** @type {any} */ (err).body || {};
      setError(/** @type {Error} */ (err).message + (b.path ? ` (left in place: ${b.path})` : ""));
      if (b.output) setOutput(b.output);
      if (b.warnings && b.warnings.length) setWarnings({ lead: "Warnings:", items: b.warnings });
    } finally {
      setSubmitting(false);
    }
  };

  const root = info && info.root;
  const dirName = sanitizeDirName(newName);
  const preview = root && dirName ? `${root.replace(/[\\/]+$/, "")}${info?.separator}${dirName}` : "-";
  const hint = infoError || (info && !info.root ? "Add cockpit.new_project.root to magnaflow.yml to create new projects here." : "");

  const tabBtn = (/** @type {string} */ t, /** @type {string} */ label) => html`
    <button type="button" class="segmented-btn tab-btn" aria-pressed=${String(tab === t)} onClick=${() => setTab(t)}>${label}</button>`;

  return html`
    <${Dialog} id="add-project-dialog" open=${open} onClose=${onClose}>
      <form id="add-project-form" ref=${formRef} onSubmit=${submit}>
        <div class="dialog-header">
          <h2>Add project</h2>
          <button type="button" id="add-project-close" class="icon-btn" aria-label="Close" onClick=${onClose}>×</button>
        </div>

        <div class="segmented" role="tablist">
          ${tabBtn("existing", "Existing")}
          ${tabBtn("new", "New")}
        </div>

        <div id="tab-existing" class="add-project-tab" style=${tab === "existing" ? "" : "display:none;"}>
          <div class="field"><label for="existing-name">Name</label><input type="text" id="existing-name" placeholder="my-project"
            value=${existingName} onInput=${(/** @type {any} */ e) => setExistingName(e.currentTarget.value)} onKeyDown=${onKey}/></div>
          <${PathPicker} path=${existingPath} setPath=${setExistingPath} name=${existingName} setName=${setExistingName}
            onKeyDown=${onKey} submit=${submitForm}/>
        </div>

        <div id="tab-new" class="add-project-tab" style=${tab === "new" ? "" : "display:none;"}>
          <div id="new-project-hint" class="muted" style=${hint ? "" : "display:none;"}>${hint}</div>
          <div id="new-project-fields" style=${hint ? "display:none;" : ""}>
            <div class="field"><label for="new-name">Name</label><input type="text" id="new-name" placeholder="my-project"
              value=${newName} onInput=${(/** @type {any} */ e) => setNewName(e.currentTarget.value)} onKeyDown=${onKey}/></div>
            <div class="field">
              <label for="new-template">Template</label>
              <select id="new-template" onChange=${(/** @type {any} */ e) => setTemplate(e.currentTarget.value)}>
                <option value="" selected=${template === ""}>(empty)</option>
                ${(info ? info.templates : []).map((t) => html`<option key=${t} value=${t} selected=${t === template}>${t}</option>`)}
              </select>
            </div>
            <div class="field">
              <label>Target path</label>
              <div class="muted target-preview" id="new-target-preview">${preview}</div>
            </div>
          </div>
        </div>

        <div id="add-project-error" class="dialog-error">${error}</div>
        <pre id="add-project-output" class="dialog-output" style=${output ? "" : "display:none;"}>${output}</pre>
        <div id="add-project-warnings" class="muted" style=${warnings ? "" : "display:none;"}>${warnings
          ? [warnings.lead, html`<ul>${warnings.items.map((w) => html`<li>${w}</li>`)}</ul>`]
          : null}</div>
        <div class="dialog-footer">
          <button type="button" id="add-project-cancel" class="secondary" onClick=${onClose}>Cancel</button>
          <button type="submit" id="add-project-submit" title="Ctrl+Enter to add" disabled=${submitting}>${added ? "Open project" : "Add project"}</button>
        </div>
      </form>
    <//>`;
}

// ---- page ----

function Page({ state: s }) {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [dialogKey, setDialogKey] = useState(0);
  const openDialog = () => { setDialogKey(dialogKey + 1); setDialogOpen(true); };
  return html`
    <main>
      <${AttentionCard} projects=${s.projects} error=${s.loadError}/>
      <${ProjectsCard} projects=${s.projects} branchFor=${s.branchFor} runFor=${s.runFor} onAdd=${openDialog}/>
    </main>
    <${AddProjectDialog} key=${dialogKey} open=${dialogOpen} onClose=${() => setDialogOpen(false)}/>`;
}

// ---- boot ----

rerender();

// index.html refetches the whole project list on any project-affecting event (cheap, no payloads
// over SSE either way). project: null → no per-project filtering; the live marker is LiveBadge.
const loadSafely = () => load().catch(() => {});
Cockpit.live({
  project: null,
  onState: (/** @type {LiveState} */ live) => { state.live = live; rerender(); },
  handlers: [
    { kind: "lane", refetch: loadSafely },
    { kind: "projects", refetch: loadSafely },
    { kind: "watch", refetch: loadSafely },
    { kind: "run", refetch: loadSafely },
  ],
});
load().catch((err) => { state.loadError = err.message; rerender(); });
