// @ts-check
// chat.html (docs/specs/cockpit/chat.md). Page pattern (docs/specs/cockpit/index.md#page-rendering):
// the conversation, the busy flag and the in-flight send live in `state`; the streamed reply is
// one message object updated in place and re-rendered per frame. The input box is UI state in
// the form. Read-only: the chat never writes; "Make this a command" creates a draft through the
// same endpoint as everywhere else.
import { html, render, useRef, Breadcrumb, LiveBadge, Markdown, ctrlEnter } from "../ui.js";
import * as Cockpit from "../app.js";

/** @typedef {import("../types.js").ChatFrame} ChatFrame */
/** @typedef {import("../types.js").CreatedCommand} CreatedCommand */
/** @typedef {import("../types.js").ProjectSummary} ProjectSummary */
/** @typedef {import("../types.js").LiveState} LiveState */
/**
 * @typedef {object} Message
 * @property {"user" | "agent"} role
 * @property {string} text  plain text: the user's message, the streaming preview, or a fallback
 * @property {string | null} [markdown]  the final reply, rendered through md.js
 * @property {boolean} [timedOut]
 */

const project = Cockpit.qs("p") || "";
const base = `/api/projects/${encodeURIComponent(project)}`;
document.title = "chat · " + project;

const chatIdKey = "mf-cockpit-chat-" + project;
let chatId = sessionStorage.getItem(chatIdKey) || null;

const state = {
  /** @type {LiveState | null} */ live: null,
  /** @type {Message[]} */ messages: [],
  busy: false,
  sending: false,
  lastAgentText: "",
};

const mount = /** @type {HTMLElement} */ (document.getElementById("app"));

// Autoscroll follows the reply only while the reader is at the bottom of the log: scrolling up
// mid-stream to reread something is not undone by the next frame. Sending always scrolls down.
function rerender(/** @type {boolean} */ forceScroll = false) {
  const log = document.getElementById("chat-log");
  const atBottom = !log || log.scrollHeight - log.scrollTop - log.clientHeight < 8;
  render(html`<${Page} state=${state}/>`, mount);
  const after = document.getElementById("chat-log");
  if (after && (forceScroll || atBottom)) after.scrollTop = after.scrollHeight;
}

// Every raw stream-json line (system/tool-call/assistant/result events) reaches the client as
// its own "line" frame; only the assistant's own text is worth a live preview while streaming.
function extractAssistantPreview(/** @type {any} */ obj) {
  if (obj && obj.type === "assistant" && Array.isArray(obj.message?.content)) {
    const text = obj.message.content.filter((/** @type {any} */ c) => c.type === "text").map((/** @type {any} */ c) => c.text).join("\n");
    return text || null;
  }
  return null;
}

async function checkBusy() {
  try {
    /** @type {ProjectSummary} */
    const summary = await Cockpit.api(base);
    state.busy = summary.counts.running > 0;
    rerender();
  } catch { /* non-fatal */ }
}

async function send(/** @type {string} */ message) {
  state.messages.push({ role: "user", text: message });
  /** @type {Message} */
  const agent = { role: "agent", text: "" };
  state.messages.push(agent);
  state.sending = true;
  state.lastAgentText = "";
  rerender(true);

  try {
    const res = await fetch(`${base}/chat`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message, chatId }),
    });
    if (!res.ok || !res.body) throw new Error(`${res.status} ${res.statusText}`);

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      let sep;
      while ((sep = buffer.indexOf("\n\n")) >= 0) {
        const frame = buffer.slice(0, sep);
        buffer = buffer.slice(sep + 2);
        const dataLine = frame.split("\n").find((l) => l.startsWith("data: "));
        if (!dataLine) continue;
        /** @type {ChatFrame} */
        const payload = JSON.parse(dataLine.slice("data: ".length));

        if (payload.type === "start") {
          chatId = payload.chatId || null;
          if (chatId) sessionStorage.setItem(chatIdKey, chatId);
        } else if (payload.type === "line") {
          let obj = null;
          try { obj = JSON.parse(payload.text || ""); } catch { /* not a JSON line; nothing to preview */ }
          const preview = extractAssistantPreview(obj);
          if (preview) {
            agent.text = preview; // still streaming — plain text, replaced by rendered markdown once "done" arrives
            rerender();
          }
        } else if (payload.type === "done") {
          state.lastAgentText = payload.finalText || "";
          if (payload.finalText) {
            agent.markdown = payload.finalText;
          } else if (!agent.text) {
            agent.text = payload.timedOut ? "(timed out — no response)" : "(no response)";
          }
          agent.timedOut = !!payload.timedOut;
          rerender();
        }
      }
    }
  } catch (err) {
    agent.markdown = null;
    agent.timedOut = false;
    agent.text = "error: " + /** @type {Error} */ (err).message;
  } finally {
    state.sending = false;
    rerender();
    checkBusy();
  }
}

