// @ts-check
// command.html (docs/specs/cockpit/command.md). Page pattern (docs/specs/cockpit/index.md#page-rendering):
// the command detail lives in `state`, load() assigns and rerender()s on every lane/evidence event.
// The follow-up form's input, each log's Raw/Pretty mode and the open JSON lines are UI state in
// the components that own them, so a live refetch keeps them.
import {
  html, render, useState, SummaryBar, StatusPill, Markdown, Frontmatter, LogView, ctrlEnter, startProjectChrome,
} from "../ui.js";
import * as Cockpit from "../app.js";

/** @typedef {import("../types.js").CommandDetail} CommandDetail */
/** @typedef {import("../types.js").LaneFile} LaneFile */
/** @typedef {import("../types.js").CreatedCommand} CreatedCommand */
/** @typedef {import("../types.js").ProjectSummary} ProjectSummary */
/** @typedef {import("../types.js").LiveState} LiveState */

const project = Cockpit.qs("p") || "";
const id = Cockpit.qs("id") || "";
const base = `/api/projects/${encodeURIComponent(project)}/commands/${encodeURIComponent(id)}`;
document.title = `${id} · ${project}`; // refined with this command's own status icon once loaded

const state = {
  /** @type {string[]} */ projects: [],
  /** @type {ProjectSummary | null} */ summary: null,
  /** @type {LiveState | null} */ live: null,
  /** @type {CommandDetail | null} */ detail: null,
  /** The first load's error, shown in place of the heading. @type {string | null} */ loadError: null,
};

const mount = /** @type {HTMLElement} */ (document.getElementById("app"));
function rerender() {
  render(html`<${Page} state=${state}/>`, mount);
}

async function load() {
  /** @type {CommandDetail} */
  const detail = await Cockpit.api(base);
  state.detail = detail;
  const status = detail.malformed ? "malformed" : detail.status;
  document.title = `${Cockpit.statusIcon(status)} ${id} · ${project}`.trim();
  rerender();
}

// Frontmatter as a key/value block, body as markdown — never the raw "--- title: … ---" text the
// markdown renderer would otherwise make of it. The cmd file arrives body-only (the server
// surfaces its frontmatter as structured fields above), pln/qa have none; the rst has all four.
// Links to sibling lane files are rewritten to command.html by Markdown's `project`.
/** @param {{file: LaneFile, emptyText: string}} props */
function LaneFileView({ file, emptyText }) {
  if (!file.exists) return html`<span class="muted">${emptyText}</span>`;
  const { fields, body } = Cockpit.splitFrontmatter(file.content);
  return [html`<${Frontmatter} fields=${fields}/>`, html`<${Markdown} source=${body} project=${project}/>`];
}

/** @param {{detail: CommandDetail}} props */
function ReadyButton({ detail }) {
  const [busy, setBusy] = useState(false);
  if (detail.status !== "draft") return null;
  const onClick = async () => {
    setBusy(true);
    try {
      await Cockpit.api(`${base}/ready`, { method: "POST" });
      await load();
    } catch (err) {
      alert(/** @type {Error} */ (err).message);
    }
    setBusy(false);
  };
  return html`<button class="secondary" disabled=${busy} onClick=${onClick}>Make ready</button>`;
}

// "Continue on this command" is sensible only once the story is over (ontwerp-v0.1.md v0.2).
function FollowUpCard() {
  const [feedback, setFeedback] = useState("");
  const [slug, setSlug] = useState("");
  const [error, setError] = useState("");

  const submit = async () => {
    setError("");
    try {
      /** @type {CreatedCommand} */
      const result = await Cockpit.api(`${base}/follow-up`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ feedback, slug: slug || null }),
      });
      if (result.warning) alert(result.warning);
      location.href = `command.html?p=${encodeURIComponent(project)}&id=${encodeURIComponent(result.id)}`;
    } catch (err) {
      setError(/** @type {Error} */ (err).message);
    }
  };
  const onSubmit = (/** @type {Event} */ e) => { e.preventDefault(); submit(); };
  // Ctrl+Enter submits the follow-up draft (ontwerp-v0.5.md item 9), through the form so `required` holds.
  const onCtrlEnter = ctrlEnter(() => {
    const form = document.getElementById("followup-form");
    if (form) /** @type {HTMLFormElement} */ (form).requestSubmit();
  });

  return html`
    <div class="card" id="followup-card">
      <h2>Continue on this command</h2>
      <form id="followup-form" onSubmit=${onSubmit}>
        <div class="field">
          <label for="followup-feedback">Feedback</label>
          <textarea id="followup-feedback" rows="3" placeholder="good, but this line should be different…" required
            value=${feedback} onInput=${(/** @type {any} */ e) => setFeedback(e.currentTarget.value)} onKeyDown=${onCtrlEnter}></textarea>
        </div>
        <div class="field">
          <label for="followup-slug">Slug (optional)</label>
          <input type="text" id="followup-slug" placeholder="defaults to the same name"
            value=${slug} onInput=${(/** @type {any} */ e) => setSlug(e.currentTarget.value)} onKeyDown=${onCtrlEnter}/>
        </div>
        <button type="submit" title="Ctrl+Enter to create">Create follow-up draft</button>
        <span id="followup-error" class="muted">${error}</span>
      </form>
    </div>`;
}

