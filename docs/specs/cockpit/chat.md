# Technical

`chat.html?p=<name>` is a read-only AI chat about one project. It
answers questions and can turn an answer into a draft command. It never
does work itself; the lane does. The nav link appears on every page only
when `chat.enabled` is true (default true). Otherwise the endpoint
answers 403. Appearance follows
[design](../concepts/design.md#shared-conventions).

## Read-only by construction

Each message spawns `chat.command` (default `claude`) in the project
root as `-p --output-format stream-json --verbose --permission-mode plan`,
with the message on stdin. `chat.args` are appended after the fixed args,
so config can narrow the agent but never remove plan mode. The process
has a hard timeout (`chat.timeout_minutes`, default 5) and is killed when
the browser disconnects.

## Stream contract

`POST /api/projects/{name}/chat` with `{message, chatId?}` (an empty
message is a 400) answers with an SSE stream of `data:` frames:

- `{type: "start", chatId}`: first, always. A new `chatId` is minted when
  none was sent.
- `{type: "line", text}`: every raw agent output line, as it arrives. The
  page previews only the assistant's text from these and never shows the
  raw JSON.
- `{type: "done", chatId, sessionId, finalText, timedOut, exitCode}`:
  last. `finalText` is the `result` field of the stream's terminal
  `result` event, the same value the worker harvests. It replaces the
  preview, rendered as markdown.

## Continuity

The `chatId` lives in the tab's `sessionStorage`, one per project. The
server maps `(project, chatId)` to the agent session id and resumes it
with `--resume` on the next message. That map is in memory only:

- Reloading the tab keeps the session but clears the visible
  conversation, because the log exists only in the DOM.
- A cockpit restart silently starts a fresh agent session for the same
  tab.

## Make this a command

Enabled once a reply has a `finalText`. It prompts for a title and
creates a normal draft whose body is the last reply, unedited, through
the same write as the project page's New draft
(`POST /api/projects/{name}/commands`). Then the browser goes to the new
draft's command page.

## Busy banner

When any command in the project is `running`, a banner says the chat
shares the machine with it. It is informational only and never blocks.
It refreshes on `lane` events, after each reply, and every 15 s.

BUG: the markdown renderer (`md.js`) copies link targets verbatim, so a
`javascript:` URL in an agent reply becomes a clickable link. This
matters most here, because the rendered text is model output.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0007-cockpit-design.md
