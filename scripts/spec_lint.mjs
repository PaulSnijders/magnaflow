#!/usr/bin/env node
// Format lint for docs/specs/ — run by /spec-drift; its stdout is the
// body of STATUS.md's "Format problems" section.
//
// The specs are served in-app: the Help half (above `# Technical`) to all
// users, the Technical half to admins only. A malformed split marker can
// leak admin content, so these checks are mechanical, not eyeballed.
//
// Checks
// ------
// 1. Every spec file (surface pages, `_group.md`, concepts, `_overview.md`)
//    has one of the two valid shapes from docs/specs/README.md:
//      - with help: exactly two h1s, the second exactly `# Technical`,
//        with non-blank help text between them;
//      - without help: the file's first line is `# Technical` and it is
//        the only h1.
//    A title followed by an empty help section is rejected on purpose:
//    "forgotten" and "deliberately none" must not look alike.
// 2. Every folder directly under docs/specs/ is either `concepts/` or a
//    surface declared in docs/specs/config.yml.
// 3. Every `{#kebab-slug}` anchor is unique within its file.
// 4. Every `file.md#anchor` reference resolves to an existing explicit
//    anchor or heading in the target file.
// 5. Each concept's Technical section ends with a `Code:` line whose
//    path-like tokens all exist on disk (only `Why:` and `DRAFT:` lines
//    may follow it).
//
// Usage: node scripts/spec_lint.mjs [repo-root]
//   repo-root defaults to the parent of this script's folder.
// Output: one `- ` bullet per problem, or `none`. Exit 0 when clean,
// 1 when problems were found.
//
// No dependencies. Line endings are normalised on read, so a CRLF checkout
// does not fail the lint (but pin them anyway — see `.gitattributes`).
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const DOCS = path.join(ROOT, "docs");
const SPECS = path.join(DOCS, "specs");
const CONFIG = path.join(SPECS, "config.yml");

// Folders under docs/ that are never scanned: the prompt lane is a frozen
// record, the kit copy holds placeholders, and `temp/` is git-ignored
// scratch — generated reports whose own anchor slugs the project has no
// reason to keep in step.
const SKIP_DIRS = new Set(["prompts", "spec-kit", "temp"]);
// System files under docs/ whose `file.md#anchor` occurrences are not
// references: README.md and CLAUDE.md illustrate the convention, and
// STATUS.md carries this lint's own stdout verbatim — scanning it would
// re-report every broken anchor a second time, as an extra problem that
// can never be cleared.
const SKIP_REF_FILES = new Set([
  "docs/specs/README.md",
  "docs/CLAUDE.md",
  "docs/specs/STATUS.md",
]);
// Placeholder filenames used when describing the `file.md#anchor` convention.
const PLACEHOLDER_FILES = new Set(["file.md"]);
// Top-level docs/specs/ files that are not specs.
const NON_SPEC_FILES = new Set(["README.md", "STATUS.md", "ACCEPTED.md", "config.yml"]);

