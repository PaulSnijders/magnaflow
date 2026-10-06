// Tiny vendored markdown renderer (ontwerp-v0.1.md "Tech": "no CDN dependencies... rendered
// client-side by a small vendored renderer"). Not CommonMark-complete - headers, emphasis, inline
// code, fenced code blocks, links, lists, blockquotes, GitHub-style pipe tables, and paragraphs,
// which is everything the lane/plan/report/spec markdown in this project actually uses.
//
// A list item runs on over indented continuation lines (also across a blank line when the next
// line is still indented); an indented marker is a nested list. A fenced code block indented under
// an item is not supported: it ends the list.
//
// Headings get ids by the spec kit's rule (scripts/spec_lint.mjs ANCHOR + ghSlug), so every anchor
// the lint accepts also resolves here: a `{#slug}` is dropped from the visible text and becomes the
// id, and the GitHub slug of the heading is a second target when it differs. Ids are unique per
// call; a duplicate gets -1, -2, ... like GitHub.
//
// Link targets are vetted: only http:, https:, mailto: and relative targets become links; any
// other scheme (javascript:, data:, ...) renders as plain text, since chat replies are model output.
// opts.rewriteLink(target) may map a relative target to a new href, or return null to render the
// link as plain text (specs.html resolves spec cross-links with it). External links open in a new
// tab; a same-page `#anchor`, or a link rewriteLink changed, is in-cockpit navigation and does not.

