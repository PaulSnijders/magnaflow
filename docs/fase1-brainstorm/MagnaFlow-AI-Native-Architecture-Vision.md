# MagnaFlow -- AI-Native Application Architecture

## Core vision

MagnaFlow shifts software development from **code-centric** to
**knowledge-centric**.

Instead of:

Vision → Requirements → Code → Manual testing

it becomes:

Vision → Product Specs → Interaction Specs → Planned Changes → AI
Implementation → Agent Verification → Human Verification → Knowledge
updated.

Code is just one artifact.

## Key ideas

-   Spec-first development.
-   Conversations lead first to ideas, then accepted
    decisions, and only then specs.
-   Interaction Specs form an executable specification.
-   A single Semantic Interaction Model is the source for:
    -   Human UI
    -   Agent View
    -   Tests
    -   Documentation
    -   Accessibility
    -   Debugging

## Traceability

All parts are linked:

Vision → Feature → Interaction Spec → Implementation → Tests → Reports

AI maintains these relationships automatically.

## Agent View

Not HTML but semantics.

Business apps: - fields - tables - actions - validation

Graphical apps: - scenes - actors - goals - feedback - animations

The graphics remain entirely free; only the meaning is made explicit.

## Test strategy

1.  Agent tests via the Semantic Interaction Model.
2.  Human UI tests via screenshots and AI analysis.
3.  Reports with screenshots and findings are stored.

## Planned Changes

Changes get their own lifecycle:

-   proposed
-   accepted
-   impact analysed
-   implementation pending
-   implementing
-   testing
-   completed

## Prompts

Prompts are execution history, not the source of truth.

Source of truth:

Decision → Spec → Change → AI execution.

## Architecture

Conversation → Knowledge → Product Specs → Interaction Specs → Runtime
Semantic Model → Human UI / Agent View / Tests / Documentation

## Open source inspiration

-   GitHub Spec Kit (spec-first)
-   Model Context Protocol (MCP)
-   AG-UI
-   Google A2UI
-   XState / Stately
-   Playwright

## Design principles

-   Every constraint must help AI.
-   Constrain developers as little as possible.
-   Everything important must be traceable.
-   Everything traceable must be maintained automatically.
-   The agent view is not a goal, but a logical consequence of AI-native
    software development.