const FENCE = /^\s*(```|~~~)/;
const HEADING = /^(#{1,6})\s+(.*)$/;
const ANCHOR = /\{#([a-z0-9-]+)\}/;
const FILE_REF = /([\w./-]+\.md)#([a-z0-9-]+)/g;
const LINK = /\]\(([^)]+)\)/g;

const rel = (p) => path.relative(ROOT, p).replaceAll("\\", "/");
const read = (p) => fs.readFileSync(p, "utf-8").replace(/\r\n?/g, "\n");
const isDir = (p) => fs.existsSync(p) && fs.statSync(p).isDirectory();

// Surface names from config.yml: the keys nested one level under
// `surfaces:`. Deliberately not a YAML parser — the file's shape is fixed.
function surfacesFromConfig() {
  if (!fs.existsSync(CONFIG)) return null;
  const names = [];
  let inSurfaces = false;
  for (const line of read(CONFIG).split("\n")) {
    if (/^surfaces:\s*(\{\s*\})?\s*(#.*)?$/.test(line)) {
      inSurfaces = true;
      continue;
    }
    if (!inSurfaces) continue;
    if (/^\S/.test(line) && !line.startsWith("#")) break; // next top-level key
    const m = /^  ([A-Za-z0-9_-]+):\s*(#.*)?$/.exec(line);
    if (m) names.push(m[1]);
  }
  return names;
}

function mdFiles(base) {
  const out = [];
  const walk = (dir) => {
    if (SKIP_DIRS.has(path.basename(dir))) return;
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith(".md")) out.push(full);
    }
  };
  if (isDir(base)) walk(base);
  return out.sort();
}

// [level, rawText, lineIndex] for headings outside fenced code blocks.
function headings(text) {
  const out = [];
  let inFence = false;
  const lines = text.split("\n");
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (FENCE.test(line)) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;
    const m = HEADING.exec(line);
    if (m) out.push([m[1].length, m[2].trim(), i]);
  }
  return out;
}

// GitHub-style slug of a heading's visible text (anchor markup removed).
function ghSlug(headingText) {
  let t = headingText.replace(new RegExp(ANCHOR.source, "g"), "").trim().toLowerCase();
  t = t.replace(/[^\p{L}\p{N}_\s-]/gu, "");
  return t.replaceAll(" ", "-");
}

function validAnchors(text) {
  const out = new Set();
  for (const [, htext] of headings(text)) {
    const m = ANCHOR.exec(htext);
    if (m) out.add(m[1]);
    out.add(ghSlug(htext));
  }
  out.delete("");
  return out;
}

// Check 1 — returns a problem string or null.
function splitMarkerProblem(text) {
  const lines = text.split("\n");
  const h1s = headings(text).filter(([level]) => level === 1);
  const describe = () => h1s.map(([, h]) => "# " + h).join(", ") || "none";

  if (h1s.length === 1 && h1s[0][1] === "Technical") {
    if (lines[0].trim() !== "# Technical") {
      return "help-less form must start with `# Technical` on line 1";
    }
    return null;
  }
  if (h1s.length === 2 && h1s[1][1] === "Technical") {
    const between = lines.slice(h1s[0][2] + 1, h1s[1][2]);
    if (between.every((l) => l.trim() === "")) {
      return "title with an empty help section — drop the title and start " +
        "with `# Technical`, or write the help";
    }
    return null;
  }
  return `bad split marker: expected title + help + \`# Technical\`, or ` +
    `\`# Technical\` as the only h1 on line 1; found ${h1s.length} h1 (${describe()})`;
}

