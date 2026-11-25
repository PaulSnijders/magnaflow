// @ts-check
// project.html (docs/specs/cockpit/project.md). Page pattern (docs/specs/cockpit/index.md#page-rendering):
// server data lives in the one `state` object below; every loader keeps its endpoint and trigger,
// assigns into `state` and calls rerender(). Pure UI state (expanded lists, open logs, unsaved
// input, in-flight buttons) is useState inside the card that owns it, so a refetch never has to
// carry it. Derived controls (Switch, Pull/Sync, Commit all, Promote, Run) are computed in render.
import {
  html, render, useState, useEffect, SummaryBar, Breadcrumb, StatusPill, ctrlEnter,
} from "../ui.js";
import * as Cockpit from "../app.js";

/** @typedef {import("../types.js").LaneCommand} LaneCommand */
/** @typedef {import("../types.js").GitInfo} GitInfo */
/** @typedef {import("../types.js").GitBranches} GitBranches */
/** @typedef {import("../types.js").RunStatus} RunStatus */
/** @typedef {import("../types.js").RunService} RunService */
/** @typedef {import("../types.js").StableService} StableService */
/** @typedef {import("../types.js").WatchStatus} WatchStatus */
/** @typedef {import("../types.js").ActionsStatus} ActionsStatus */
/** @typedef {import("../types.js").Action} Action */
/** @typedef {import("../types.js").ActionLast} ActionLast */
/** @typedef {import("../types.js").ProjectSummary} ProjectSummary */
/** @typedef {import("../types.js").LiveState} LiveState */
/** @typedef {{text: string, cls: string}} ResultLine */

const project = Cockpit.qs("p");
const base = `/api/projects/${encodeURIComponent(project)}`;
const JSON_POST = (/** @type {any} */ body) => ({
  method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
});

const state = {
  /** @type {string[]} */ projects: [],
  /** @type {ProjectSummary | null} */ summary: null,
  /** @type {LiveState | null} */ live: null,
  /** @type {LaneCommand[] | null} */ lane: null,
  /** @type {GitInfo | null} */ git: null,
  /** @type {string | null} */ gitError: null,
  /** @type {GitBranches | null} */ branches: null,
  /** Under the branch select: a fetch error, "fetched", or the switch's own result. @type {ResultLine | null} */
  switchResult: null,
  /** @type {RunStatus | null} */ run: null,
  /** @type {string | null} */ runError: null,
  /** @type {WatchStatus | null} */ watch: null,
  /** @type {ActionsStatus | null} */ actions: null,
};

const mount = /** @type {HTMLElement} */ (document.getElementById("app"));
function rerender() {
  render(html`<${Page} state=${state}/>`, mount);
}

// ---- loaders: same endpoints and triggers as before the Preact move ----

// Status first, so a background tab answers "is it still working?" without being opened; kept in
// step with the lane by the summary bar's refresh (docs/prompts/0010's rst, "Follow-up").
document.title = project || "?";

function setSummary(/** @type {ProjectSummary} */ s) {
  state.summary = s;
  try { document.title = Cockpit.projectTitle(s, project || "?"); } catch { /* a title is never worth breaking the bar */ }
  rerender();
}

// Live refreshes go straight to the fast per-project summary; /api/overview is a page-load thing.
async function refreshSummary() {
  try { setSummary(await Cockpit.api(base)); } catch { /* keep last */ }
}

// Announce terminal transitions over this project's full lane (ontwerp-sound-cues.md). Silent on
// the first render; at most one cue per refetch, priority aborted > questions > done.
const cues = Cockpit.cueTracker();

async function loadLane() {
  const lane = /** @type {LaneCommand[]} */ (await Cockpit.api(`${base}/commands`));
  cues.observe(lane.map((c) => ({ project, id: c.id, status: c.status })));
  state.lane = lane;
  rerender();
}

// The slow half (at least one `git` subprocess spawn): loaded on its own, never on the live fast
// path, so it never delays the lane/watcher data and a git error never blocks the rest.
async function loadGit() {
  try {
    state.git = await Cockpit.api(`${base}/git`);
    state.gitError = null;
  } catch (err) {
    state.gitError = /** @type {Error} */ (err).message;
  }
  rerender();
}

// One local ref read, so not on the git TTL cache and not on the live refresh: branches change
// when a human changes them. `fetchFirst` is ↻, the only control in this card on the network.
async function loadBranches(/** @type {boolean} */ fetchFirst) {
  try {
    const data = /** @type {GitBranches} */ (await Cockpit.api(`${base}/git/branches${fetchFirst ? "?fetch=true" : ""}`));
    state.branches = data;
    // The list is still the truth about this working copy; only the network half failed.
    if (data.fetchError) state.switchResult = { text: `fetch: ${data.fetchError}`, cls: "run-result run-error" };
    else if (fetchFirst) state.switchResult = { text: "fetched", cls: "run-result muted" };
  } catch (err) {
    state.switchResult = { text: /** @type {Error} */ (err).message, cls: "run-result run-error" };
  }
  rerender();
}