/**
 * One evidence log: pretty (JSON collapsible) by default, Raw toggles to the plain text.
 * @param {{name: string, lines: string[] | null}} props
 */
function LogBlock({ name, lines }) {
  const [raw, setRaw] = useState(false);
  return html`
    <h3>${name} <button type="button" class="log-toggle secondary" onClick=${() => setRaw(!raw)}>${raw ? "Pretty" : "Raw"}</button></h3>
    <div class="log-tail">${lines ? html`<${LogView} lines=${lines} raw=${raw}/>` : null}</div>`;
}

function Page({ state: s }) {
  /** @type {CommandDetail | null} */
  const d = s.detail;
  const status = d ? (d.malformed ? "malformed" : d.status) : null;
  const meta = d
    ? `${d.id} · attempts ${d.attempts}${d.maxAttempts ? "/" + d.maxAttempts : ""}` +
      (d.branch ? ` · branch ${d.branch}` : "") +
      (d.group ? ` · group ${d.group}` : "") +
      (d.error ? ` · ${d.error}` : "")
    : "";
  const none = (/** @type {LaneFile | undefined} */ f, /** @type {string} */ emptyText) =>
    f ? html`<${LaneFileView} file=${f} emptyText=${emptyText}/>` : "none";

  // The report's own one-line summary sits right under the meta line — the human-sized answer to
  // "what did this command do?" (docs/decisions/0015-rst-summary-line.md). Plain text by design,
  // never markdown-rendered, and not repeated above the report itself.
  return html`
    <${SummaryBar} project=${project} projects=${s.projects} summary=${s.summary} live=${s.live}/>
    <main>
      <div class="card">
        <h2 id="cmd-heading">${d ? [d.title, " ", html`<${StatusPill} status=${status}/>`] : (s.loadError || "Loading…")}</h2>
        <div id="cmd-meta" class="muted">${meta}</div>
        ${d && d.summary ? html`<div id="cmd-summary" class="rst-summary">${d.summary}</div>` : null}
        ${d ? html`<${ReadyButton} detail=${d}/>` : null}
      </div>

      ${d && (d.status === "done" || d.status === "aborted") ? html`<${FollowUpCard}/>` : null}

      <div class="row">
        <div class="card"><h2>Command</h2><div id="cmd-body">${d ? none(d.cmd, "missing") : null}</div></div>
        <div class="card"><h2>Plan</h2><div id="pln-body" class="muted">${none(d?.pln, "no plan file - this command raised no questions")}</div></div>
      </div>
      <div class="row">
        <div class="card"><h2>Questions</h2><div id="qa-body" class="muted">${none(d?.qa, "no questions")}</div></div>
        <div class="card"><h2>Report</h2><div id="rst-body" class="muted">${none(d?.rst, "no report yet")}</div></div>
      </div>

      <div class="card">
        <h2>Evidence</h2>
        <div id="session-id" class="muted">${d ? (d.sessionId ? `session: ${d.sessionId}` : "no recorded session") : ""}</div>
        <${LogBlock} name="claude.log" lines=${d ? d.claudeLog.lines : null}/>
        <${LogBlock} name="build.log" lines=${d ? d.buildLog.lines : null}/>
        <${LogBlock} name="test.log" lines=${d ? d.testLog.lines : null}/>
      </div>
    </main>`;
}

// ---- boot ----

const refreshSummary = startProjectChrome({ project, label: id, chrome: state, rerender });
rerender();

Cockpit.live({
  project,
  onState: (/** @type {LiveState} */ live) => { state.live = live; rerender(); },
  handlers: [
    { kind: "lane", refetch: load },
    { kind: "lane", refetch: refreshSummary },
    { kind: "evidence", refetch: load },
  ],
});

load().catch((err) => { state.loadError = err.message; rerender(); });