function main() {
  const problems = [];

  if (!isDir(SPECS)) {
    console.log(`- docs/specs/ not found under ${ROOT}`);
    return 1;
  }

  // --- Check 2: layout matches config.yml ---
  const surfaces = surfacesFromConfig();
  if (surfaces === null) {
    problems.push("docs/specs/config.yml missing — cannot tell surfaces from stray folders");
  }
  const allowedDirs = new Set(["concepts", ...(surfaces ?? [])]);
  const specFiles = [];
  for (const entry of fs.readdirSync(SPECS, { withFileTypes: true })) {
    const full = path.join(SPECS, entry.name);
    if (entry.isDirectory()) {
      if (allowedDirs.has(entry.name)) specFiles.push(...mdFiles(full));
      else if (surfaces !== null) {
        problems.push(
          `docs/specs/${entry.name}/ — folder is not a surface in config.yml ` +
            `(declare it, or move its specs under a declared surface)`
        );
      }
    } else if (entry.name.endsWith(".md") && !NON_SPEC_FILES.has(entry.name)) {
      specFiles.push(full);
    }
  }
  specFiles.sort();
  for (const s of surfaces ?? []) {
    if (!isDir(path.join(SPECS, s))) {
      problems.push(`docs/specs/${s}/ — surface declared in config.yml has no folder`);
    }
  }

  // --- Check 1: split marker shape ---
  const allDocs = mdFiles(DOCS);
  const textOf = new Map(allDocs.map((p) => [p, read(p)]));
  for (const p of specFiles) {
    if (!textOf.has(p)) textOf.set(p, read(p));
    const problem = splitMarkerProblem(textOf.get(p));
    if (problem) problems.push(`${rel(p)} — ${problem}`);
  }

  // --- Check 3: per-file anchor uniqueness ---
  for (const p of allDocs) {
    const seen = new Set();
    const dup = new Set();
    for (const [, htext] of headings(textOf.get(p))) {
      const m = ANCHOR.exec(htext);
      if (m) (seen.has(m[1]) ? dup : seen).add(m[1]);
    }
    for (const slug of [...dup].sort()) {
      problems.push(`${rel(p)} — duplicate anchor {#${slug}}`);
    }
  }

  // --- Check 4: file.md#anchor references resolve ---
  const anchorsOf = new Map(allDocs.map((p) => [p, validAnchors(textOf.get(p))]));
  const byBasename = new Map();
  for (const p of allDocs) {
    const b = path.basename(p);
    if (!byBasename.has(b)) byBasename.set(b, p);
  }
  const resolveFile = (filepart, referrer) => {
    let fp = filepart.replace(/^[./]+/, "");
    if (fp.startsWith("docs/")) fp = fp.slice("docs/".length);
    const candidates = [
      path.join(DOCS, fp),
      path.join(path.dirname(referrer), filepart),
      byBasename.get(path.basename(fp)),
    ];
    for (const c of candidates) {
      if (c && fs.existsSync(c) && fs.statSync(c).isFile()) return path.resolve(c);
    }
    return null;
  };
  const checkRef = (filepart, anchor, referrer) => {
    if (PLACEHOLDER_FILES.has(path.basename(filepart))) return;
    let target;
    if (filepart === "") {
      target = referrer;
    } else {
      target = resolveFile(filepart, referrer);
      if (target === null) {
        problems.push(`${rel(referrer)} — reference to missing file \`${filepart}#${anchor}\``);
        return;
      }
    }
    if (!(anchorsOf.get(target) ?? new Set()).has(anchor)) {
      problems.push(
        `${rel(referrer)} — broken anchor \`${filepart || path.basename(target)}#${anchor}\` ` +
          `(no \`{#${anchor}}\` or heading in ${rel(target)})`
      );
    }
  };
  for (const p of allDocs) {
    if (SKIP_REF_FILES.has(rel(p))) continue;
    const text = textOf.get(p);
    for (const m of text.matchAll(FILE_REF)) checkRef(m[1], m[2], p);
    for (const m of text.matchAll(LINK)) {
      const target = m[1];
      const idx = target.indexOf("#");
      if (idx < 0) continue;
      const filepart = target.slice(0, idx);
      const anchor = target.slice(idx + 1);
      if (!/^[a-z0-9-]+$/.test(anchor)) continue;
      if (filepart === "" || filepart.endsWith(".md")) checkRef(filepart, anchor, p);
    }
  }

  // --- Check 5: concept Code: line at the end, paths exist ---
  const conceptsDir = path.join(SPECS, "concepts");
  for (const p of specFiles.filter((f) => path.dirname(f) === conceptsDir)) {
    const lines = textOf.get(p).split("\n");
    let codeIdx = null;
    for (let i = lines.length - 1; i >= 0; i--) {
      if (lines[i].startsWith("Code:")) {
        codeIdx = i;
        break;
      }
    }
    if (codeIdx === null) {
      problems.push(`${rel(p)} — concept Technical section has no \`Code:\` line`);
      continue;
    }
    // The Code: paragraph runs until the first blank line.
    const block = [];
    let j = codeIdx;
    while (j < lines.length && lines[j].trim() !== "") block.push(lines[j++]);
    const trailing = lines.slice(j).filter((l) => l.trim() !== "");
    if (trailing.some((l) => !/^(Why:|DRAFT:)/.test(l.trim()))) {
      problems.push(`${rel(p)} — \`Code:\` line is not the last content of the file (only \`Why:\` and \`DRAFT:\` may follow)`);
    }
    // Path-like tokens: contain a slash; backticks, commas and brackets
    // stripped. Symbol names in parentheses have no slash and are ignored;
    // so are URL routes (leading `/`), globs and `{placeholders}`. A path
    // wrapped after a `/` at the line end is joined back together.
    const tokens = block
      .map((l) => l.trim())
      .filter((l) => !/^(Why:|DRAFT:)/.test(l))
      .reduce((acc, l) => (acc.endsWith("/") ? acc + l : acc + " " + l), "")
      .replace(/^\s*Code:\s*/, "")
      .split(/[\s,;`()]+/)
      .map((t) => t.replace(/^[`'"(]+|[`'".:)]+$/g, ""))
      .filter((t) => t.includes("/") && !t.startsWith("/") && !/[*{]/.test(t));
    if (tokens.length === 0) {
      problems.push(`${rel(p)} — \`Code:\` line names no paths`);
    }
    for (const token of tokens) {
      if (!fs.existsSync(path.join(ROOT, token))) {
        problems.push(`${rel(p)} — \`Code:\` path not found: ${token}`);
      }
    }
  }

  const unique = [...new Set(problems)].sort();
  console.log(unique.length ? unique.map((x) => `- ${x}`).join("\n") : "none");
  console.error(`spec_lint: ${unique.length} problem(s)`);
  return unique.length ? 1 : 0;
}

process.exit(main());
