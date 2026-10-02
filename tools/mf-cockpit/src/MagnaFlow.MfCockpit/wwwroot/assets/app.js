// Shared helper for every page: no build step, no framework, no CDN (ontwerp-v0.1.md "Tech").
const Cockpit = (() => {
  function qs(name) {
    return new URLSearchParams(location.search).get(name);
  }

  async function api(path, options) {
    const res = await fetch(path, options);
    if (!res.ok) {
      let detail = "";
      try { detail = (await res.json()).error || ""; } catch { /* not JSON */ }
      throw new Error(`${res.status} ${res.statusText}${detail ? ": " + detail : ""}`);
    }
    if (res.status === 204) return null;
    const ct = res.headers.get("content-type") || "";
    return ct.includes("application/json") ? res.json() : res.text();
  }

  function escapeHtml(s) {
    return String(s ?? "").replace(/[&<>"']/g, (c) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
    })[c]);
  }

  function statusPill(status) {
    const s = escapeHtml(status || "unknown");
    return `<span class="pill ${s}">${s}</span>`;
  }

  function formatDate(iso) {
    if (!iso) return "";
    try { return new Date(iso).toLocaleString(); } catch { return iso; }
  }

  // Tab titles (docs/prompts/0010's rst, "Follow-up"). A tab strip truncates hard, so the
  // distinctive part goes first
  // and the app identity is left to the favicon rather than a "mf-cockpit — " prefix every tab
  // shares — with the prefix, six open cockpit tabs all read "mf-cockpit — ma…".
  const STATUS_ICON = {
    running: "🟠", questions: "❓", aborted: "🔴", ready: "🟢", draft: "⚪", done: "✅", malformed: "⚠️",
  };
  function statusIcon(status) { return STATUS_ICON[status] || ""; }

  // A project's whole lane collapsed into the one thing worth seeing from another tab: is it
  // working, does it need me, or is it quiet. Same precedence the summary bar itself renders, so
  // the tab and the page can never disagree — and `attention` (not raw counts) is what decides
  // "needs me", so a single ancient aborted command does not mark the tab red forever
  // (ontwerp-v0.5.md item 5).
  function projectTitle(summary, project) {
    const s = summary || {};
    if (s.running) return `🟠 running · ${project}`;
    const attention = (s.attention || [])[0];
    if (attention) return `${statusIcon(attention.status) || "❗"} ${attention.status} · ${project}`;
    if (s.counts && s.counts.ready > 0) return `🟢 ready · ${project}`;
    return `✅ ${project}`;
  }

  // Display-only host rewrite (ontwerp-v0.5.md item 3): a service url whose host is loopback/any
  // (localhost, 127.0.0.1, ::1, 0.0.0.0) is useless when the dashboard is opened over Tailscale/LAN
  // — the link would point at the *browser's* own machine. Substitute the host you came in on
  // (window.location.hostname), keeping scheme/port/path byte-for-byte. Pure function: the hostname
  // is a parameter, defaulting to the current one, so any other host (or viewing from localhost
  // itself, where the substitution is a no-op) is returned unchanged. Never touches the server,
  // config, or mf-run — the configured value stays the link's title.
  const LOCAL_HOSTS = new Set(["localhost", "127.0.0.1", "0.0.0.0", "[::1]"]);
  function displayUrl(url, hostname) {
    if (!url) return url;
    hostname ??= (typeof location !== "undefined" ? location.hostname : "");
    let host;
    try { host = new URL(url).hostname; } catch { return url; }
    if (!LOCAL_HOSTS.has(host) || !hostname || hostname === host) return url;
    // Replace only the host authority (preserves scheme/port/path/trailing exactly).
    return url.replace("//" + host, "//" + hostname);
  }

  // The one shared live-updates helper (ontwerp-v0.5.md item 1). A page hands over the current
  // project and a list of {kind, refetch, guard?} handlers; this owns the EventSource, filters
  // events by project + kind, and calls the matching page-supplied refetch — pages refetch their own
  // JSON and re-render, never location.reload(). Robustness lives here, not in each page:
  //   - heartbeat watchdog: a {"kind":"ping"} frame every ~15s resets a dead-connection timer; if
  //     ~45s pass with no traffic while we believe we're connected, force a reconnect (the socket
  //     looked open but was dead — Tailscale/laptop-sleep);
  //   - exponential backoff on reconnect (1s→2s→…→20s);
  //   - polling fallback (~20s) while disconnected, so a broken SSE degrades instead of freezing;
  //   - refetch immediately on visibilitychange→visible, and force a reconnect if the socket looks
  //     dead;
  //   - a guarded handler skips its refetch while guard() is true (e.g. a dirty textarea / open
  //     modal) and calls onGuardedSkip instead, so live data never clobbers unsaved input.
  // Returns { refetch, close, onState(cb) }; badge is an optional element for the live marker.
  function live(opts) {
    const project = opts.project ?? null;
    const handlers = opts.handlers ?? [];
    const badge = opts.badge ?? null;
    const pollMs = opts.pollMs ?? 20000;
    const deadMs = opts.deadMs ?? 45000;
    const stateListeners = [];
    if (opts.onState) stateListeners.push(opts.onState);

    let source = null;
    let backoff = 1000;
    let lastTraffic = Date.now();
    let connected = false;
    let lastUpdated = null;
    let pollTimer = null;
    let reconnectTimer = null;
    let closed = false;

    if (badge) badge.innerHTML = '<span class="dot"></span><span class="label">connecting…</span>';

    function clock(ms) {
      try { return new Date(ms).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }); } catch { return ""; }
    }

    function render() {
      const polling = !connected && pollTimer !== null;
      const label = connected ? "live" : (polling ? "reconnecting…" : "connecting…");
      const suffix = lastUpdated ? " · " + clock(lastUpdated) : "";
      if (badge) {
        badge.classList.toggle("connected", connected);
        const l = badge.querySelector(".label");
        if (l) l.textContent = label + suffix;
      }
      const state = { connected, polling, label, lastUpdated };
      stateListeners.forEach((cb) => { try { cb(state); } catch { /* ignore */ } });
    }

    function markUpdated() { lastUpdated = Date.now(); render(); }

    // One refetch per handler at a time (docs/prompts/0010): a burst of events — a service writing
    // to its log fires "run" continuously — would otherwise stack refetches of a handler that is
    // still waiting on its previous one, and each of those can be a process spawn. Dropping the
    // extras loses nothing: the refetch already in flight reads the current state anyway.
    const inFlight = new Set();
    function runHandler(h) {
      if (h.guard && h.guard()) { if (h.onGuardedSkip) h.onGuardedSkip(); return; }
      if (inFlight.has(h)) return;
      inFlight.add(h);
      let pending;
      try { pending = Promise.resolve(h.refetch()); } catch { inFlight.delete(h); return; }
      pending.then(markUpdated, () => {}).finally(() => inFlight.delete(h));
    }

    function refetchAll() { handlers.forEach(runHandler); }

    function startPolling() {
      if (pollTimer !== null) return;
      pollTimer = setInterval(refetchAll, pollMs);
      render();
    }
    function stopPolling() {
      if (pollTimer === null) return;
      clearInterval(pollTimer);
      pollTimer = null;
    }

    function drop() {
      connected = false;
      if (source) { try { source.close(); } catch { /* ignore */ } source = null; }
    }

    function connect() {
      if (closed) return;
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
      if (source) { try { source.close(); } catch { /* ignore */ } }
      source = new EventSource("/api/events");
      source.onopen = () => {
        connected = true;
        backoff = 1000;
        lastTraffic = Date.now();
        stopPolling();
        render();
        refetchAll();
      };
      source.onmessage = (e) => {
        lastTraffic = Date.now();
        let evt;
        try { evt = JSON.parse(e.data); } catch { return; }
        if (evt.kind === "ping") return; // heartbeat: traffic only, no refetch
        if (project && evt.project && evt.project !== project) return;
        handlers.filter((h) => h.kind === evt.kind).forEach(runHandler);
      };
      source.onerror = () => {
        drop();
        startPolling();
        render();
        clearTimeout(reconnectTimer);
        reconnectTimer = setTimeout(connect, backoff);
        backoff = Math.min(backoff * 2, 20000);
      };
    }

    const watchdog = setInterval(() => {
      if (connected && Date.now() - lastTraffic > deadMs) {
        drop();
        startPolling();
        render();
        connect();
      }
    }, 5000);

    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState !== "visible") return;
      refetchAll();
      if (!connected || Date.now() - lastTraffic > deadMs) {
        backoff = 1000;
        connect();
      }
    });

    connect();

    return {
      refetch: refetchAll,
      onState(cb) { stateListeners.push(cb); },
      close() {
        closed = true;
        clearInterval(watchdog);
        stopPolling();
        clearTimeout(reconnectTimer);
        drop();
      },
    };
  }

  // Clickable breadcrumb (v0.2 "polish"): segments = [{label, href}], last one plain text.
  function renderBreadcrumb(el, segments) {
    el.innerHTML = segments.map((s, i) => {
      const isLast = i === segments.length - 1;
      return isLast || !s.href
        ? `<span class="crumb-current">${escapeHtml(s.label)}</span>`
        : `<a href="${s.href}">${escapeHtml(s.label)}</a>`;
    }).join(' <span class="crumb-sep">/</span> ');
  }

  let overviewPromise = null;
  // GET /api/overview — one request per page load for everything the shared chrome needs
  // (docs/prompts/0010): chat gating for the nav, the switcher's project names, and this project's
  // summary. It replaces the three separate calls (/api/config, /api/projects,
  // /api/projects/{name}) wireNav + summaryBar used to make between them. Memoized per page load,
  // so calling both helpers still costs exactly one request; live refreshes go to the fast
  // per-project endpoint, not back through here.
  function getOverview(project) {
    overviewPromise ??= api("/api/overview" + (project ? "?project=" + encodeURIComponent(project) : ""))
      .catch(() => ({ chatEnabled: false, projects: [], project: null }));
    return overviewPromise;
  }

  // Wires the standard project-scoped nav (Overview/Project/Specs/Config/Chat, minus whichever
  // page is currently open — see project.html for the canonical link set): every page just needs
  // to include the <a> ids it wants in its own <nav>, this fills in the hrefs and gates #chat-link
  // on chat.enabled so every page exposes the same menu "as far as accessible".
  function wireNav(project) {
    const hrefs = {
      "project-link": "project.html?p=",
      "specs-link": "specs.html?p=",
      "config-link": "config.html?p=",
      "scratchpad-link": "scratchpad.html?p=",
      "chat-link": "chat.html?p=",
    };
    Object.entries(hrefs).forEach(([id, base]) => {
      const el = document.getElementById(id);
      if (el) el.href = base + encodeURIComponent(project);
    });
    const chatLink = document.getElementById("chat-link");
    if (chatLink) getOverview(project).then((o) => { if (o.chatEnabled) chatLink.style.display = ""; });
  }

  // A short one-line summary for a JSON log line's <summary>, so the collapsed view still says
  // something useful (v0.2 "polish" — pretty-print JSON logs).
  function summarizeJson(obj) {
    if (obj && typeof obj === "object" && !Array.isArray(obj)) {
      const bits = [];
      if (obj.type) bits.push(obj.type);
      if (obj.subtype) bits.push(obj.subtype);
      if (bits.length) return bits.join(":");
    }
    const flat = JSON.stringify(obj);
    return flat.length > 90 ? flat.slice(0, 90) + "…" : flat;
  }

  // Detects single-JSON-object/array lines (as in claude.log's stream-json) and renders them
  // pretty-printed and collapsible; other lines render as plain text (v0.2 "polish"). The bounded
  // tail itself is unchanged — this only affects how the same lines are displayed.
  function renderLogLines(el, lines) {
    el.innerHTML = lines.map((line) => {
      const trimmed = line.trim();
      if (trimmed.startsWith("{") || trimmed.startsWith("[")) {
        try {
          const obj = JSON.parse(trimmed);
          const summary = escapeHtml(summarizeJson(obj));
          const pretty = escapeHtml(JSON.stringify(obj, null, 2));
          return `<details class="json-line"><summary>${summary}</summary><pre>${pretty}</pre></details>`;
        } catch { /* looked like JSON, wasn't — fall through to plain rendering */ }
      }
      return `<div class="log-line">${escapeHtml(line)}</div>`;
    }).join("") || '<span class="muted">(empty)</span>';
  }

  /// Manages one evidence log block: pretty (default, JSON collapsible) vs raw text, toggled by
  /// the caller-supplied button. update(lines) re-renders in whichever mode is currently active.
  function logView(el, toggleBtn) {
    let raw = false;
    function render(lines) {
      if (raw) el.textContent = lines.join("\n") || "(empty)";
      else renderLogLines(el, lines);
    }
    let lastLines = [];
    toggleBtn.addEventListener("click", () => {
      raw = !raw;
      toggleBtn.textContent = raw ? "Pretty" : "Raw";
      render(lastLines);
    });
    return { update(lines) { lastLines = lines; render(lines); } };
  }

  // Markdown link rewriting: cmd/pln/qa/rst bodies reference sibling lane files by their actual
  // on-disk name (e.g. the follow-up feature links back to "./0005-cmd-foo.md" and
  // "./0005-rst-foo.md") — correct for `cat`, but the cockpit never serves docs/prompts/*.md as
  // static content, only through command.html?id=. Rewrite any such link's href to the page that
  // actually renders it, after the markdown itself is rendered to HTML.
  const LANE_FILE_LINK = /^\.?\/?(\d{4}[B-Z]?)-(?:cmd|pln|qa|rst)-([a-z0-9-]+)\.md$/;
  function rewriteLaneLinks(container, project) {
    container.querySelectorAll("a[href]").forEach((a) => {
      const match = LANE_FILE_LINK.exec(a.getAttribute("href"));
      if (!match) return;
      a.href = `command.html?p=${encodeURIComponent(project)}&id=${encodeURIComponent(`${match[1]}-${match[2]}`)}`;
      // The markdown renderer treats every link as external (target=_blank); this one now points
      // inside the cockpit itself, so it should navigate like any other in-app link.
      a.removeAttribute("target");
      a.removeAttribute("rel");
    });
  }

  // A lane file's YAML frontmatter is metadata, not prose. Handing the whole file to the markdown
  // renderer turned it into a stray "--- title: … cmd: … done: … ---" paragraph on top of every
  // rendered report — the `---` fences read as horizontal rules and the keys as one run-on line.
  // Split it off so it can be rendered as what it is: a small key/value block.
  // Tolerant on purpose: no frontmatter at all (pln, qa) simply yields fields: null.
  const FRONTMATTER = /^\s*---[ \t]*\r?\n([\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)/;
  const FRONTMATTER_KEY = /^([A-Za-z_][\w-]*):[ \t]*(.*)$/;

  function splitFrontmatter(text) {
    const match = FRONTMATTER.exec(text || "");
    if (!match) return { fields: null, body: text || "" };

    const fields = [];
    for (const line of match[1].split(/\r?\n/)) {
      const kv = FRONTMATTER_KEY.exec(line);
      if (kv) {
        fields.push([kv[1], unquoteScalar(kv[2])]);
      } else if (fields.length && line.trim()) {
        // A block list under the previous key (`specs:` + `  - a`) — flattened onto one line
        // rather than modelled; nothing here needs the structure, only readability.
        const last = fields[fields.length - 1];
        const item = line.trim().replace(/^-[ \t]*/, "");
        last[1] = last[1] ? last[1] + ", " + item : item;
      }
    }
    return { fields, body: text.slice(match[0].length) };
  }

  // Only what a hand-written frontmatter value actually uses: surrounding quotes, and the escaped
  // quote inside a double-quoted scalar. Not a YAML parser — the server owns the real parsing.
  function unquoteScalar(value) {
    const v = (value || "").trim();
    if (v.length > 1 && v[0] === '"' && v.endsWith('"')) return v.slice(1, -1).replace(/\\"/g, '"').replace(/\\\\/g, "\\");
    if (v.length > 1 && v[0] === "'" && v.endsWith("'")) return v.slice(1, -1).replace(/''/g, "'");
    return v;
  }

  function frontmatterHtml(fields) {
    if (!fields || !fields.length) return "";
    return '<dl class="lane-frontmatter">' + fields
      .filter(([, value]) => value !== "")
      .map(([key, value]) => `<dt>${escapeHtml(key)}</dt><dd>${escapeHtml(value)}</dd>`)
      .join("") + "</dl>";
  }

  // Fire a primary action on Ctrl+Enter (Cmd+Enter on macOS) from inside a text input/textarea
  // (ontwerp-v0.5.md item 9). Put the hint in the button's title so it is discoverable. Native
  // <dialog> already closes on Esc, so no extra Esc handling is needed for the modals.
  function submitOnCtrlEnter(el, submit) {
    if (!el) return;
    el.addEventListener("keydown", (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
        e.preventDefault();
        submit();
      }
    });
  }

  // Status sound cues (ontwerp-sound-cues.md). Client-side only: no endpoint, no server change,
  // no new write action. Three parts live here — synthesis (chirp), the honest autoplay-aware
  // toggle, and a pure transition detector (detectCue / cueTracker) — plus one gesture listener.
  const sound = (() => {
    const KEY = "cockpit.sound"; // one localStorage key, per browser, default on
    let ctx = null;              // created lazily and only while enabled ("no AudioContext work" when off)
    const buttons = [];          // every rendered toggle, kept in step so all reflect one state

    function enabled() { try { return localStorage.getItem(KEY) !== "0"; } catch { return true; } }
    function setEnabled(on) { try { localStorage.setItem(KEY, on ? "1" : "0"); } catch { /* private mode */ } }

    // Lazy AudioContext, only ever touched while the toggle is on. Returns null when off/unsupported.
    function ensureCtx() {
      if (!enabled()) return null;
      if (!ctx) {
        const AC = window.AudioContext || window.webkitAudioContext;
        if (!AC) return null;
        ctx = new AC();
      }
      return ctx;
    }

    // Autoplay: browsers keep a fresh context suspended until the user interacts. "on" but not yet
    // resumed is a *pending* state (see buttonState) — never claimed as on while swallowing cues.
    function ready() { return !!ctx && ctx.state === "running"; }

    function prime() {
      if (!enabled()) return; // off → no context work at all
      const c = ensureCtx();
      if (c && c.state === "suspended") c.resume().then(refreshButtons, () => {});
      refreshButtons();
    }

    // One sine oscillator per note with a fast frequency glide, a short gain envelope (attack < 10ms),
    // a light detuned second oscillator for body and a touch of vibrato so it reads as a bird, not a
    // UI beep. Peak gain stays low (~0.15).
    function note(c, start, dur, f0, f1, peak) {
      const end = start + dur;
      const g = c.createGain();
      g.gain.setValueAtTime(0.0001, start);
      g.gain.linearRampToValueAtTime(peak, start + 0.008);
      g.gain.exponentialRampToValueAtTime(0.0001, end);
      g.connect(c.destination);

      const vib = c.createOscillator();
      vib.frequency.value = 26;
      const vibGain = c.createGain();
      vibGain.gain.value = f0 * 0.012;
      vib.connect(vibGain);

      [0, 9].forEach((detune, i) => {         // fundamental + a slightly detuned twin for body
        const osc = c.createOscillator();
        osc.type = "sine";
        osc.detune.value = detune;
        osc.frequency.setValueAtTime(f0, start);
        osc.frequency.exponentialRampToValueAtTime(f1, end);
        vibGain.connect(osc.frequency);
        const body = c.createGain();
        body.gain.value = i === 0 ? 1 : 0.4;
        osc.connect(body); body.connect(g);
        osc.start(start); osc.stop(end + 0.02);
      });
      vib.start(start); vib.stop(end + 0.02);
    }

    // Cockpit.chirp(kind): play one call now, if audio is enabled and activated. Silent (returns
    // false) when off or still pending — the toggle, not this, shows that state.
    function chirp(kind) {
      const c = ensureCtx();
      if (!c || c.state !== "running") return false;
      const t = c.currentTime + 0.02;
      const P = 0.15;
      if (kind === "done") {                    // rising two-note whistle, bright
        note(c, t, 0.12, 700, 1050, P);
        note(c, t + 0.11, 0.17, 1050, 1550, P);
      } else if (kind === "questions") {        // short repeated chirp, rising at the end
        note(c, t, 0.07, 980, 1080, P);
        note(c, t + 0.10, 0.07, 980, 1080, P);
        note(c, t + 0.20, 0.16, 1000, 1600, P);
      } else if (kind === "aborted") {          // descending two-note call, lower, darker
        note(c, t, 0.16, 520, 400, P);
        note(c, t + 0.15, 0.22, 420, 300, P);
      } else {
        return false;
      }
      return true;
    }

    function buttonState() {
      if (!enabled()) return "off";
      return ready() ? "on" : "pending";
    }
    function renderButton(btn) {
      const st = buttonState();
      btn.classList.toggle("off", st === "off");
      btn.classList.toggle("pending", st === "pending");
      btn.setAttribute("aria-pressed", String(st !== "off"));
      if (st === "off") { btn.textContent = "🔕"; btn.title = "Geluid uit — klik om aan te zetten"; }
      else if (st === "pending") { btn.textContent = "🔔"; btn.title = "Klik ergens op de pagina om geluid te activeren"; }
      else { btn.textContent = "🔔"; btn.title = "Geluid aan"; }
    }
    function refreshButtons() { buttons.forEach(renderButton); }

    function toggleButton() {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "sound-toggle";
      buttons.push(btn);
      btn.addEventListener("click", () => {
        const turningOn = !enabled();
        setEnabled(turningOn);
        if (turningOn) prime();       // this click is itself the activating gesture
        else refreshButtons();
      });
      renderButton(btn);
      return btn;
    }

    // Pure transition detector. prevMap: a `project + id → status` Map, or null before the first
    // render. items: [{project, id, status}]. Returns { cue, map }: at most one cue (priority
    // aborted > questions > done), null on the baseline render or when nothing transitioned. The
    // returned map records every current status (plus any vanished ids) so later changes still fire.
    const CUE_PRIO = { aborted: 3, questions: 2, done: 1 };
    function detectCue(prevMap, items) {
      const map = new Map();
      let cue = null;
      for (const it of items) {
        const key = it.project + " " + it.id;
        map.set(key, it.status);
        if (!prevMap) continue;                          // first render: fill baseline, stay silent
        const prev = prevMap.get(key);
        if (prev === undefined || prev === it.status) continue; // new id, or unchanged → no cue
        const prio = CUE_PRIO[it.status];
        if (prio && (!cue || prio > CUE_PRIO[cue])) cue = it.status;
      }
      if (prevMap) for (const [k, v] of prevMap) if (!map.has(k)) map.set(k, v);
      return { cue, map };
    }
    // Stateful wrapper: holds the baseline across refetches. observe(items) → cue string or null,
    // and plays it. First call after page load is silent (fills the baseline).
    function cueTracker() {
      let map = null;
      return {
        observe(items) {
          const r = detectCue(map, items);
          map = r.map;
          if (r.cue) chirp(r.cue);
          return r.cue;
        },
      };
    }

    ["click", "keydown"].forEach((ev) => document.addEventListener(ev, prime));

    return { chirp, toggleButton, cueTracker, detectCue, enabled };
  })();

  // A compact sticky "is anything happening here?" bar under the header (ontwerp-v0.5.md item 2),
  // shared by every project-scoped page. One muted row, no card chrome. Everything in it comes from
  // the fast, file-I/O-only project summary. It deliberately carries NO branch or run chip
  // (docs/prompts/0010): those cost a git subprocess and an mf-run spawn, and paying for them on
  // pages that show neither — specs, scratchpad, config, command — was most of what a page load
  // cost. project.html has a full Git card and Run card and keeps both.
  // Returns { refresh, marker } — pass marker as Cockpit.live's badge so the live/reconnecting
  // indicator lives in the bar, and call refresh from the page's live handlers. The switcher jumps
  // between projects and back to the index.
  const STATUS_ORDER = ["running", "questions", "aborted", "ready", "draft", "done"];
  // onSummary, when given, is called with every rendered project summary — project.html uses it to
  // keep its tab title in step with the lane without fetching the summary a second time.
  function summaryBar(project, onSummary) {
    const bar = document.createElement("div");
    bar.className = "summary-bar";
    bar.innerHTML = `
      <select class="sb-switcher" title="Switch project">
        <option value="">Overview…</option>
      </select>
      <span class="sb-lane"></span>
      <span class="sb-counts"></span>
      <span class="sb-spacer"></span>
      <span class="sb-watch muted"></span>
      <span class="sb-live badge-live"><span class="dot"></span><span class="label">connecting…</span></span>`;
    const header = document.querySelector("header.cockpit-header");
    header.insertAdjacentElement("afterend", bar);

    // Sound toggle (ontwerp-sound-cues.md): same place on every project-scoped page. Only index.html
    // and project.html actually fetch lane data and chime; the others just carry the toggle.
    bar.querySelector(".sb-live").insertAdjacentElement("beforebegin", sound.toggleButton());

    const laneEl = bar.querySelector(".sb-lane");
    const countsEl = bar.querySelector(".sb-counts");
    const watchEl = bar.querySelector(".sb-watch");
    const switcher = bar.querySelector(".sb-switcher");

    switcher.addEventListener("change", () => {
      location.href = switcher.value ? "project.html?p=" + encodeURIComponent(switcher.value) : "index.html";
    });

    function cmdLink(c) {
      return `<a href="command.html?p=${encodeURIComponent(project)}&id=${encodeURIComponent(c.id)}">${escapeHtml(c.id)}</a>`;
    }
    function shortTitle(t) { return t && t.length > 28 ? t.slice(0, 27) + "…" : (t || ""); }

    function renderSummary(s) {
      if (s.running) {
        laneEl.innerHTML = `<span class="sb-dot running"></span> ${cmdLink(s.running)} <span class="muted">${escapeHtml(shortTitle(s.running.title))}</span>`;
      } else if (s.latest) {
        laneEl.innerHTML = `<span class="muted">idle · latest</span> ${cmdLink(s.latest)} ${statusPill(s.latest.status)}`;
      } else {
        laneEl.innerHTML = '<span class="muted">idle · no commands</span>';
      }

      const counts = s.counts || {};
      countsEl.innerHTML = STATUS_ORDER
        .filter((k) => counts[k] > 0)
        .map((k) => `<span class="sb-count ${k}">${counts[k]} ${k}</span>`)
        .join(" ") || '<span class="muted">—</span>';

      watchEl.innerHTML = s.watcherAlive
        ? '<span class="sb-dot alive"></span> watcher'
        : '<span class="muted">watcher off</span>';

      if (onSummary) { try { onSummary(s); } catch { /* a title is never worth breaking the bar */ } }
    }

    // Live refreshes go straight to the fast per-project summary — the shared /api/overview is a
    // page-load thing, memoized, and re-requesting it would only re-send the switcher's names.
    async function refresh() {
      try { renderSummary(await api(`/api/projects/${encodeURIComponent(project)}`)); } catch { /* keep last */ }
    }

    // The bar's whole first render — switcher options and summary — out of the one chrome request.
    getOverview(project).then((o) => {
      switcher.innerHTML = '<option value="">Overview…</option>' + (o.projects || []).map((name) =>
        `<option value="${escapeHtml(name)}"${name === project ? " selected" : ""}>${escapeHtml(name)}</option>`).join("");
      if (o.project) renderSummary(o.project);
    }).catch(() => {});

    return {
      marker: bar.querySelector(".sb-live"),
      refresh,
    };
  }

  return {
    qs, api, escapeHtml, statusPill, formatDate, displayUrl, live, summaryBar, submitOnCtrlEnter, renderBreadcrumb,
    getOverview, wireNav, logView, rewriteLaneLinks, splitFrontmatter, frontmatterHtml,
    statusIcon, projectTitle,
    chirp: sound.chirp, cueTracker: sound.cueTracker, detectCue: sound.detectCue, soundToggle: sound.toggleButton,
  };
})();