// Kept identical to scripts/spec_lint.mjs - change both together.
const MD_ANCHOR = /\{#([a-z0-9-]+)\}/;
function mdGhSlug(headingText) {
  let t = headingText.replace(new RegExp(MD_ANCHOR.source, "g"), "").trim().toLowerCase();
  t = t.replace(/[^\p{L}\p{N}_\s-]/gu, "");
  return t.replaceAll(" ", "-");
}

function renderMarkdown(src, opts) {
  if (!src) return "";
  const rewriteLink = opts && opts.rewriteLink;

  function escapeHtml(s) {
    return s.replace(/[&<>"']/g, (c) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
    })[c]);
  }

  const codeBlocks = [];
  const markerFor = (idx) => "\u0000CODEBLOCK" + idx + "\u0000";
  const markerPattern = /\u0000CODEBLOCK(\d+)\u0000/;

  const text = String(src).replace(/```([a-zA-Z0-9_-]*)\n([\s\S]*?)```/g, (_, lang, code) => {
    const idx = codeBlocks.length;
    codeBlocks.push('<pre><code class="lang-' + escapeHtml(lang) + '">' + escapeHtml(code.replace(/\n$/, "")) + "</code></pre>");
    return markerFor(idx);
  });

  function inline(s) {
    s = escapeHtml(s);
    s = s.replace(/`([^`]+)`/g, "<code>$1</code>");
    s = s.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
    s = s.replace(/(?<!\*)\*([^*]+)\*(?!\*)/g, "<em>$1</em>");
    s = s.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_, label, escapedTarget) => {
      const target = unescapeHtml(escapedTarget);
      const href = linkHref(target);
      if (href === null) return label;
      const sameTab = href.startsWith("#") || href !== target;
      return '<a href="' + escapeHtml(href) + '"' + (sameTab ? "" : ' target="_blank" rel="noopener"') + ">" + label + "</a>";
    });
    return s;
  }

  function unescapeHtml(s) {
    return s.replace(/&(amp|lt|gt|quot|#39);/g, (_, e) => ({ amp: "&", lt: "<", gt: ">", quot: '"', "#39": "'" })[e]);
  }

  // The href a link target gets, or null to render the link as plain text.
  function linkHref(target) {
    const scheme = /^([a-z][a-z0-9+.-]*):/i.exec(target);
    if (scheme) return ["http", "https", "mailto"].includes(scheme[1].toLowerCase()) ? target : null;
    return rewriteLink ? rewriteLink(target) : target;
  }

  const usedIds = new Set();
  function uniqueId(id) {
    let candidate = id;
    for (let n = 1; usedIds.has(candidate); n++) candidate = id + "-" + n;
    usedIds.add(candidate);
    return candidate;
  }

  function headingHtml(level, raw) {
    const explicit = MD_ANCHOR.exec(raw);
    const slug = mdGhSlug(raw);
    const visible = raw.replace(new RegExp(MD_ANCHOR.source, "g"), "").trim();
    const ids = [];
    if (explicit) ids.push(uniqueId(explicit[1]));
    if (slug && (!explicit || slug !== explicit[1])) ids.push(uniqueId(slug));
    const extra = ids.slice(1).map((id) => '<a id="' + escapeHtml(id) + '"></a>').join("");
    const idAttr = ids.length ? ' id="' + escapeHtml(ids[0]) + '"' : "";
    return extra + "<h" + level + idAttr + ">" + inline(visible) + "</h" + level + ">";
  }

  // GitHub-style pipe table cells; `\|` is a literal pipe, not a separator.
  const PIPE = "\u0001";
  function tableCells(line) {
    let t = line.trim().replace(/\\\|/g, PIPE);
    if (t.startsWith("|")) t = t.slice(1);
    if (t.endsWith("|")) t = t.slice(0, -1);
    return t.split("|").map((c) => c.trim().replaceAll(PIPE, "|"));
  }
  const DELIMITER_CELL = /^:?-+:?$/;
  function tableAt(i) {
    if (i + 1 >= lines.length || !lines[i].includes("|") || !lines[i + 1].includes("|")) return null;
    const header = tableCells(lines[i]);
    const delims = tableCells(lines[i + 1]);
    if (delims.length !== header.length || !delims.every((d) => DELIMITER_CELL.test(d))) return null;
    const align = delims.map((d) => d.startsWith(":") && d.endsWith(":") ? "center" : d.endsWith(":") ? "right" : d.startsWith(":") ? "left" : "");
    const cell = (tag, content, col) =>
      "<" + tag + (align[col] ? ' style="text-align:' + align[col] + '"' : "") + ">" + inline(content) + "</" + tag + ">";
    const rows = [];
    let j = i + 2;
    for (; j < lines.length && lines[j].trim() !== "" && lines[j].includes("|"); j++) {
      const cells = tableCells(lines[j]);
      rows.push("<tr>" + header.map((_, col) => cell("td", cells[col] || "", col)).join("") + "</tr>");
    }
    const html = '<div class="table-wrap md-table"><table><thead><tr>' + header.map((h, col) => cell("th", h, col)).join("") +
      "</tr></thead><tbody>" + rows.join("") + "</tbody></table></div>";
    return { html, next: j };
  }

  const lines = text.split("\n");
  const out = [];
  let paragraph = [];
  // Open lists, outermost first: { tag: 'ul'|'ol', indent, items: [{ text, contentIndent, subs: [] }],
  // parentItem }. A nested list hangs in its parent item's subs.
  let lists = [];
  let quote = [];

  function flushParagraph() {
    if (paragraph.length) {
      out.push("<p>" + inline(paragraph.join(" ")) + "</p>");
      paragraph = [];
    }
  }
  function listHtml(l) {
    return "<" + l.tag + ">" + l.items.map((it) => "<li>" + inline(it.text) + it.subs.map(listHtml).join("") + "</li>").join("") +
      "</" + l.tag + ">";
  }
  function flushList() {
    if (lists.length) {
      out.push(listHtml(lists[0]));
      lists = [];
    }
  }
  const indentOf = (line) => /^\s*/.exec(line)[0].length;

  // A marker line at `indent`: a sub-item when it reaches the open item's content column,
  // otherwise an item of the open list at its level (a different marker type starts a new list).
  function addItem(tag, indent, contentIndent, text) {
    const item = { text, contentIndent, subs: [] };
    let top = lists[lists.length - 1];
    if (top) {
      const last = top.items[top.items.length - 1];
      if (indent >= last.contentIndent) {
        const sub = { tag, indent, items: [item], parentItem: last };
        last.subs.push(sub);
        lists.push(sub);
        return;
      }
      while (lists.length > 1 && indent < top.indent) { lists.pop(); top = lists[lists.length - 1]; }
      if (top.tag === tag) { top.items.push(item); return; }
      if (lists.length > 1) {
        const sibling = { tag, indent, items: [item], parentItem: top.parentItem };
        top.parentItem.subs.push(sibling);
        lists[lists.length - 1] = sibling;
        return;
      }
      flushList();
    }
    lists.push({ tag, indent, items: [item], parentItem: null });
  }

  // An indented line under an open list continues the deepest item it is indented past,
  // joined with one space so inline markdown across the line break still works.
  function continueItem(indent, text) {
    let k = lists.length - 1;
    while (k > 0 && indent <= lists[k].indent) k--;
    const item = lists[k].items[lists[k].items.length - 1];
    item.text += " " + text;
  }

  // After a blank line a list stays open only if the next non-blank line is indented under it.
  function listContinuesAfterBlank(i) {
    let j = i + 1;
    while (j < lines.length && lines[j].trim() === "") j++;
    return j < lines.length && indentOf(lines[j]) > 0;
  }
  function flushQuote() {
    if (quote.length) {
      out.push("<blockquote>" + inline(quote.join(" ")) + "</blockquote>");
      quote = [];
    }
  }
  function flushAll() { flushParagraph(); flushList(); flushQuote(); }

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();

    const codeMarker = markerPattern.exec(trimmed);
    if (codeMarker && trimmed === codeMarker[0]) {
      flushAll();
      out.push(codeBlocks[parseInt(codeMarker[1], 10)]);
      continue;
    }

    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    if (heading) {
      flushAll();
      out.push(headingHtml(heading[1].length, heading[2]));
      continue;
    }

    const table = tableAt(i);
    if (table) {
      flushAll();
      out.push(table.html);
      i = table.next - 1;
      continue;
    }

    if (trimmed === "") {
      if (lists.length && listContinuesAfterBlank(i)) continue;
      flushAll();
      continue;
    }

    const bullet = /^(\s*)[-*]\s+(.*)$/.exec(line);
    const numbered = /^(\s*)\d+\.\s+(.*)$/.exec(line);
    if (bullet || numbered) {
      flushParagraph();
      flushQuote();
      const m = bullet || numbered;
      addItem(bullet ? "ul" : "ol", m[1].length, line.length - m[2].length, m[2]);
      continue;
    }

    if (lists.length && indentOf(line) > 0) {
      continueItem(indentOf(line), trimmed);
      continue;
    }

    const quoted = /^\s*>\s?(.*)$/.exec(line);
    if (quoted) {
      flushParagraph();
      flushList();
      quote.push(quoted[1]);
      continue;
    }

    flushList();
    flushQuote();
    paragraph.push(trimmed);
  }
  flushAll();

  return out.join("\n");
}
