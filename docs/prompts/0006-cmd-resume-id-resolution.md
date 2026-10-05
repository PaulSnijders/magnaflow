---
title: "mf-worker: resolve resume: from a cmd filename, and explain it when it fails"
status: done
attempts: 0
created: 2026-08-11
---

## Context

A cmd file carried `resume: 0008-cmd-funnel` — the cmd file's *name* —
where `ResumeResolver` expects the command *id* (`0008-funnel`, the
`.magnaflow/<id>/` folder name). The value is id-shaped, so it was
classified as another command's id, no `.magnaflow/0008-cmd-funnel/`
existed, and the run died:

```text
resume: '0008-cmd-funnel' looks like a command ID, but no such command
has ever run
```

The value was resolvable, and the message names what was assumed rather
than what exists.

## Task

1. **Resolve a lane filename to its id.** In `ResumeResolver`: try the
   value verbatim against `.magnaflow/<value>/` first, then a normalized
   form — directory part and trailing `.md` dropped, and a
   `cmd`/`pln`/`qa`/`rst` segment dropped **only in the lane-infix
   position**, i.e. directly after the number and its optional letter.
   `0008-cmd-funnel` and `docs/prompts/0008B-rst-funnel.md` resolve;
   `0012-fix-qa-export` must come through untouched — a plain string
   replace would corrupt it. Verbatim stays first so a command whose slug
   really starts with a lane word resolves to itself.

2. **Unresolvable stays a hard error** — never a silent fresh session; a
   run that quietly lost its parent's context is worse than a stop. But
   the message names every id tried and the commands that have actually
   run, so the fix is a one-line edit instead of a hunt through
   `.magnaflow/`.

3. **Tests**: each accepted spelling; `0012-fix-qa-export` unchanged;
   verbatim wins over normalization; a raw session id still passes
   through verbatim; the error text contains the tried ids.

4. **Spec-first**: update the owning spec for the worker's resume
   behaviour in the same commit (or `tools/worker-controller/README.md`
   if there is none). `tools/mf-spec/system.md`'s "Session continuity
   (`resume:`)" section states the intent; flag any deviation in the
   report.

Not in scope: resolving a bare number, changing the raw-session-id path,
`group:`/self-resume precedence, or mf-watch/mf-cockpit id handling —
both compare whole id strings by design.
