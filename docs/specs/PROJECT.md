# Project rules — deviations from the spec kit

The kit's files are replaced on every update: `docs/specs/README.md`,
`docs/CLAUDE.md`, `/spec`, `/spec-drift`, `/cmd-go` and the `specs` and
`magnaflow` skills. What this project does differently goes here, under
a heading named after what it changes: a README section (`## Branches`)
or a file (`## cmd-go`, `## docs/CLAUDE.md`). Where this file and a kit
file disagree, this file wins. No update touches it.

State the rule, not the diff: "Every cmd gets its own branch
`feat/NNNN-short-name`" rather than "change the default in Branches".
Rules every session needs, whatever it works on, go in the root
`CLAUDE.md` instead, outside the kit's markers.

Nothing yet.
