// @ts-check
// Shared cockpit components (docs/specs/cockpit/index.md#page-rendering, decision 0022).
// Preact + htm, vendored; text interpolated with ${…} is escaped by the template. The one place
// that injects an HTML string is Markdown below, fed with md.js output (which vets its own links).
//
// app.js and md.js are still classic scripts (0036B turns them into modules): a page loads them
// with a plain <script src> before its module, and this file reads window.Cockpit /
// window.renderMarkdown for the non-HTML helpers (api, formatDate, displayUrl, sound, ...).
import {
  html, render, useState, useEffect, useLayoutEffect, useRef, useMemo, useCallback,
} from "./vendor/preact-htm.js";

/** @typedef {import("./types.js").ProjectSummary} ProjectSummary */
/** @typedef {import("./types.js").LiveState} LiveState */

export { html, render, useState, useEffect, useLayoutEffect, useRef, useMemo, useCallback };

/** The classic app.js helper object. @type {any} */
export const Cockpit = /** @type {any} */ (window).Cockpit;

/** @param {{status?: string | null}} props */
export function StatusPill({ status }) {
  const s = status || "unknown";
  return html`<span class=${"pill " + s}>${s}</span>`;
}

function clock(/** @type {number} */ ms) {
  try { return new Date(ms).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }); } catch { return ""; }
}

/**
 * The live marker, fed from Cockpit.live()'s onState. Same markup and label as the badge
 * live() renders itself: "connecting…" until the first state arrives.
 * @param {{live?: LiveState | null, class?: string}} props
 */
export function LiveBadge({ live, class: extra }) {
  const connected = !!(live && live.connected);
  const label = live ? live.label : "connecting…";
  const suffix = live && live.lastUpdated ? " · " + clock(live.lastUpdated) : "";
  const cls = [extra, "badge-live", connected ? "connected" : ""].filter(Boolean).join(" ");
  return html`<span class=${cls}><span class="dot"></span><span class="label">${label + suffix}</span></span>`;
}

/**
 * The sound toggle is a DOM button app.js keeps in step itself (every rendered toggle reflects
 * one state), so it is adopted once into a wrapper that takes no layout of its own.
 */
export function SoundToggle() {
  const ref = useRef(/** @type {HTMLSpanElement | null} */ (null));
  useEffect(() => {
    const el = ref.current;
    if (el && !el.firstChild) el.appendChild(Cockpit.soundToggle());
  }, []);
  return html`<span ref=${ref} style="display:contents"></span>`;
}

/**
 * Clickable breadcrumb: segments [{label, href}], the last one (or one without href) plain text.
 * @param {{segments: {label: string, href?: string}[]}} props
 */
export function Breadcrumb({ segments }) {
  return segments.map((s, i) => {
    const isLast = i === segments.length - 1;
    const sep = i ? [" ", html`<span class="crumb-sep">/</span>`, " "] : null;
    const crumb = isLast || !s.href
      ? html`<span class="crumb-current">${s.label}</span>`
      : html`<a href=${s.href}>${s.label}</a>`;
    return [sep, crumb];
  });
}

function summarizeJson(/** @type {any} */ obj) {
  if (obj && typeof obj === "object" && !Array.isArray(obj)) {
    const bits = [];
    if (obj.type) bits.push(obj.type);
    if (obj.subtype) bits.push(obj.subtype);
    if (bits.length) return bits.join(":");
  }
  const flat = JSON.stringify(obj);
  return flat.length > 90 ? flat.slice(0, 90) + "…" : flat;
}

/**
 * An evidence log block's content: pretty (single-JSON lines pretty-printed and collapsible,
 * other lines plain) or raw text. Renders children for the caller's container element.
 * @param {{lines: string[], raw?: boolean}} props
 */
export function LogView({ lines, raw }) {
  if (raw) return lines.join("\n") || "(empty)";
  if (!lines.length) return html`<span class="muted">(empty)</span>`;
  return lines.map((line) => {
    const trimmed = line.trim();
    if (trimmed.startsWith("{") || trimmed.startsWith("[")) {
      try {
        const obj = JSON.parse(trimmed);
        return html`<details class="json-line"><summary>${summarizeJson(obj)}</summary><pre>${JSON.stringify(obj, null, 2)}</pre></details>`;
      } catch { /* looked like JSON, wasn't — plain rendering */ }
    }
    return html`<div class="log-line">${line}</div>`;
  });
}

/**
 * Rendered markdown: the only component that injects an HTML string (md.js output). With
 * `project`, links to sibling lane files are rewritten to command.html after every render.
 * @param {{source: string, opts?: any, project?: string | null, class?: string, id?: string}} props
 */
export function Markdown({ source, opts, project, class: cls, id }) {
  const ref = useRef(/** @type {HTMLDivElement | null} */ (null));
  const rendered = useMemo(() => /** @type {any} */ (window).renderMarkdown(source || "", opts), [source, opts]);
  useLayoutEffect(() => {
    if (project && ref.current) Cockpit.rewriteLaneLinks(ref.current, project);
  }, [rendered, project]);
  return html`<div ref=${ref} class=${cls} id=${id} dangerouslySetInnerHTML=${{ __html: rendered }}></div>`;
}