async function loadRun() {
  try {
    state.run = await Cockpit.api(`${base}/run`);
    state.runError = null;
  } catch (err) {
    state.runError = /** @type {Error} */ (err).message;
  }
  rerender();
}

async function loadWatch() {
  const watch = /** @type {WatchStatus} */ (await Cockpit.api(`${base}/watch?tail=100`));
  // TailReader splits on '\n', so a log ending in a newline hands us a trailing empty line —
  // invisible in a 100-line block, but it would eat one of the ten visible slots.
  state.watch = { ...watch, log: { ...watch.log, lines: trimTrailingEmpty(watch.log.lines) } };
  rerender();
}

async function loadActions() {
  state.actions = await Cockpit.api(`${base}/actions`);
  rerender();
}

function trimTrailingEmpty(/** @type {string[]} */ lines) {
  const out = lines.slice();
  while (out.length && out[out.length - 1] === "") out.pop();
  return out;
}

// ---- helpers ----

// m:ss under an hour (0:23, 5:07), h:mm:ss above; null renders an empty cell.
function formatDuration(/** @type {number | null} */ seconds) {
  if (seconds == null) return "";
  const pad = (/** @type {number} */ n) => String(n).padStart(2, "0");
  const h = Math.floor(seconds / 3600), m = Math.floor(seconds % 3600 / 60), s = seconds % 60;
  return h ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

const exitLine = (/** @type {import("../types.js").ProcessResult} */ r) =>
  `exit ${r.exitCode}${r.timedOut ? " (timed out)" : ""} — ${r.output || "(no output)"}`;

const ResultText = ({ /** @type {ResultLine | null} */ line, /** @type {string | undefined} */ id }) =>
  html`<div id=${id} class=${line ? line.cls : "run-result muted"}>${line ? line.text : ""}</div>`;

// ---- action row ----

// "Check now" (docs/prompts/0008 item 3, moved to the action row by 0012): one empty file, consumed
// by the watcher within a second or so. Enabled only while a watcher runs.
function CheckNow({ /** @type {WatchStatus | null} */ watch }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const click = async () => {
    setError("");
    setBusy(true);
    try {
      const result = await Cockpit.api(`${base}/watch/check-now`, { method: "POST" });
      if (!result.success) setError(result.error || "check-now failed");
      await loadWatch();
      // The wake is consumed a beat *after* the POST returns, and an append to an open
      // mf-watch.log does not reliably raise a file event on Windows: one late refetch.
      setTimeout(() => loadWatch().catch(() => {}), 2000);
    } catch (err) {
      setError(/** @type {Error} */ (err).message);
    } finally {
      setBusy(false);
    }
  };
  return html`
    <div class="action-row">
      <button id="watch-check-now" class="secondary" onClick=${click} disabled=${busy || !(watch && watch.running)}
        title="Write .magnaflow/mf-watch.wake — a running watcher polls now instead of finishing its backed-off sleep">Check now</button>
      <span id="check-now-error" class="muted">${error}</span>
    </div>`;
}

// ---- Run card ----

// Combines mf-run's two independent status facts — tracked (running) and port_listening — into
// the four card states (docs/prompts/0003).
function runState(/** @type {RunService} */ s) {
  const listening = s.portListening; // true | false | null (no url to probe)
  if (s.running) {
    return listening === false
      ? { dot: "warning", text: "process up, port not answering" + (s.pid ? " (pid " + s.pid + ")" : "") }
      : { dot: "running", text: "running" + (s.pid ? " (pid " + s.pid + ")" : "") };
  }
  return listening === true
    ? { dot: "running", text: "running (not started by mf-run)" }
    : { dot: "", text: "stopped" };
}

const STABLE_STATE_PILL = /** @type {Record<string, string>} */ ({ building: "running", ready: "done", failed: "aborted" });

/**
 * One service row, or the stable row when `stable` is set. The open log refetches whenever the
 * run status does (an SSE "run" event fires continuously while a service writes its log).
 * @param {{svc: RunService | StableService, stable?: boolean, head?: string | null, laneRunning?: boolean}} props
 */
function RunServiceRow({ svc, stable, head, laneRunning }) {
  const [expanded, setExpanded] = useState(false);
  const [log, setLog] = useState("");
  const [result, setResult] = useState(/** @type {ResultLine | null} */ (null));
  const [busy, setBusy] = useState(/** @type {string | null} */ (null));

  useEffect(() => {
    if (!expanded) return;
    let stale = false;
    Cockpit.api(`${base}/run/${encodeURIComponent(svc.name)}/log?tail=100`)
      .then((/** @type {import("../types.js").LogTail} */ tail) => { if (!stale) setLog(tail.exists ? (tail.lines.join("\n") || "(empty)") : "(no log yet)"); })
      .catch((/** @type {Error} */ err) => { if (!stale) setLog(err.message); });
    return () => { stale = true; };
  }, [svc, expanded]);

  const toggleLog = () => {
    if (!expanded) setLog("Loading…");
    setExpanded(!expanded);
  };

  const act = async (/** @type {string} */ action) => {
    setBusy(action);
    setResult({ text: `${action}ing…`, cls: "run-result muted" });
    try {
      const r = await Cockpit.api(`${base}/run/${encodeURIComponent(svc.name)}/${action}`, { method: "POST" });
      setResult({ text: `${action}: ${exitLine(r)}`, cls: "run-result " + (r.success ? "muted" : "run-error") });
      await loadRun();
    } catch (err) {
      setResult({ text: /** @type {Error} */ (err).message, cls: "run-result run-error" });
    } finally {
      setBusy(null);
    }
  };

  const promote = async () => {
    setBusy("promote");
    setResult({ text: "starting promote…", cls: "run-result muted" });
    try {
      const r = await Cockpit.api(`${base}/run/stable/promote`, { method: "POST" });
      setResult({ text: r.output || "promote started", cls: "run-result " + (r.success ? "muted" : "run-error") });
      // The row moves to building on the "run" event for mf-run's state.yml write.
      if (r.success) await loadRun();
    } catch (err) {
      setResult({ text: /** @type {Error} */ (err).message, cls: "run-result run-error" });
    } finally {
      setBusy(null);
    }
  };

  const proc = runState(svc);
  const procText = proc.text + (svc.reason ? ` — ${svc.reason}` : "");
  const buttons = ["start", "stop", "restart"].map((action) => html`
    <button class="secondary run-btn" disabled=${busy === action} onClick=${() => act(action)}>${action[0].toUpperCase() + action.slice(1)}</button>`);

  let link = null, extra = null, message = null, promoteBtn = null;
  if (!stable) {
    if (svc.url) {
      const shown = Cockpit.displayUrl(svc.url);
      link = html`<a href=${shown} title=${"configured: " + svc.url} target="_blank" rel="noopener noreferrer">${shown}</a>`;
    }
  } else {
    // The stable instance (mf-run.md#stable-instance): Behind compares the promoted sha with the
    // HEAD that came with the same run response; the link is `link` verbatim, else `url` rewritten.
    const s = /** @type {StableService} */ (svc);
    extra = [
      s.state
        ? html`<span class=${"pill " + (STABLE_STATE_PILL[s.state] || "unknown")}>${s.state}</span>`
        : html`<span class="muted">never promoted</span>`,
      s.sha ? [" ", html`<code title=${s.sha}>${s.sha.slice(0, 7)}</code>`,
        s.dirty ? [" ", html`<span class="pill aborted" title="published from a tree with uncommitted changes">dirty</span>`] : null] : null,
      s.sha && head && s.sha !== head
        ? [" ", html`<span class="pill questions" title=${`stable runs ${s.sha.slice(0, 7)}; HEAD is ${head.slice(0, 7)}`}>behind</span>`]
        : null,
      s.at ? [" ", html`<span class="muted">${Cockpit.formatDate(s.at)}</span>`] : null,
    ];
    const href = s.link || (s.url ? Cockpit.displayUrl(s.url) : null);
    if (href) link = html`<a href=${href} title=${s.link ? "configured link" : "configured: " + s.url} target="_blank" rel="noopener noreferrer">${href}</a>`;
    message = s.state === "failed" && s.message ? html`<div class="run-error">${s.message}</div>` : null;
    const block = s.state === "building"
      ? "a promote is already building"
      : (laneRunning ? "a command is running — the publish would read a tree the agent is editing" : "");
    promoteBtn = html`<button class="secondary run-promote" disabled=${!!block || busy === "promote"} onClick=${promote}
      title=${block || "mf-run promote: publish the checked-out tree, swap it in, restart stable"}>Promote</button>`;
  }

  return html`
    <div class="run-service" data-service=${svc.name}>
      <div class="run-service-header">
        <span class=${"run-dot " + proc.dot} title=${procText}></span>
        <strong>${svc.name}</strong>
        <span class="muted">${procText}</span>
        ${extra}
        ${link}
        <span class="row-inline" style="margin-left:auto;">
          <button class="secondary run-log-toggle" onClick=${toggleLog}>${expanded ? "Hide log" : "Log"}</button>
          ${buttons}
          ${promoteBtn}
        </span>
      </div>
      ${message}
      <pre class="log-tail run-log" style=${expanded ? "" : "display:none;"}>${log}</pre>
      <${ResultText} line=${result}/>
    </div>`;
}

// The Run card (ontwerp-v0.3.md): every status read and button is a fetch to an endpoint that
// shells out to mf-run — nothing here tracks process state.
function RunCard({ /** @type {RunStatus | null} */ run, /** @type {string | null} */ error, /** @type {boolean} */ laneRunning }) {
  const [allError, setAllError] = useState("");
  const [busy, setBusy] = useState(/** @type {string | null} */ (null));

  if (!run && !error) return null; // not loaded yet: hidden, as before
  if (run && !error && (!run.configured || (!run.services.length && !run.stable))) return null;

  const runAll = async (/** @type {string} */ action) => {
    setBusy(action);
    setAllError("");
    try {
      const r = await Cockpit.api(`${base}/run/${action}`, { method: "POST" });
      if (!r.success) setAllError(`${action}-all: ${exitLine(r)}`);
      await loadRun();
    } catch (err) {
      setAllError(/** @type {Error} */ (err).message);
    } finally {
      setBusy(null);
    }
  };

  // Start all / Stop all never touch stable, so a stable-only project has nothing for them.
  const hasServices = !!(run && run.services.length);
  return html`
    <div class="card" id="run-card">
      <h2>
        Run
        <span class="row-inline">
          <button id="run-start-all" class="secondary" style=${hasServices ? "" : "display:none;"} disabled=${busy === "start"} onClick=${() => runAll("start")}>Start all</button>
          <button id="run-stop-all" class="secondary" style=${hasServices ? "" : "display:none;"} disabled=${busy === "stop"} onClick=${() => runAll("stop")}>Stop all</button>
        </span>
      </h2>
      <div id="run-error" class="muted">${allError || error || (run && run.error) || ""}</div>
      <div id="run-services">
        ${run && run.configured ? run.services.map((s) => html`<${RunServiceRow} key=${s.name} svc=${s}/>`) : null}
        ${run && run.configured && run.stable
          ? html`<${RunServiceRow} key=${"\u0000stable"} svc=${run.stable} stable=${true} head=${run.head} laneRunning=${laneRunning}/>`
          : null}
      </div>
    </div>`;
}

// ---- Lane card ----

const LANE_VISIBLE = 10;

function ReadyButton({ /** @type {string} */ id }) {
  const [busy, setBusy] = useState(false);
  const click = async () => {
    setBusy(true);
    try {
      await Cockpit.api(`${base}/commands/${encodeURIComponent(id)}/ready`, { method: "POST" });
      await loadLane();
    } catch (err) {
      alert(/** @type {Error} */ (err).message);
      setBusy(false);
    }
  };
  return html`<button class="ready-btn secondary" disabled=${busy} onClick=${click}>Make ready</button>`;
}

// Newest-first, 10 at a time (docs/prompts/0008 item 1). The server's ordinal order is reversed at
// render time only — the *full* list is what the cue tracker sees. Reversing puts a follow-up
// *above* its parent, so the marker is ↱ (shaft rising from the parent below).
function LaneCard({ /** @type {LaneCommand[] | null} */ lane }) {
  const [expanded, setExpanded] = useState(false);
  let rows;
  if (!lane) {
    rows = html`<tr><td colspan="7" class="muted">Loading…</td></tr>`;
  } else if (!lane.length) {
    rows = html`<tr><td colspan="7" class="muted">No commands yet.</td></tr>`;
  } else {
    const newestFirst = lane.slice().reverse();
    const hidden = expanded ? 0 : Math.max(0, newestFirst.length - LANE_VISIBLE);
    const shown = hidden ? newestFirst.slice(0, LANE_VISIBLE) : newestFirst;
    rows = [
      shown.map((c) => html`
        <tr key=${c.id} class=${c.isFollowUp ? "followup-row" : ""}>
          <td>${c.isFollowUp ? html`<span class="muted followup-marker" title="follow-up — its parent is the row below">↱ </span>` : null}<a href=${`command.html?p=${encodeURIComponent(project)}&id=${encodeURIComponent(c.id)}`}>${c.id}</a></td>
          <td>${c.title}${c.summary ? html`<div class="lane-summary" title=${c.summary}>${c.summary}</div>` : null}</td>
          <td><${StatusPill} status=${c.malformed ? "malformed" : c.status}/></td>
          <td>${c.attempts}${c.maxAttempts ? "/" + c.maxAttempts : ""}</td>
          <td class="muted">${formatDuration(c.durationSeconds)}</td>
          <td class="muted">${c.branch || "-"}</td>
          <td>${c.status === "draft" ? html`<${ReadyButton} id=${c.id}/>` : null}</td>
        </tr>`),
      hidden ? html`<tr key="\u0000more"><td colspan="7"><button id="lane-more" class="secondary" onClick=${() => setExpanded(true)}>+ ${hidden} older</button></td></tr>` : null,
    ];
  }
  return html`
    <div class="card">
      <h2>Lane</h2>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Id</th><th>Title</th><th>Status</th><th>Attempts</th><th>Duration</th><th>Branch</th><th></th></tr></thead>
          <tbody id="lane-body">${rows}</tbody>
        </table>
      </div>
    </div>`;
}

// ---- New draft command ----

function DraftCard() {
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [error, setError] = useState("");
  const submit = async (/** @type {Event} */ e) => {
    e.preventDefault();
    setError("");
    try {
      await Cockpit.api(`${base}/commands`, JSON_POST({ title, body }));
      setTitle("");
      setBody("");
      await loadLane();
    } catch (err) {
      setError(/** @type {Error} */ (err).message);
    }
  };
  // Ctrl+Enter submits (ontwerp-v0.5.md item 9), through the form so `required` still applies.
  const onKey = ctrlEnter(() => {
    const form = /** @type {HTMLFormElement | null} */ (document.getElementById("draft-form"));
    if (form) form.requestSubmit();
  });
  return html`
    <div class="card">
      <h2>New draft command</h2>
      <form id="draft-form" onSubmit=${submit}>
        <div class="field">
          <label for="draft-title">Title</label>
          <input type="text" id="draft-title" required value=${title} onInput=${(/** @type {any} */ e) => setTitle(e.currentTarget.value)} onKeyDown=${onKey}/>
        </div>
        <div class="field">
          <label for="draft-body">Body</label>
          <textarea id="draft-body" rows="5" placeholder=${"## Goal\n..."} value=${body} onInput=${(/** @type {any} */ e) => setBody(e.currentTarget.value)} onKeyDown=${onKey}></textarea>
        </div>
        <button type="submit" title="Ctrl+Enter to create">Create draft</button>
        <span id="draft-error" class="muted">${error}</span>
      </form>
    </div>`;
}

// ---- Git card ----

/**
 * Branch switch (docs/prompts/0014), ↻, Pull/Sync (0025) and Commit all. Every guard is derived
 * from state the page already holds — git (dirty, ahead/behind), lane (running), watch (alive),
 * run (services up) — so neither the disabled state nor the post-switch warning costs a request.
 * The server enforces the same rules (409); `title` says so before the button is pressed.
 * @param {{git: GitInfo | null, gitError: string | null, branches: GitBranches | null,
 *   switchResult: ResultLine | null, laneRunning: boolean, watcherAlive: boolean, servicesRunning: boolean}} props
 */
function GitCard({ git, gitError, branches, switchResult, laneRunning, watcherAlive, servicesRunning }) {
  const [selected, setSelected] = useState(/** @type {string | null} */ (null));
  const [switching, setSwitching] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [pulling, setPulling] = useState(/** @type {"Pull" | "Sync" | null} */ (null));
  const [pullResult, setPullResult] = useState(/** @type {ResultLine | null} */ (null));
  const [message, setMessage] = useState("");
  const [committing, setCommitting] = useState(false);
  const [commitError, setCommitError] = useState("");

  // A reloaded branch list shows the current branch again, as the rebuilt <select> did.
  useEffect(() => { setSelected(null); }, [branches]);
  // Every git load refills the message with the server's suggestion, as before.
  useEffect(() => { if (git && git.available) setMessage(git.defaultCommitMessage || ""); }, [git]);
  // Commit all re-enables only through the next git load (or on an error), as before.
  useEffect(() => { setCommitting(false); }, [git]);

  const available = !!(git && git.available);
  const dirty = available && !!git?.dirty;
  const diverged = available && (git?.ahead ?? 0) > 0 && (git?.behind ?? 0) > 0;
  const branchList = branches ? branches.branches || [] : [];
  const current = selected ?? (branches ? branches.current : null);

  const switchReason = dirty
    ? "the working tree is dirty — commit or discard first (\"Commit all\"), then switch"
    : (laneRunning ? "a command is running — switching would swap the tree under it" : "");
  const pullLabel = pulling || (diverged ? "Sync" : "Pull");
  const pullReason = dirty
    ? `the working tree is dirty — commit or discard first ("Commit all"), then ${diverged ? "sync" : "pull"}`
    : (laneRunning ? "a command is running — wait until it finishes" : "");
  const pullTitle = pullReason || (diverged
    ? "diverged from the upstream: git pull --rebase (replays your local commits on top), then git push"
    : "git pull --ff-only");

  const refresh = async () => {
    setRefreshing(true);
    state.switchResult = { text: "fetching…", cls: "run-result muted" };
    rerender();
    try { await loadBranches(true); } finally { setRefreshing(false); }
  };

  // The server refuses a dirty tree, a running command and any branch outside the list it handed
  // out; git's own exit code and output are shown inline, never swallowed.
  const doSwitch = async () => {
    const branch = current;
    if (!branch) return;
    setSwitching(true);
    state.switchResult = { text: `switching to ${branch}…`, cls: "run-result muted" };
    rerender();
    try {
      const r = await Cockpit.api(`${base}/git/checkout`, JSON_POST({ branch }));
      // Warn, never refuse: a watcher poll in flight cannot be detected, and services are the old
      // tree's processes with a Restart button right here on the page.
      const warnings = [];
      if (watcherAlive) warnings.push("the watcher is running here — it may start a ready command on this branch at any moment");
      if (servicesRunning) warnings.push("services are still running from the old tree — Restart them in the Run card");
      const warn = r.success && warnings.length ? " — " + warnings.join("; ") : "";
      state.switchResult = {
        text: `switch: ${exitLine(r)}${warn}`,
        cls: "run-result " + (r.success ? (warnings.length ? "run-warning" : "muted") : "run-error"),
      };
      rerender();
      // A checkout rewrites docs/prompts/ and .magnaflow/ (SSE refreshes the lane), but the Git
      // card is not on that fast path, so refetch its own reads here as Pull does.
      await Promise.all([loadGit(), loadBranches(false), loadLane(), refreshSummary()]);
    } catch (err) {
      state.switchResult = { text: /** @type {Error} */ (err).message, cls: "run-result run-error" };
      rerender();
    } finally {
      setSwitching(false);
    }
  };

  // Exactly `git pull --ff-only`, or on a diverged copy Sync: `git pull --rebase` then `git push`.
  // A conflicting rebase was already aborted server-side, so the tree is as it was.
  const pull = async () => {
    const sync = pullLabel === "Sync";
    setPulling(sync ? "Sync" : "Pull");
    setPullResult({ text: sync ? "syncing…" : "pulling…", cls: "run-result muted" });
    try {
      const r = await Cockpit.api(`${base}/git/${sync ? "sync" : "pull"}`, { method: "POST" });
      const lines = [`${sync ? "pull --rebase" : "pull"}: ${exitLine(r)}`];
      if (r.conflicts && r.conflicts.length)
        lines.push(`conflict in: ${r.conflicts.join(", ")} — rebase aborted, the tree is as it was; resolve it by hand`);
      if (r.push) lines.push(`push: ${exitLine(r.push)}`);
      const ok = r.success && (!r.push || r.push.success);
      setPullResult({ text: lines.join("\n"), cls: "run-result " + (ok ? "muted" : "run-error") });
      await loadGit();
    } catch (err) {
      setPullResult({ text: /** @type {Error} */ (err).message, cls: "run-result run-error" });
    } finally {
      setPulling(null);
    }
  };

  const commit = async (/** @type {Event} */ e) => {
    e.preventDefault();
    setCommitError("");
    setCommitting(true);
    try {
      /** @type {import("../types.js").CommitAllResult} */
      const r = await Cockpit.api(`${base}/git/commit-all`, JSON_POST({ message }));
      if (!r.committed) setCommitError("nothing to commit");
      else if (!r.push) setCommitError("committed (no remote — not pushed)");
      else if (!r.push.success) setCommitError(`committed, but push failed: ${exitLine(r.push)}`);
      else setCommitError("committed and pushed");
      await loadGit();
    } catch (err) {
      setCommitError(/** @type {Error} */ (err).message);
      setCommitting(false);
    }
  };
  const commitKey = ctrlEnter(() => {
    const form = /** @type {HTMLFormElement | null} */ (document.getElementById("commit-all-form"));
    if (form) form.requestSubmit();
  });

  let info;
  if (gitError) info = gitError;
  else if (!git) info = "Loading…";
  else if (!git.available) info = "git not available for this project";
  else {
    // Ahead/behind are against the upstream as last fetched (↻), so a divergence shows after one.
    info = [
      "branch ", html`<strong>${git.branch || "-"}</strong>`, " ",
      git.dirty ? html`<span class="pill aborted">dirty</span>` : html`<span class="pill done">clean</span>`,
      git.ahead == null ? null : [" ", html`<span class="muted" title="commits ahead of / behind the upstream, as last fetched">↑${git.ahead} ↓${git.behind}</span>`],
      git.commits && git.commits.length ? html`<ul class="specs-tree">${git.commits.map((c) => html`
        <li><code>${c.hash.slice(0, 7)}</code> ${c.message} <span class="muted">— ${c.author}, ${Cockpit.formatDate(c.date)}</span></li>`)}</ul>` : null,
    ];
  }

  return html`
    <div class="card">
      <h2>
        Git
        <span class="row-inline">
          <select id="branch-select" title="Branches in this working copy" onChange=${(/** @type {any} */ e) => setSelected(e.currentTarget.value)}>
            ${branchList.map((b) => html`<option key=${b.name} value=${b.name} selected=${b.name === current}>${b.name}${b.remoteOnly ? " (remote)" : ""}</option>`)}
          </select>
          <button id="branch-switch-btn" class="secondary" onClick=${doSwitch}
            disabled=${switching || !!switchReason || branchList.length === 0}
            title=${switchReason || "git switch <branch> — the lane lives on the branch, not on the project"}>Switch</button>
          <button id="branch-refresh-btn" class="secondary" onClick=${refresh} disabled=${refreshing}
            title="git fetch, then reload the list — the only control here that reaches the network. Needed to see a branch another machine pushed.">↻</button>
          <button id="git-pull-btn" class="secondary" onClick=${pull} disabled=${!!pulling || !!pullReason} title=${pullTitle}>${pullLabel}</button>
        </span>
      </h2>
      <div id="git-info" class="muted">${info}</div>
      <${ResultText} id="switch-result" line=${switchResult}/>
      <${ResultText} id="pull-result" line=${pullResult}/>
      <form id="commit-all-form" class="row" style="margin-top:0.75rem;" onSubmit=${commit}>
        <input type="text" id="commit-message" placeholder="commit message" required value=${message}
          onInput=${(/** @type {any} */ e) => setMessage(e.currentTarget.value)} onKeyDown=${commitKey}/>
        <button type="submit" id="commit-all-btn" disabled=${committing || !dirty} title="Commit everything and push (Ctrl+Enter)">Commit all</button>
      </form>
      <span id="commit-error" class="muted">${commitError}</span>
    </div>`;
}

// ---- Watcher card ----

// The log tail the server sent, last 10 visible (docs/prompts/0008 item 2): chronological like
// `tail`, cropped; "load all" reveals lines already fetched. The expanded state survives a refetch.
const WATCH_LOG_VISIBLE = 10;

function WatchCard({ /** @type {WatchStatus | null} */ watch }) {
  const [expanded, setExpanded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const toggle = async () => {
    const action = watch && watch.running ? "stop" : "start";
    setError("");
    setBusy(true);
    try {
      const r = await Cockpit.api(`${base}/watch/${action}`, { method: "POST" });
      if (!r.success) setError(r.error || `${action} failed`);
      await loadWatch();
    } catch (err) {
      setError(/** @type {Error} */ (err).message);
    } finally {
      setBusy(false);
    }
  };

  const lines = watch ? watch.log.lines : [];
  const hidden = expanded ? 0 : Math.max(0, lines.length - WATCH_LOG_VISIBLE);
  const shown = hidden ? lines.slice(-WATCH_LOG_VISIBLE) : lines;
  return html`
    <div class="card">
      <h2>
        Watcher
        <span class="row-inline">
          <button id="watch-toggle" class="secondary" disabled=${busy} onClick=${toggle}>${watch ? (watch.running ? "Stop" : "Start") : "…"}</button>
        </span>
      </h2>
      <div id="watch-lock" class="muted">${watch ? (watch.running ? "watcher running on this copy" : "watcher not running") : ""}</div>
      <div id="watch-error" class="muted">${error}</div>
      <pre class="log-tail" id="watch-log">${watch ? (shown.join("\n") || "(empty)") : ""}</pre>
      <button id="watch-log-more" class="secondary" style=${hidden ? "" : "display:none;"} onClick=${() => setExpanded(true)}>load all (${hidden} older)</button>
    </div>`;
}

// ---- Actions card ----

function LastRun({ /** @type {ActionLast | null} */ last }) {
  if (!last) return html`<span class="muted">never run</span>`;
  return [
    last.runAt
      ? `${Cockpit.formatDate(last.runAt)} (${last.trigger || "?"}): ${last.result || "?"}, ${last.durationSeconds ?? "?"}s`
      : html`<span class="muted">never run</span>`,
    last.refusedAt
      ? [html`<br/>`, html`<span class="muted">refused ${Cockpit.formatDate(last.refusedAt)}: ${last.refused || ""}</span>`]
      : null,
    // A skip by the action's condition: `condition` muted, `condition error (…)` in the failed style.
    last.skippedAt
      ? [html`<br/>`, html`<span class=${(last.skipped || "").startsWith("condition error") ? "action-error" : "muted"}>skipped ${Cockpit.formatDate(last.skippedAt)}: ${last.skipped || ""}</span>`]
      : null,
  ];
}

/**
 * One action row plus its log tail, collapsed by default. An open log refetches with every
 * actions refetch, so it follows the run like the lane and watcher lists.
 * @param {{a: Action, watcherAlive: boolean}} props
 */
function ActionRow({ a, watcherAlive }) {
  const [open, setOpen] = useState(false);
  const [lines, setLines] = useState(/** @type {string[] | null} */ (null));
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    let stale = false;
    Cockpit.api(`${base}/actions/${encodeURIComponent(a.name)}/log`)
      .then((/** @type {import("../types.js").LogTail} */ tail) => { if (!stale) setLines(trimTrailingEmpty(tail.lines)); })
      .catch((/** @type {Error} */ err) => { if (!stale) setLines((prev) => prev ?? [err.message]); });
    return () => { stale = true; };
  }, [a, open]);

  const run = async () => {
    setError("");
    setBusy(true);
    try {
      const r = await Cockpit.api(`${base}/actions/${encodeURIComponent(a.name)}/run`, { method: "POST" });
      if (!r.success) setError(r.error || "run failed");
    } catch (err) {
      setError(/** @type {Error} */ (err).message);
    }
    await loadActions().catch(() => {});
    setBusy(false);
  };

  const runTitle = a.pending ? "a run is queued for the watcher's next poll"
    : !watcherAlive ? "no watcher runs for this project: nothing would pick the request up"
    : `runs at the watcher's next poll; by hand: mf-worker action ${a.name}`;
  return [
    html`<tr key="row">
      <td><strong>${a.name}</strong>${a.description ? [html`<br/>`, html`<span class="muted">${a.description}</span>`] : null}</td>
      <td>${a.schedule}</td>
      <td><${LastRun} last=${a.last}/></td>
      <td>${a.pending ? html`<em>queued</em>` : null}<span class="muted">${error}</span></td>
      <td class="row-inline">
        <button class="secondary" title=${runTitle} disabled=${busy || a.pending || !watcherAlive} onClick=${run}>Run</button>
        <button class="secondary" onClick=${() => setOpen(!open)}>${open ? "hide log" : "log"}</button>
      </td>
    </tr>`,
    open ? html`<tr key="log"><td colspan="5"><pre class="log-tail">${(lines || []).join("\n") || "(empty)"}</pre></td></tr>` : null,
  ];
}