async function escalate() {
  const title = prompt("Title for the new draft command:");
  if (!title) return;
  try {
    /** @type {CreatedCommand} */
    const result = await Cockpit.api(`${base}/commands`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ title, body: state.lastAgentText }),
    });
    location.href = `command.html?p=${encodeURIComponent(project)}&id=${encodeURIComponent(result.id)}`;
  } catch (err) {
    alert(/** @type {Error} */ (err).message);
  }
}

/** @param {{message: Message}} props */
function ChatMessage({ message: m }) {
  if (m.role === "user") return html`<div class="chat-msg user">${m.text}</div>`;
  const note = m.timedOut ? html`<div class="muted">[timed out]</div>` : null;
  // The rendered reply is itself the bubble, so `.chat-msg.agent > *:first-child` still reaches
  // the markdown's own first block. A timed-out note then sits just under the bubble.
  if (m.markdown) return [html`<${Markdown} class="chat-msg agent" source=${m.markdown}/>`, note];
  return html`<div class="chat-msg agent">${m.text}${note}</div>`;
}

/** @param {{sending: boolean, canEscalate: boolean}} props */
function ChatForm({ sending, canEscalate }) {
  // Uncontrolled: the DOM keeps what is typed across every re-render, and submit reads it there.
  const inputRef = useRef(/** @type {HTMLTextAreaElement | null} */ (null));
  const onSubmit = (/** @type {Event} */ e) => {
    e.preventDefault();
    const el = inputRef.current;
    const message = el ? el.value.trim() : "";
    if (!el || !message) return;
    el.value = "";
    send(message);
  };
  // Ctrl+Enter sends (ontwerp-v0.5.md item 9), through the form so `required` holds.
  const onKeyDown = ctrlEnter(() => {
    const form = document.getElementById("chat-form");
    if (form) /** @type {HTMLFormElement} */ (form).requestSubmit();
  });
  return html`
    <form id="chat-form" class="row" onSubmit=${onSubmit}>
      <textarea id="chat-input" rows="2" placeholder="Ask about this project…" required
        ref=${inputRef} onKeyDown=${onKeyDown}></textarea>
      <div>
        <button type="submit" id="send-btn" title="Ctrl+Enter to send" disabled=${sending}>Send</button>
        <button type="button" id="escalate-btn" class="secondary" disabled=${!canEscalate} onClick=${escalate}>Make this a command</button>
      </div>
    </form>`;
}

function Page({ state: s }) {
  return html`
    <main>
      ${s.busy ? html`<div id="busy-banner" class="busy-banner">
        A command is currently running in this project — the chat shares the machine with it.
      </div>` : null}
      <div class="card">
        <h2>Chat (read-only)</h2>
        <div class="chat-log" id="chat-log">${s.messages.map((/** @type {Message} */ m) => html`<${ChatMessage} message=${m}/>`)}</div>
        <${ChatForm} sending=${s.sending} canEscalate=${!s.sending && !!s.lastAgentText}/>
      </div>
    </main>`;
}

// ---- boot ----

const crumb = document.getElementById("breadcrumb");
if (crumb) {
  render(html`<${Breadcrumb} segments=${[
    { label: "mf-cockpit", href: "index.html" },
    { label: project, href: "project.html?p=" + encodeURIComponent(project) },
    { label: "chat" },
  ]}/>`, crumb);
}
Cockpit.wireNav(project);

const liveSlot = document.getElementById("live");
function renderLive() {
  if (liveSlot) render(html`<${LiveBadge} live=${state.live}/>`, liveSlot);
}
renderLive();
rerender();

// Live marker + refresh the "a command is running" busy banner on lane changes (ontwerp-v0.5.md
// item 1); the 15s interval below stays as a belt-and-braces fallback, and every reply re-checks.
Cockpit.live({
  project,
  onState: (/** @type {LiveState} */ live) => { state.live = live; renderLive(); },
  handlers: [{ kind: "lane", refetch: () => checkBusy() }],
});

checkBusy();
setInterval(checkBusy, 15000);
