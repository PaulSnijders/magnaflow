# MagnaFlow vs. Anthropic's "AI-native SDLC playbook"
Status: dropped
Supersedes: —

Source: https://claude.com/resources/articles/the-ai-native-sdlc-playbook

## Problem
Does the playbook show gaps in MagnaFlow worth closing, and what do its
`intent.md` / `spec.md` artifacts teach us?

Playbook core: six stages (Plan, Design, Build, Test, Deploy, Maintain);
each stage ends in a committed artifact the next stage reads; the commit
chain is the audit trail. Maturity: by hand → command/skill → triggered
on artifact commit → closed loop from monitoring.

## Mapping (today)
| Playbook | MagnaFlow | Level |
|---|---|---|
| intent.md (originator's ask, committed) | `/brainstorm` note — git-ignored scratch; `docs/context/` for outside asks | 1-2, ask not committed |
| spec.md (per-change design spec) | state specs edited ahead + decision + cmd `## Context` (`/architect`) | 2 |
| plan.md (human-approved) | worker plan phase; pln only on questions (0015); human gate = draft→ready | 3, gate moved earlier |
| Build: CLAUDE.md, skills, hooks, parallel | worker + kit skills; serial per project; no hooks | 3 |
| Test: in-session loops, evals in CI | build+test every run, bounded retries; no CI, no evals | 2-3 |
| Deploy: agentic PR review, gates | commit to main; `cc-review` + quality pass by hand; stable instance, self-update | 2 |
| Maintain: monitor → intent.md | scheduled actions exist; no finding → cmd/intent loop | 1 |

## Premises (draft, not yet agreed)
1. MagnaFlow's state specs beat a per-change spec.md: one truth, no pile of stale change specs (the article's own "dual source of truth" pitfall). Do not adopt spec.md.
2. The real gap is Plan: the originator's ask is not committed. The cmd's Context is the architect's retelling; the brainstorm note is deliberately thrown away.
3. "What was asked" vs "what was decided" as a committed pair is valuable for review (`cc-review`) and for rework metrics.
4. Advisory-only enforcement of the hard rule is a known, reasoned choice (KIT.md rejected list); the playbook's "add a hook" does not override it for a solo, main-only repo.

## Directions
(to fill after premises are agreed)

## Open questions
- Who is the originator in MagnaFlow projects other than the user? (Clients via context/? Incidents?)
- Is intent worth a file, or a section in the cmd?

## For the architect
Read decision 0016 (genres and gate), docs/context/README.md, KIT.md rejected list.

## Chosen
Dropped. Premise 2 disputed by the user: the raw ask is refined in the
conversation into the cmd on purpose; odd phrasing would confuse the
worker, and storing it separately only adds words (cf. kit 1.3 weight).
No improvements for MagnaFlow from the article.

Is MagnaFlow an SDLC? Yes: idea (brainstorm) -> design (state specs,
decision) -> build+test (worker) -> review (cc-review, quality pass) ->
deploy (main = continuous delivery, stable instance, self-update).
Missing: a board/backlog view (magnaboard, in progress) and a
finding -> cmd loop in Maintain. Release planning is deliberately absent:
release stays "commit to main"; a project with a production gate keeps
that gate in its own config/actions.