// The Actions card (docs/specs/cockpit/project.md#actions): only with .magnaflow/actions.yml. Run
// writes a request the watcher consumes in its next poll, so it is disabled while no watcher runs
// and while a request is already queued.
function ActionsCard({ /** @type {ActionsStatus | null} */ actions, /** @type {boolean} */ watcherAlive }) {
  if (!actions || !actions.exists) return null;
  return html`
    <div class="card" id="actions-card">
      <h2>Actions</h2>
      <div id="actions-warning" class="muted">${actions.ignored
        ? "git ignores .magnaflow/actions.yml: add !.magnaflow/actions.yml to .gitignore, or the actions stay on this machine"
        : ""}</div>
      <div id="actions-error" class="muted">${actions.error ? `actions.yml: ${actions.error}` : ""}</div>
      <div class="table-wrap">
        <table>
          <tbody id="actions-rows">
            ${(actions.actions || []).map((a) => html`<${ActionRow} key=${a.name} a=${a} watcherAlive=${watcherAlive}/>`)}
          </tbody>
        </table>
      </div>
    </div>`;
}

// ---- page ----

// Card order is the reading order of the page: what is running, then the lane it is running from,
// then the way to add to that lane, and only then git, the watcher and the actions.
function Page({ state: s }) {
  const laneRunning = !!(s.lane && s.lane.some((/** @type {LaneCommand} */ c) => c.status === "running"));
  const watcherAlive = !!(s.watch && s.watch.running);
  // Anything still up is a process of the *old* tree — the warning after a switch, nothing more.
  const servicesRunning = !!(s.run && s.run.configured && s.run.services
    .some((/** @type {RunService} */ v) => v.running || v.portListening === true));
  return html`
    <${SummaryBar} project=${project} projects=${s.projects} summary=${s.summary} live=${s.live}/>
    <main>
      <${CheckNow} watch=${s.watch}/>
      <${RunCard} run=${s.run} error=${s.runError} laneRunning=${laneRunning}/>
      <${LaneCard} lane=${s.lane}/>
      <${DraftCard}/>
      <${GitCard} git=${s.git} gitError=${s.gitError} branches=${s.branches} switchResult=${s.switchResult}
        laneRunning=${laneRunning} watcherAlive=${watcherAlive} servicesRunning=${servicesRunning}/>
      <${WatchCard} watch=${s.watch}/>
      <${ActionsCard} actions=${s.actions} watcherAlive=${watcherAlive}/>
    </main>`;
}