/**
 * A lane file's frontmatter as a key/value block (fields from Cockpit.splitFrontmatter).
 * @param {{fields: [string, string][] | null}} props
 */
export function Frontmatter({ fields }) {
  if (!fields || !fields.length) return null;
  return html`<dl class="lane-frontmatter">${fields
    .filter(([, value]) => value !== "")
    .map(([key, value]) => [html`<dt>${key}</dt>`, html`<dd>${value}</dd>`])}</dl>`;
}

const STATUS_ORDER = ["running", "questions", "aborted", "ready", "draft", "done"];

/**
 * The sticky "is anything happening here?" bar under the header of every project-scoped page.
 * Data comes from the page: `projects` for the switcher (GET /api/overview), `summary` (the
 * overview's project, then GET /api/projects/{name} on live refreshes), `live` from onState.
 * @param {{project: string, projects: string[], summary: ProjectSummary | null, live: LiveState | null}} props
 */
export function SummaryBar({ project, projects, summary, live }) {
  const ref = useRef(/** @type {HTMLDivElement | null} */ (null));

  // --summary-bar-h carries the bar's real height so a scrolled-to heading lands below it; a #hash
  // target the (taller, wrapped) bar now covers is scrolled into view again. Same as app.js.
  useLayoutEffect(() => {
    const bar = ref.current;
    if (!bar) return;
    const sync = () => {
      const height = bar.offsetHeight;
      document.documentElement.style.setProperty("--summary-bar-h", height + "px");
      let id;
      try { id = decodeURIComponent(location.hash.slice(1)); } catch { return; }
      if (!id) return;
      const matches = document.querySelectorAll(`:is(h1, h2, h3, h4, h5, h6, a)[id="${CSS.escape(id)}"]`);
      const target = matches[matches.length - 1];
      const top = target ? target.getBoundingClientRect().top : -1;
      if (top >= 0 && top < height) target.scrollIntoView();
    };
    sync();
    const observer = new ResizeObserver(sync);
    observer.observe(bar);
    return () => observer.disconnect();
  }, []);

  const onSwitch = (/** @type {Event} */ e) => {
    const value = /** @type {HTMLSelectElement} */ (e.currentTarget).value;
    location.href = value ? "project.html?p=" + encodeURIComponent(value) : "index.html";
  };

  const cmdLink = (/** @type {{id: string}} */ c) =>
    html`<a href=${`command.html?p=${encodeURIComponent(project)}&id=${encodeURIComponent(c.id)}`}>${c.id}</a>`;
  const shortTitle = (/** @type {string} */ t) => t && t.length > 28 ? t.slice(0, 27) + "…" : (t || "");

  let lane = null, counts = null, watch = null;
  if (summary) {
    lane = summary.running
      ? [html`<span class="sb-dot running"></span>`, " ", cmdLink(summary.running), " ", html`<span class="muted">${shortTitle(summary.running.title)}</span>`]
      : summary.latest
        ? [html`<span class="muted">idle · latest</span>`, " ", cmdLink(summary.latest), " ", html`<${StatusPill} status=${summary.latest.status}/>`]
        : html`<span class="muted">idle · no commands</span>`;
    const c = summary.counts || {};
    const present = STATUS_ORDER.filter((k) => c[k] > 0);
    counts = present.length
      ? present.map((k, i) => [i ? " " : null, html`<span class=${"sb-count " + k}>${c[k]} ${k}</span>`])
      : html`<span class="muted">—</span>`;
    watch = summary.watcherAlive
      ? [html`<span class="sb-dot alive"></span>`, " watcher"]
      : html`<span class="muted">watcher off</span>`;
  }

  return html`
    <div class="summary-bar" ref=${ref}>
      <select class="sb-switcher" title="Switch project" onChange=${onSwitch}>
        <option value="">Overview…</option>
        ${projects.map((name) => html`<option value=${name} selected=${name === project}>${name}</option>`)}
      </select>
      <span class="sb-lane">${lane}</span>
      <span class="sb-counts">${counts}</span>
      <span class="sb-spacer"></span>
      <span class="sb-watch muted">${watch}</span>
      <${SoundToggle}/>
      <${LiveBadge} class="sb-live" live=${live}/>
    </div>`;
}

/**
 * Native <dialog> driven by an `open` prop: showModal()/close() through a ref. A close by Esc or
 * the form's own close is reported back through onClose so the owner's state follows.
 * @param {{open: boolean, onClose: () => void, id?: string, children?: any}} props
 */
export function Dialog({ open, onClose, id, children }) {
  const ref = useRef(/** @type {HTMLDialogElement | null} */ (null));
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    else if (!open && d.open) d.close();
  }, [open]);
  return html`<dialog ref=${ref} id=${id} onClose=${onClose}>${children}</dialog>`;
}

/**
 * Ctrl+Enter (Cmd+Enter) handler for an input's onKeyDown (ontwerp-v0.5.md item 9).
 * @param {() => void} submit
 */
export function ctrlEnter(submit) {
  return (/** @type {KeyboardEvent} */ e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
      e.preventDefault();
      submit();
    }
  };
}
