---
title: Add shared styling
status: ready
branch: task/0002-add-styling
base: task/0001-hello-page    # stack on command 0001's (unmerged) work branch
group: website                # share the agent session with adjacent 'website' commands
specs:
  - docs/specs/website.md
attempts: 0
max_attempts: 3
created: 2026-07-08
---

## Goal

A stylesheet at `src/style.css`, linked from `src/index.html`.

## Context

Command 0001 created the homepage on branch `task/0001-hello-page`; this command builds on
that branch (see `base:` above) because it needs the page to exist.

## Acceptance criteria

- [ ] `src/style.css` exists with basic typography and a centered content column
- [ ] `src/index.html` links the stylesheet