// ---- boot ----

const crumb = document.getElementById("breadcrumb");
if (crumb) render(html`<${Breadcrumb} segments=${[{ label: "mf-cockpit", href: "index.html" }, { label: project || "?" }]}/>`, crumb);
Cockpit.wireNav(project);
rerender();

// The summary bar's first render — switcher names and summary — out of the one chrome request.
Cockpit.getOverview(project).then((/** @type {import("../types.js").Overview} */ o) => {
  state.projects = o.projects || [];
  if (o.project) setSummary(o.project);
  else rerender();
}).catch(() => {});

// Shared live helper (ontwerp-v0.5.md item 1): lane/watch/run/actions each refetch their own card;
// the Git card keeps its own slower path. The live marker is the bar's LiveBadge, fed by onState.
Cockpit.live({
  project,
  onState: (/** @type {LiveState} */ live) => { state.live = live; rerender(); },
  handlers: [
    { kind: "lane", refetch: loadLane },
    { kind: "lane", refetch: refreshSummary },
    { kind: "watch", refetch: loadWatch },
    { kind: "watch", refetch: refreshSummary },
    { kind: "run", refetch: loadRun },
    { kind: "actions", refetch: loadActions },
  ],
});

// Fast half: awaited together, errors surfaced. Git, branches and run each spawn a process and are
// kicked off separately, so they never delay the lane/watcher data.
Promise.all([loadLane(), loadWatch(), loadActions()]).catch((err) => alert(err.message));
loadGit();
loadBranches(false); // local ref read only — the fetch stays behind ↻
loadRun();
