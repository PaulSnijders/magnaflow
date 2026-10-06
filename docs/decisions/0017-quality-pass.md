---
date: 2026-10-06
topic: mf-spec, quality, security, review
status: accepted
---

# 0017 — Code quality: principles in design.md, a quality pass per PR

## Situation

AI agents write most of the code. In MagnaFlow itself nothing went
wrong, but in team projects with junior developers who mostly prompt,
plans and answers look fine while code and specs get less structured,
and the project grows faster than its quality. Nothing hurts yet; it
will. The aim is to get ahead of it cheaply, without control for
control's sake and without asking much of the developers.

Outside evidence (2025–2026): duplication up and refactoring down in
AI-era code (GitClear), OWASP-class flaws in close to half of LLM output
(Veracode), AI as an amplifier of both throughput and instability
(DORA 2025). Böckeler's "harness engineering" (martinfowler.com,
2026-04) splits the harness into *guides* (specs, CLAUDE.md, skills —
steer before) and *sensors* (tests, linters, review — feedback after),
deterministic sensors first. The kit has strong guides; its sensors are
thin. LLM-assigned 0–10 scores are noisy, yes/no checks against named
rules are stable, and false positives teach people to ignore findings.

## Options

1. **Kit text plus a habit.** design.md carries engineering principles
   (a guide every `/architect` cmd is checked against); the adopt prompt
   asks once whether to set up a strict build or only advise; a quality
   pass per PR with the built-in Claude Code review skills, triaged by a
   human; `/spec-drift` shows when the last pass was.
2. Option 1 plus a kit command `/quality` that checks the code against
   the design.md principles and writes its own dated report.
3. Nothing in the kit; a company guideline page.

Also weighed and dropped inside option 1: gstack's `/health` and `/cso`
as the default (third-party install per developer, interactive, state in
`~/.gstack` outside git, `/health` is JS/TS-oriented and skips most of a
.NET repo); turning findings into cmds automatically (false-positive
rates would flood the lane); a quality scorecard with grades.

## Measurement

What reaches the repos where it matters, at the lowest running cost for
the developer, using only state in git. Option 3 does not reach the
repos. Option 2 overlaps `/security-review` and `/code-review` and adds a
report to ignore; it stays available if option 1 shows gaps.

## Choice

Option 1, kit 1.2. The pass is part of the developer's own workflow
(running `/security-review` before the PR is fine even with a security
action in CI — it simply passes). Its record is a context file, so no
new genre. The `/spec-drift` line flags a pass older than 30 days but not
"none", so a fresh adoption does not nag. Strict build in this repo:
advice only for now; setting it up is a separate cmd.
