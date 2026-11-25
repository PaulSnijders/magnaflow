// @ts-check
// specs.html (docs/specs/cockpit/specs.md). Page pattern (docs/specs/cockpit/index.md#page-rendering):
// server data in `state`, the loader assigns and rerender()s. The open path comes from ?path= and
// every link here is a full page load, so the page has no UI state of its own.
import { html, render, useLayoutEffect, SummaryBar, Markdown, startProjectChrome } from "../ui.js";
import * as Cockpit from "../app.js";

/** @typedef {import("../types.js").SpecsResult} SpecsResult */
/** @typedef {import("../types.js").SpecsEntry} SpecsEntry */
/** @typedef {import("../types.js").ProjectSummary} ProjectSummary */
/** @typedef {import("../types.js").LiveState} LiveState */

const project = Cockpit.qs("p") || "";
const path = Cockpit.qs("path") || "";
document.title = "specs · " + project;

const state = {
  /** @type {string[]} */ projects: [],
  /** @type {ProjectSummary | null} */ summary: null,
  /** @type {LiveState | null} */ live: null,
  /** @type {SpecsResult | null} */ result: null,
  /** @type {string | null} */ error: null,
};

const mount = /** @type {HTMLElement} */ (document.getElementById("app"));
function rerender() {
  render(html`<${Page} state=${state}/>`, mount);
}

function pathUrl(/** @type {string} */ p) {
  return `specs.html?p=${encodeURIComponent(project)}${p ? "&path=" + encodeURIComponent(p) : ""}`;
}

// Spec cross-links (`../concepts/design.md#why`) resolve against the open spec's folder and
// stay inside the specs page; a link that climbs out of docs/specs/ renders as plain text.
// Other relative targets are left as written.
function specLink(/** @type {string} */ specPath, /** @type {string} */ target) {
  const hashAt = target.indexOf("#");
  const linkPath = hashAt < 0 ? target : target.slice(0, hashAt);
  const anchor = hashAt < 0 ? "" : target.slice(hashAt);
  if (!/\.md$/i.test(linkPath)) return target;
  if (linkPath.startsWith("/")) return null;
  const parts = specPath.split("/").filter(Boolean).slice(0, -1);
  for (const part of linkPath.split("/")) {
    if (part === "" || part === ".") continue;
    if (part === "..") {
      if (!parts.length) return null;
      parts.pop();
    } else {
      parts.push(part);
    }
  }
  return pathUrl(parts.join("/")) + anchor;
}

// One object for the page's lifetime (the path is fixed per load), so Markdown's memo holds.
const mdOpts = { rewriteLink: (/** @type {string} */ target) => specLink(path, target) };

// A non-markdown file (config.yml, ...) is shown as written, preformatted. YAML gets one touch -
// a `#` at line start or after whitespace, outside quotes, starts a dimmed comment.
function PlainFile({ path: p, content }) {
  const yaml = /\.ya?ml$/i.test(p);
  const lines = content.split("\n").map((line, n) => {
    const nl = n ? "\n" : "";
    if (!yaml) return nl + line;
    let quote = null;
    for (let i = 0; i < line.length; i++) {
      const c = line[i];
      if (quote) { if (c === quote) quote = null; continue; }
      if (c === '"' || c === "'") { quote = c; continue; }
      if (c === "#" && (i === 0 || /\s/.test(line[i - 1]))) {
        return [nl + line.slice(0, i), html`<span class="yaml-comment">${line.slice(i)}</span>`];
      }
    }
    return nl + line;
  });
  return html`<pre class="spec-file"><code>${lines}</code></pre>`;
}

// The rendered spec arrives after the page has loaded, so the browser's own jump to
// location.hash finds nothing; scroll to it once rendered and on every hash change. Looked up
// inside the view only, so a heading slug equal to a page id (`live`) still lands on the heading.
function scrollToHash() {
  if (location.hash.length < 2) return;
  let id;
  try { id = decodeURIComponent(location.hash.slice(1)); } catch { return; }
  const view = document.getElementById("specs-view");
  const target = view && view.querySelector(`[id="${CSS.escape(id)}"]`);
  if (target) target.scrollIntoView();
}
window.addEventListener("hashchange", scrollToHash);

function Crumbs() {
  const parts = path.split("/").filter(Boolean);
  let acc = "";
  return [html`<a href=${pathUrl("")}>docs/specs</a>`, ...parts.map((part) => {
    acc = acc ? acc + "/" + part : part;
    return [" / ", html`<a href=${pathUrl(acc)}>${part}</a>`];
  })];
}

function View({ result, error }) {
  const isMarkdown = !!result && result.type === "file" && /\.md$/i.test(path);
  useLayoutEffect(() => { if (isMarkdown) scrollToHash(); }, [isMarkdown, result]);

  if (error) return html`<div id="specs-view"><span class="muted">${error}</span></div>`;
  if (!result) return html`<div id="specs-view">Loading…</div>`;
  if (result.type === "dir") {
    if (!result.entries.length) return html`<div id="specs-view"><span class="muted">(empty)</span></div>`;
    return html`<div id="specs-view"><ul class="specs-tree">${result.entries.map((/** @type {SpecsEntry} */ e) => html`
      <li key=${e.name}><a href=${pathUrl(path ? path + "/" + e.name : e.name)}>${(e.isDirectory ? "📁 " : "📄 ") + e.name}</a></li>`)}
    </ul></div>`;
  }
  if (isMarkdown) return html`<${Markdown} id="specs-view" source=${result.content} opts=${mdOpts}/>`;
  return html`<div id="specs-view"><${PlainFile} path=${path} content=${result.content}/></div>`;
}

function Page({ state: s }) {
  return html`
    <${SummaryBar} project=${project} projects=${s.projects} summary=${s.summary} live=${s.live}/>
    <main>
      <div class="card">
        <h2 id="breadcrumbs"><${Crumbs}/></h2>
        <${View} result=${s.result} error=${s.error}/>
      </div>
    </main>`;
}

async function load() {
  const url = `/api/projects/${encodeURIComponent(project)}/specs` + (path ? "/" + path.split("/").map(encodeURIComponent).join("/") : "");
  try {
    state.result = await Cockpit.api(url);
    state.error = null;
  } catch (err) {
    state.error = /** @type {Error} */ (err).message;
  }
  rerender();
}

// ---- boot ----

const refreshSummary = startProjectChrome({ project, label: "specs", chrome: state, rerender });
rerender();

// Specs aren't watched (docs/specs, not docs/prompts), but the top bar still reflects lane/watch
// changes; the live helper drives the connection marker (ontwerp-v0.5.md items 1–2).
Cockpit.live({
  project,
  onState: (/** @type {LiveState} */ live) => { state.live = live; rerender(); },
  handlers: [
    { kind: "lane", refetch: refreshSummary },
    { kind: "watch", refetch: refreshSummary },
  ],
});

load();
