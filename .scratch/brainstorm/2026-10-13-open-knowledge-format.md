# Google Open Knowledge Format (OKF) vs our spec system
Status: direction chosen — B implemented (uncommitted, no kit version bump)

## Problem
User saw Google's OKF blog. Feels it is close to our spec system (knowledge
as markdown), differs in per-file status/dates. Wants: what is OKF, adoption,
comparison, and what to adopt (esp. title + description on every file for
search / AI retrieval).

## Research: OKF (2026-10-13)
- Blog 2026-06-12 (BigQuery tech leads), v0.1; now v0.2 (~1,000 lines).
  Repo GoogleCloudPlatform/open-knowledge-format, Apache-2.0, ~940 stars,
  no Google commit since 2026-08-21; 28 open issues/PRs.
- Format: bundle = dir of .md; file = concept, path = ID. Required frontmatter:
  `type` only (free text). Recommended: title, description, resource, tags.
  Reserved index.md (link list) and log.md (dated change log).
  v0.2: sources (provenance), generated/verified (trust tiers),
  status draft|stable|deprecated (default stable), stale_after.
  Links: plain untyped markdown. Conformance = frontmatter parses + has type.
  No JSON Schema, no official validator/MCP/SDK.
- Purpose: context about DATA assets (tables, metrics, joins), largely
  LLM-generated from BigQuery; trust fields exist because content is machine-made.
  Only first-party consumer: Knowledge Catalog (Dataplex) via kcmd — carries
  7 keys, drops cross-links, "untested beyond 14-file demo".
- Adoption: Google-controlled, no partners/vendors, ~80 repos with okf_version,
  some community tools (linters, WP plugin). Reception mixed: "markdown + YAML,
  15kb spec", untyped links, structural not semantic interop, commoditization play.

## Grounding (our side)
- specs/README: "No frontmatter. File path is the identity, git history is the timestamp."
  specs: 24 files, 1 with frontmatter.
- Records already have frontmatter: decisions (date/topic/status/supersedes),
  context, prompts (cmd status lifecycle; rst title/cmd/done/summary).
- KIT.md rejected: "mandatory Date/Topic/Status header on records",
  "index README per folder" — already weighed (scale).
- design.md II: identity in filename, never duplicated in content.
- FINDING: every spec's first paragraph under `# Technical` already is a
  one/two-sentence description (checked all 22). `_overview.md` = root index.md.
  git log = log.md. Folder (genre) = OKF `type`. /spec-drift = stronger trust
  than verified stamps (checks against code).

## Premises (draft, not yet agreed)
1. OKF and our system share the substrate (md + YAML + git), not the job:
   OKF describes data assets for sharing across tools/orgs; our specs are the
   source of truth for building software in one repo, audited against code.
2. Status/dates per spec add nothing: git is the timestamp, drift audit is the
   freshness check; status already exists where a lifecycle exists (cmd, decisions).
3. A per-file description is valuable, and we already have it — as prose
   (first paragraph of each spec, rst `summary:`, decision `topic:`).
4. OKF is not (yet) a standard worth conforming to: single vendor, stalled,
   no consumer that would read our docs.
5. Frontmatter on specs would duplicate the first paragraph -> drift risk,
   against design II.

## Directions
A. Do nothing; file this research in docs/context/ (or just this note) and add
   "OKF conformance / title+description frontmatter on specs" to KIT.md rejected.
   Effort ~0. Risk: none. Reuses KIT rejected list.
B. (recommended) Make the existing habit a rule: specs/README states the first
   paragraph of `# Technical` is the file's description (1-2 sentences, what it
   is, standalone); /spec writes it so; optional spec_lint check that it exists.
   Effort: small kit change (README + /spec + lint), no migration (all 22 comply).
   Risk: low. Reuses spec_lint, /spec, design II.
C. OKF-lite frontmatter (type/title/description) on all docs files -> conformant
   bundle. Effort: kit change + ~100 files + lint. Risk: reverses "no
   frontmatter", duplication/drift, payoff only if a consumer appears.
   (Variant: an on-demand OKF export script — YAGNI until someone asks.)

## Chosen
B, rule only (no lint check), no version bump; rides along with the next kit change. Plus a KIT.md rejected row for OKF frontmatter. Otherwise nothing; align with OKF in new parts only when convenient.

## Open questions
- Does the user actually search descriptions today, or is it hypothetical?
- Should the other genres (context) get a description line too? decisions have topic:, rst summary:.

## For the architect
Read: specs/README (frontmatter line), KIT.md rejected table, design.md II,
scripts/spec_lint.mjs. Decide first: rule-only or rule + lint.
