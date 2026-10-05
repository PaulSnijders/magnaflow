# Technical

`specs.html?p=<name>[&path=<relative>]` browses one project's
`docs/specs/` tree, read-only. The cockpit never edits specs.
Appearance follows [design](../concepts/design.md#shared-conventions).

## Browsing

`path` is relative to `docs/specs/`, with `/` separators. Each segment
of the header breadcrumb links back up the tree. A directory lists its
folders first, then its files, each alphabetically (case-insensitive).
A file is fetched as text and rendered as markdown, whatever its
extension.

`GET /api/projects/{name}/specs[/{**path}]` answers
`{type: "dir", entries: [{name, isDirectory}]}` or
`{type: "file", content}`.

## Path safety

`path` is user input from the URL, and guarding it is the point of this
page's endpoint (`PathGuard.ResolveInside`). A rooted path, or any path
containing `:`, is refused before resolving. After normalization, the
result must still lie inside `docs/specs/`. An escape is a 400; a path
that does not exist is a 404. No file outside the specs root is ever
served.

## Live updates

None for the tree itself, because `docs/specs/` is not watched. Only the
summary bar follows `lane` and `watch` events. An open spec is as fresh
as its page load.

BUG: links inside a rendered spec keep their raw relative target and
open in a new tab against the cockpit's own root. For example,
`../concepts/design.md` becomes `/concepts/design.md`, a 404. Unlike
lane-file links on the command page, spec cross-links are not rewritten
to `specs.html?path=…`, so no cross-link between specs works here.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0007-cockpit-design.md
