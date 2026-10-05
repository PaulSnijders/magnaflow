---
date: 2026-07-01
topic: vision
source: AI brainstorm summary (date approximate: before 2026-07-02)
---

# MagnaFlow AI -- Conversation Summary

## Core idea

MagnaFlow is **not another AI coding assistant**. It is an **open,
Git-native software development workflow** where AI is one
interchangeable component.

The repository is the source of truth.

## Philosophy

-   Keep it simple, but compose simple tools into powerful systems.
-   Everything important is plain text.
-   Markdown + YAML front matter.
-   Git is the database and history.
-   Local-first.
-   Open formats.
-   Small deterministic tools.
-   Humans define intent, AI performs work.
-   AI is replaceable.
-   Observable over magical.
-   Restartable and idempotent.

## Repository structure

> **Superseded:** the pinned structure lives in
> `docs/decisions/0001-worker-workflow-v0-1.md` — that document is the
> single source of truth.

``` text
docs/                 # knowledge and specs
src/                  # implementation
tests/

.magnaflow/
  config.yml
  tasks/
    0001-name/        # task folder: task.md (definition + status) + logs + result.yml
```

Everything is versioned in Git. Ideas like `help/`, `decisions/` and
`scripts/` from this brainstorm are not pinned yet.

## Specs

Use Markdown with YAML front matter.

The compiler validates: - unique IDs - references - dependencies -
acceptance criteria - links between specs, code and tests

Help pages are part of the specifications.

## Workflow

Intent → Specification → Plan → Implementation → Tests → Review → Merge

Example CLI:

-   mf check
-   mf plan
-   mf apply
-   mf review
-   mf sync
-   mf release

## Worker architecture

Main laptop: - control - review

Worker machine (old Ubuntu laptop): - Claude Code - builds - tests -
spec daemon - tmux

Phone: - approve - comment - voice steering

## AI

Preferred architecture:

MagnaFlow → Claude Code

Hermes/OpenClaw are optional orchestrators, not the core.

They should call MagnaFlow commands instead of freely controlling the
computer.

## Existing projects to study

### Highly relevant

-   GitHub Spec Kit
-   OpenSpec
-   Kiro
-   BMAD Method
-   specs.md / AI-DLC

### Foundations

-   Docs as Code
-   Backstage TechDocs

### Coding workers

-   Claude Code
-   Aider
-   Codex CLI
-   Gemini CLI
-   Continue
-   Cline
-   OpenHands

## Differentiation

Not another AI IDE.

Focus on:

-   Git-native
-   Markdown-first
-   Local-first
-   Help/spec/code synchronized
-   Small composable tools
-   Open repository standard
-   AI-agnostic workflow

## Long-term vision

Become a **Developer Operating System**.

The repository becomes the API.

MagnaFlow becomes the glue between: - specifications - documentation -
help - coding agents - tests - deployment

## Possible mission

Provide an open path for modern software development.

Help developers build software with AI while keeping ownership,
simplicity and openness.

## Possible slogan

**You walk. MagnaFlow builds.**

or

**You walk. We light the path.**
