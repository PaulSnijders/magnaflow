---
title: Add an about page
status: ready
branch: task/0003-about-page
base: task/0002-add-styling   # continue stacking on the styled site
group: website                # same group as 0002: reuses its agent session in run-all
specs:
  - docs/specs/website.md
attempts: 0
max_attempts: 3
created: 2026-07-08
---

## Goal

An about page at `src/about.html`, linked from the homepage nav.

## Acceptance criteria

- [ ] `src/about.html` exists, uses the shared stylesheet, and briefly describes the site
- [ ] Both pages have a `<nav>` linking Home and About
