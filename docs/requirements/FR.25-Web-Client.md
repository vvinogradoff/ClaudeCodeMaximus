> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.25 — Web Client (Tessyn Web)

On 2026-11-12 the Architect asked for a way to work with Tessyn from a phone. The Telegram bridge (FR.21) lets him chat with a session, but it has no Focus view (FR.23) and cannot create sessions, and a chat is a poor place to browse a tree of about 2,700 sessions. He mostly holds the phone in portrait. An iOS app may follow later and must not need a second API. So Tessyn gets a **web client**, **Tessyn Web**: a mobile-first web app at `https://tessyn.vvinogradoff.com` that drives the daemon inside TessynDesktop remotely. It has the sessions panel (Tree, Recent, Tags), the Session and Focus views, per-session settings, the usage strip and live updates. It leaves out everything that only makes sense at the desk: no Add directory, no context menus, no instruction toggles, no MD/JSONL switch, no in-session search, no profile actions, no browser control, no completion sound.

Telegram stays as it is (long polling, FR.21). The Telegram bridge, the scheduler, orchestration and the web client all start turns through the same headless path (FR.25.33), so they behave alike.

The remote API behind the web client is documented in `docs/refactoring/tdcp-spec.md` §8, and operations in `docs/features/web-client.md`. Use cases: `UC.WEB.01`. Test cases: `TC.WEB.01`.

#### Topology, components and security

**FR.25.01 — Topology (the S3 topology of `tdcp-spec.md` §3, now built):**
- The PC opens **no inbound port**. The daemon inside TessynDesktop **dials out** over HTTPS to the web server (**Web Relay**) and keeps one long-lived connection, the **Daemon Link**.
- The web server holds no Tessyn state. It authenticates the browser, forwards allow-listed JSON-RPC requests to the daemon over the Daemon Link, relays the daemon's replies and notifications back to the browser, and serves the single-page app. The daemon owns all state: tree, session files, drafts, attention ledgers, tags, settings.
- The browser talks to the server only: `POST /api/rpc` for requests and `GET /api/events` (server-sent events) for notifications (FR.25.31, FR.25.32).
- While the PC is off, TessynDesktop is closed, or web access is off, the site still loads and shows **Desktop offline** (FR.25.29).

**FR.25.02 — Components:**

| Component | Project | Where it runs |
|---|---|---|
| Daemon, Remote API, Relay Connector | `TessynDesktop.Core` (inside TessynDesktop) | the Architect's Windows PC |
| Web Relay and single-page app host | `code/Tessyn.Web` (ASP.NET Core, `net10.0`, self-contained `linux-x64`) | server `safe`, behind nginx, loopback port 5320 |
| Web app (React, TypeScript, Vite, installable as a PWA) | `code/Tessyn.Web/ClientApp`, built into `wwwroot` | the Architect's phone (or any browser) |

Server tests are in `code/Tessyn.Web.Tests`; end-to-end tests (real server, real Relay Connector, real Core daemon with a fake `claude` process, plus a Playwright full-stack flow) are in `code/Tessyn.Web.E2E.Tests`. The web app has Vitest unit tests and Playwright flows (mock build, and a real-server login/logout flow) under `ClientApp`.

**FR.25.03 — Authentication:**
- **Single user.** The only account is the one in the server config (`TessynWeb:Auth:Email`, `TessynWeb:Auth:PasswordHash`). There is no registration, no password reset by mail and no second user.
- **Login.** `POST /api/auth/login { email, password }` returns 200 and sets the cookie, 401 for a wrong email or password (the same message for both), 429 when throttled (FR.25.04). `POST /api/auth/logout` clears the cookie. `GET /api/auth/me` returns `{ email }` or 401.
- **Password storage.** The config holds a PBKDF2 hash, never the password: `pbkdf2-sha256$<iterations>$<base64 salt>$<base64 hash>` with 600,000 iterations, a 16-byte salt and a 32-byte hash. The comparison is constant-time. The command `Tessyn.Web hash-password` reads a password from standard input and prints a hash to paste into the config. The Architect changes the password by running it again (see the operations guide).
- **Cookie (amended 2026-11-13).** `__Host-Tessyn.Auth` (the `__Host-` prefix: Secure, Path=/, no Domain, so it is bound to the exact host): HttpOnly, Secure, SameSite=Strict. It **slides with a 14-day idle limit** (`Auth:CookieDays`, default 14) and has an **absolute lifetime of 30 days** from login (`Auth:AbsoluteDays`, default 30) that sliding never extends. The Data Protection keys are stored on disk (`TessynWeb:DataProtectionKeysPath`) so cookies survive a server restart; the same keys protect the trusted-device cookie.
- **Revocation: security stamp and logout everywhere (amended 2026-11-13).** Each ticket carries a **security stamp** = SHA-256(password hash + a server-side random **auth epoch**, kept in a file `auth-epoch` beside the keys directory, override `Auth:EpochPath`) and its absolute expiry. Every request validates both; a mismatch rejects the cookie and signs it out. **Logout bumps the epoch, so it logs out every device** (not only the browser that pressed it); only an authenticated caller can bump it (an anonymous logout does nothing). **Changing the password invalidates every cookie** (the hash is part of the stamp). Cookies survive a server restart (shared keys and epoch) but not a password change.
- **Trusted device (amended 2026-11-13).** Every successful login also sets `__Host-Tessyn.Device`: a Data-Protection-protected token bound to the current password stamp, 180 days, HttpOnly, Secure, SameSite=Strict. A request that carries a valid one is exempt from the **global** failure limit of FR.25.04 (so a stranger's bad guesses cannot lock the owner out); the per-IP limit still applies to it. A forged cookie, or one from before a password change, gives no exemption.
- API calls without a valid cookie get 401 (no redirect). The server refuses to start when the password hash or the link token hash (FR.25.06) is missing.

**FR.25.04 — Login throttling:** 5 failed logins per 15 minutes per client IP and 20 failed logins per hour for the whole server (the global limit does not apply to a trusted device, FR.25.03). Beyond either limit the server answers 429 with `Retry-After` and does not check the password. The client IP comes from the forwarded headers, which the server accepts from 127.0.0.1 only, with `ForwardLimit = 1`; nginx **overwrites** `X-Forwarded-For` with `$remote_addr`, so a client cannot choose its throttle key.
- **Atomic (amended 2026-11-13).** The attempt is recorded **before** the password is checked and removed again on success, so parallel guesses cannot all pass the check (40 parallel wrong guesses give exactly 5 × 401 and 35 × 429; a successful login does not use up the budget).
- **Bounded.** A blocked request creates no entry; empty buckets are swept every minute; IPv6 addresses are keyed by their /64 (an IPv4-mapped IPv6 address maps to IPv4).

**FR.25.05 — Request protection:**
- **CSRF.** Every `POST /api/*` must carry the header `X-Tessyn-Client: web`, otherwise 400. Together with SameSite=Strict, and with no CORS headers, a foreign site cannot make a request that works.
- **Headers (amended 2026-11-13).** Content-Security-Policy `default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'none'; object-src 'none'` (no inline script), `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`; HSTS from nginx. `Cache-Control: no-store` on `/api/*`, `/link/*` and `/healthz`.
- **Request body limits (amended 2026-11-13).** Enforced before the body is parsed (413 for an oversize `Content-Length`; chunked bodies are cut by the Kestrel feature): login 4 KB, `/api/rpc` 1 MB, `/link/reply` and `/link/events` 4 MB, Kestrel global 4 MB; nginx `client_max_body_size 4M`. `limit_req` in nginx is **not** set (it needs an `http`-level zone in the shared nginx config; follow-up in the todo).
- **Rendered markdown (amended 2026-11-13).** Model output is rendered through DOMPurify with `form`, `input`, `textarea`, `select`, `button`, `style` and `iframe` forbidden and the `style` and `formaction` attributes removed, so injected markdown cannot draw a fake form or overlay. The code-block copy button is added after sanitising. Tag links are opened only when they are `https:` URLs; any other tag URL renders as plain text.
- **Client persistence.** A successful logout clears the saved drafts and reply texts (`tessyn.draft.*`, `tessyn.reply.*`) from the browser's local storage. A failed logout shows an error toast and leaves the user signed in.
- **Logging.** At Information level the server and the connector log no passwords, tokens, prompts, session content or RPC payloads.

**FR.25.06 — Link token and allowlists:**
- **Link token.** The Daemon Link is authenticated with a **Link Token**: 32 random bytes, base64url, separate from the password. The server stores only its SHA-256 (`TessynWeb:Link:TokenSha256`, hex) and compares in constant time. The desktop stores the token DPAPI-protected (FR.25.36). `Tessyn.Web new-link-token` prints a new token and its hash. A wrong token gets 401.
- **Token hash (amended 2026-11-13).** The hash is the SHA-256 of the UTF-8 bytes of the token string exactly as sent in the `Bearer` header (the base64url text), lowercase hex.
- **Allowlist on the server.** The server forwards only the 21 methods of FR.25.31; any other method gets JSON-RPC error `-32601` without leaving the server.
- **Allowlist on the daemon.** The daemon's dispatcher accepts on the relay transport only methods flagged **remote-callable**. Everything else, including the loopback-only TDCP methods, gets `-32601` there as well. Each side enforces its list on its own (defence in depth).

**FR.25.07 — What crosses the wire:** Tree nodes are addressed by a 32-hex-digit `id` (FR.1.17). Session ids are the existing `NodeId`. No filesystem path, `AgentToken`, `ClaudeSessionId`, `TdcpToken`, profile config directory, Telegram token or file name is ever sent to the server or the browser. Directories are shown by their Auto-derived label (FR.1.3). JSON is camelCase, enums are camelCase strings, nulls are omitted, times are UTC ISO-8601.

#### Layout

**FR.25.08 — Layout and navigation:**
- **Portrait first.** The page fills the screen height (`100dvh`), respects the safe-area insets, uses at least 16 px input fonts (no zoom on iOS) and keeps working on desktop widths.
- **Top bar.** ☰ (sessions panel), the session title (tapping it opens the panel), a link-status dot (green online, grey offline), ⚙ (Settings sheet, FR.25.23, only with a session open) and ⋯ (Compact session, FR.25.26; Log out).
- **Sessions panel.** Collapsed by default. Expanded, it covers the full width under the top bar and has three tabs, **Tree**, **Recent** and **Tags**. Choosing a session opens it and collapses the panel. The last used tab is remembered.
- **Session view** below the top bar (FR.25.16), the **usage strip** at the bottom (FR.25.27). With no session open the page shows a short empty state with a button that opens the panel.
- The open session is part of the address (`#/s/<nodeId>`), so the phone's back gesture works.
- Light and dark themes follow the phone's setting.
- **Login page.** Email and password, the error text, and for 429 the time to wait.

#### Tree tab

**FR.25.09 — Tree rows:** Rows follow the desktop's (FR.1.1–FR.1.8, FR.9.5, FR.9.7, FR.23.17):
- **Directory:** 📂 and the label, a thick rule above. **Group:** the name, a thin rule above.
- **Session, line 1:** the status block (8×16, the top half dark red while `running`, FR.1.8), the attention pill (count and colours as the desktop's session-row badge, FR.23.17), the name, ✏ when the session has a draft, a gold ★ on the right for a favourite. **Line 2** (small, grey): the profile name of the last USER entry on the left, the last prompt time `yyyy-MM-dd HH:mm` (local) on the right.
- **Background** by the recency of Last Activity against the server time: up to 15 minutes strong green, up to 60 lighter, up to 240 faint, up to 720 fainter; a dotted green border when active within 24 hours. The colours are translucent so they work in the dark theme.
- The **selected** row is highlighted. Selection is client state; it reaches the daemon only as `scopeNodeId` (FR.25.11, FR.25.15) or `anchorId` (FR.25.12).

**FR.25.10 — Tree structure, expansion and Worker Sessions:**
- The web shows the daemon's order (groups first, then sessions, as in the model). Only top-level sessions are listed; a supervisor shows `N workers` and a chevron. Expanding it loads its workers (`tree.workers`) and nests them under it in the same row style; an orphan worker (supervisor not found) is listed at top level, as on the desktop.
- **Expansion is owned by the client.** Tapping a directory or group row selects it and toggles it; the state is kept in the browser (localStorage) and starts from the desktop's saved `isExpanded` flag the first time a node is seen. The web never changes the desktop's expansion state, and the desktop never reads the web's.
- The tree is fetched with `tree.snapshot`. The snapshot carries a version; the daemon answers `unchanged` when a client sends the current `sinceVersion`; the web app as built always fetches a full snapshot (after `tree.changed` and after a resync), because `unchanged` would leave running and attention flags stale after an offline gap.

**FR.25.11 — Tree search (same semantics as FR.1.9–FR.1.14, plus ancestor expansion):**
- **Box.** A search box with a 300 ms debounce and a spinner while a search runs (FR.1.11), a clear ×, the **Exact** toggle and the **Project** toggle.
- **Matching.** The query is matched against message content with the Cross-Project Search Index, trimmed; a blank query clears the search. Typed text is always a literal query. A top-level session matches when it or any of its workers matches; results are ranked as FR.1.12 (strict matches first, then loose, then the last prompt time). If the index cannot be opened, the daemon falls back to the file scan and reports `usedFallback`.
- **Exact toggle (FR.1.14).** Switches on automatically when the query starts with `https://` or is shorter than 8 characters; if the user unticks it, that holds until the text changes; otherwise the last manual choice persists. Exact keeps strict matches only.
- **Project toggle (FR.1.10).** Enabled only while a session is selected, on by default when one gets selected, forced off and disabled when none is. It limits results to the project (Claude CLI project slug) of the selected session; the web sends the selected session as `scopeNodeId`. Changing it, or selecting another session while it is on, re-runs the search.
- **Result view.** Only matching sessions, their ancestors and, for a supervisor with matching workers, those workers (nested) are shown; non-matching sessions and workers are hidden. **Ancestors of matches are expanded**, whatever the user's expansion state, so that every match is visible without tapping. The stored expansion state is not changed. Directories, groups and sessions are ordered by match rank; a container ranks by its best descendant. Clearing the search restores the normal view.
- **Live.** A running search is re-run on the `index.changed` notification (FR.1.13).

**FR.25.12 — Create session:** The Tree tab has a **Create session** button where the desktop has Add directory. It is enabled when a directory, group or session is selected. It asks for a name in a small dialog (required, trimmed) and calls `tree.createSession`:
- **A session is selected:** the new session is inserted **right after it** in the same parent (a worker as the anchor resolves to its top-level supervisor).
- **A group or directory is selected:** the new session is inserted **first** inside it. *(Amended by FR.1.30 on 2026-11-18: it goes **last** inside it.)*
- The new session gets the same defaults as a session created on the desktop. The desktop's tree shows it at the same place at once (the registry raises one event carrying the parent and the preceding session; an unknown anchor creates nothing). **Desktop insert position rule (decided 2026-11-13):** the desktop does not use a literal `groupCount + index`, because a model's `Sessions` index also counts worker sessions while the tree rows do not (workers are nested under their supervisors). It inserts the row right after the row of the **preceding model session**, or first among the sessions (after the groups) when there is none. A session created by an agent (`spawn_session`) carries no index and is still appended. After creation the web opens the new session with the message box focused.

**FR.25.13 — Not in the Tree tab:** no Add directory, no Add group, no context menus (rename, delete, move, import, schedules, etc.), no drag and drop. Those stay desktop features.

#### Recent and Tags tabs

**FR.25.14 — Recent tab:** It lists what the desktop's top-bar **Recent dropdown** lists (FR.9.6–FR.9.8), as one flat list (not the grouped Recent tab of FR.1.16):
- **Content:** top-level sessions that are Pinned Sessions (favourite, at least one open Focus item of any kind including `Decided`, or at least one schedule) plus sessions active within the last 24 hours that are not hidden (`RecentIgnoredAt`).
- **Order:** by Last Activity, newest first, sessions without activity last.
- **Row:** status block, attention pill, name, directory label (small), profile name, 🕒 when the session has schedules, then the ★ for a favourite, or a **hide ✕** when the row can be hidden. Pinned sessions have no hide button. Hide calls `recent.hide`; the list refreshes. **Desktop refresh (amended 2026-11-13, after the first build):** the hide is persisted and shows in `recent.list` at once. The desktop's session node keeps its own copy of `RecentIgnoredAt`; `recent.hide` therefore raises `SessionSettingsChanged`, and the tree view model refreshes the node's favourite flag and `RecentIgnoredAt` from the model (`RefreshSettingsFromModel`, on the UI thread), so the desktop's Recent dropdown follows without a restart. (The first build only updated it after a restart.) A Pinned Session answers `{ hidden: false }`.
- Tapping a row opens the session. The list is fetched when the tab is shown and refreshed (debounced) on session and attention changes.

**FR.25.15 — Tags tab (FR.24.11–FR.24.13 as a list):**
- **Search box** with a 150 ms debounce and the 📁 scope toggle, which follows the Project toggle rules of FR.25.11 but is independent of it. Scope = the Directory node of the selected session.
- **Chips** (wrapping): selected tags first, then the matches, in the order of FR.24.11; a chip opens its URL in a new tab on long-press or a small ↗ (only URLs allowed by `SessionTagUrlPolicy`). Tapping a chip toggles its selection. The selection is client state, survives changes to the search text, tab switches and scope changes, and is not persisted.
- **Session list** under the chips: the top-level sessions carrying at least one selected tag (OR; a worker's tags count for its supervisor), grouped by directory with collapsible headers (collapsed directories are remembered), drafts first then newest first, rows as in the Recent tab.
- **Empty texts:** `No tags in this directory`, `No tags yet`, `No matching tags`, `Select tags to list their sessions`, `No sessions with the selected tags`.
- Calls `tags.search` with the text, the scope and the selected tags; refreshes on `tags.changed`.

#### Session view

**FR.25.16 — Session view header and tabs:**
- **Header:** a small location line (`directory label / tree path`), the session name in semi-bold, and below it the Session Tag chips when the session has tags (FR.24.7; tapping a chip opens its URL in a new tab).
- **Switch:** a segmented control **Session | Focus** (FR.23.12). The **Focus badge** (open items except `Decided`, coloured by the top kind, FR.23.13) shows next to it while Session is selected and the count is above 0. A newly opened session starts on Session.
- There is **no MD/JSONL switch** and **no in-session search**.

**FR.25.17 — Transcript rendering:** The Session tab shows the transcript, newest at the bottom, built from `session.entries`:
- **USER:** a right-aligned bubble with plain text (line breaks kept), "You" and the profile name, the local time (date and time, FR.2.3.1).
- **ASSISTANT:** full width, rendered as sanitised markdown (headings, lists, tables that scroll sideways, fenced code in monospace blocks that scroll sideways and have a copy button, links that open in a new tab with `rel="noopener noreferrer"`), the time (`HH:mm`) and, when stamped, the italic label `[model, effort]` (FR.2.3.5).
- **SYSTEM:** a small grey italic centred box.
- **COMPACTION:** a separator `― Context compacted above ―`.
- Entries whose content is empty or whitespace are hidden, except COMPACTION.
- **Scrolling:** the view follows new entries while the user is at the bottom; otherwise a **↓ New messages** pill appears.

**FR.25.18 — Paging, truncation and epochs:**
- The view loads the last 50 entries; **Load earlier** at the top loads the previous 50 (`beforeIndex`). New entries are fetched with `afterIndex = last index` when `session.appended` arrives.
- An entry is addressed by its index in the parsed entry list (all entries, empty ones included, FR.3.3 amended). The daemon cuts an entry longer than 32,768 characters and marks it `truncated` with its `fullLength`; the client shows **Show full message**, which fetches the entry with `session.entry`.
- **Epoch (Session Epoch).** Each session file has an in-memory counter that starts at 0 each time TessynDesktop starts and goes up by 1 whenever the file is rewritten (compaction, repair; FR.25.34). Every entries response and every `session.appended` / `session.reset` carries it. When the client sees a different epoch than the one it holds, or receives `session.reset`, it discards what it has and reloads the last 50 entries; `session.entry` for an index with an outdated epoch fails with "Session history changed".

**FR.25.19 — Pending bubbles, thinking row and running state:**
- **Pending Bubble.** A message sent from the web appears immediately at the bottom as a dimmed USER bubble, marked **queued** when the daemon reported `queued`. It is replaced when a USER entry with the same text arrives, and removed (with an error note) when the send fails.
- **Thinking row.** While the session's `running` flag is set, the bottom of the transcript shows `Claude is thinking… m:ss`, counting from when the client first saw the flag.
- `running` is the reference-counted flag of FR.1.8 (amended), so it is also set while a turn started on the desktop, by Telegram, by the scheduler or by an orchestrator is running.

**FR.25.20 — Message input (Session tab only):**
- A multi-line box that grows up to about 40% of the viewport, placeholder `Message…`, a **Send** button. Enter inserts a new line; Ctrl+Enter or Cmd+Enter sends.
- **Send stays enabled while a turn is running.** The daemon queues the message behind the running turn, as the desktop does (FR.14.9); the pending bubble shows **queued**. **`queued` (amended 2026-11-13)** is true when the session's running flag is set **or** another web turn has been accepted and has not finished yet (so a second send before the first one took the turn lock is also `queued`).
- **Stop** is visible while the session is running (`session.stop`): it kills the Persistent Session Process if one is alive (FR.19.5) and cancels a running headless turn. **Limits (amended 2026-11-13):** it cannot cancel a turn started in the desktop's own session view as a one-shot process (its cancellation lives in the desktop's `TurnController`); it does not cancel a compaction (FR.25.26).
- The draft is kept per session in the browser. The input is disabled, with the banner **Desktop offline**, while the link is down (FR.25.29). The Focus tab has no message box of its own.
- Each send carries the session's current model, effort and profile choice (FR.25.24). The web adds no instruction blocks (commit, branch, document); there are no instruction toggles.

#### Focus tab

**FR.25.21 — Focus tab layout (FR.23.14):** The tab shows `attention.list` for the open session:
- Open non-`Decided` cards first (order of FR.23.2), then a **Decisions (n)** expander and a **Closed (n)** expander, both collapsed by default; Closed collapses again each time Focus is selected.
- **Card:** kind chip (colours of FR.23.1), the id (`A12`), the bold title, the time; the markdown body; one button per option; **Use recommendation: …** when there is a recommendation; a reply box `Reply...`; **Reply**, enabled when the box has text; the close button far right.
- **Closed cards** are compact with an outcome line (`Answered: …`, `Resolved: …` or `Dismissed`) and expand on tap.
- **Empty:** `Nothing needs your attention in this session.`
- The list is refreshed on `attention.changed` for the open session; texts typed into reply boxes survive the refresh. The badge of FR.25.16 follows the same notifications.

**FR.25.22 — Focus actions (FR.23.15):**
- **Option button:** puts the option text in that card's reply box. **Use recommendation:** puts `Accepted your recommendation: <text>` there.
- **Reply:** `attention.reply` for that one card. **Send answers (n)** appears when at least one card has text and sends all of them in one `attention.reply`. The daemon behaves exactly like the desktop (FR.23.15): it resolves the titles itself, marks the items answered first, builds the answers prompt and starts the turn through the web turn path (FR.25.33). Only open items count; the response lists `answeredIds` and `skippedIds`.
- **Close button:** **Dismiss** for `ask` and `todo`, **Got it** for `critical`, `alert` and `decided`; both call `attention.dismiss` (status Dismissed, FR.23.2).
- The model, effort and profile of the session's settings are sent with a reply, as with a normal send.

#### Settings, Compact and usage

**FR.25.23 — Settings sheet:** The ⚙ button opens a bottom sheet with: the **Favourite** toggle (`session.setFavorite`, FR.9.7), the **Model** select, the **Profile** select, the **Effort** select (Default, Max, High, Medium, Low) and the **Streaming** toggle. The choices for model and profile come from `catalog.get`: the Default entry first, then the desktop's model list and profiles (display names only). There are no profile actions (re-authenticate, rename, remove), no browser-control toggle, no sound settings and no instruction toggles.

**FR.25.24 — Per-session choices and locking (FR.12.3, FR.12.6, FR.12.12, FR.19.8):**
- Model, profile and effort are client state **per session**. When a session is first opened in a browser they are initialised from `session.get.lastUsed`, which the daemon builds exactly as the desktop restores them: model and effort from the last ASSISTANT entry that carries them, the profile from the last USER entry's `profile=` (a display name mapped to a profile; unknown or `[Sys] …` names mean Default); a session without such entries starts with Default for all three. **Default profile label (decided 2026-11-13):** the Default profile is shown and stamped as `[Sys] <email>` once the desktop has learned the account email (a `claude auth status` run), and as `[Sys] Default` until then, in `catalog.get`, `session.get.lastUsed.profileName` and every USER stamp, so a stamp and a catalog entry never differ. The first call that needs the label waits for it for at most 5 s; it is cached afterwards (10 min, a failure 1 min). A pinned profile that has been deleted falls back to Default. Later choices are kept in the browser and are sent with every send, reply and compact of that session.
- Model, Profile and Effort are **disabled while the session's Persistent Session Process is alive** (`streamingAlive`), since they are spawn-time arguments (FR.19.8); they unlock when the process ends.

**FR.25.25 — Streaming toggle (FR.19.6, FR.19.7):** `session.setStreaming` changes the persistent per-session setting. Turning it **off** while a process is alive does nothing and returns `needsConfirm`; the web then asks **Stop the running Claude process for this session?**, and on Yes repeats the call with `force`, which stops the process. Turning it on never needs a confirmation. The desktop's session view refreshes when either setting changes (event `SessionSettingsChanged`: the tree row star, the command-bar star, the 📡 toggle and the Stop button follow). A `needsConfirm` answer returns `enabled` = the current value and raises no event.

**FR.25.26 — Compact session (FR.11.6):** ⋯ → **Compact session…** asks `Compact this session? The transcript is replaced by a summary.` and calls `session.compact` with the session's model, effort and profile choice. The daemon runs the same Core compaction service as the desktop's Auto-Compact (FR.11.6 amended): it takes the per-node turn lock, sets the running flag, and on success rewrites the file, which raises `session.reset` (FR.25.18). **Outcome entries (amended 2026-11-13):** for the **Nothing to compact** and **Failed** outcomes the remote module **appends** a SYSTEM entry (append only, history untouched) and publishes `turn.completed` so the web sees the result; after a success it also publishes `turn.completed`. The desktop's own compaction path writes these lines in memory only, as before. A failed or empty answer, a throwing process or a failing rewrite leaves the file and the Claude session id untouched. `session.compact` resolves model, effort and profile through the same `ITurnOptionsResolver` as `session.send` (model: node, directory, app; `default` means none; profile: resolved name, then config directory; an unknown model, an effort outside the list or an unknown profile is `-32602`); `queued` = the session was running at accept. Compact is rarely used and has no other entry point in the web.

**FR.25.27 — Usage strip (FR.18):** A strip about 28 px high at the bottom of the page shows two thin grey bars, `5h: 37%  resets 14:20` and `7d: 12%  resets Mon`, for the **profile of the open session** (`usage.get { profileId }`). The windows and labels follow FR.18.3; a stale window shows no "resets" part. The strip is hidden when no session is open or when the state is not `ok` (`pending`, `unauthorized`, `unavailable`). It is refreshed when the session or its profile changes, every 5 minutes while the page is visible, and on `usage.changed`. The daemon fetches usage per profile with a 5-minute cache and does not change the desktop's active profile or its status bar. **Behaviour as built (2026-11-13):** `usage.get` without a `profileId` means `"default"`. A fetch that takes longer than 8 s answers `pending` and carries on in the background; `usage.changed` follows when it finishes. A failed fetch is retried after 1 minute, an unauthorized one after 5 minutes; `unauthorized` with an older reading still returns that reading as `ok`. `usage.changed` is also published when the desktop's own usage poller reports (it invalidates the cache entry of the active profile).

#### Live updates, offline and PWA

**FR.25.28 — Live updates:** The browser opens one SSE stream (`GET /api/events`). The daemon's notifications (FR.25.32) arrive on it with a server sequence id. The server keeps a ring buffer of the last 1,000 events or 10 minutes; a reconnecting browser sends `Last-Event-ID` and gets what it missed, or a `resync` event when its id has fallen out of the buffer. The first event after connecting is `link.status`. The server sends a comment ping every 15 seconds. Each notification triggers only the refetch that it names, for the data currently on screen.

**FR.25.29 — Offline banner and resync:**
- When the Daemon Link is down, the server answers requests with error `-32001` (daemon offline) and publishes `link.status { online: false }`. The page shows **Desktop offline** and disables sending. A request that exceeds its timeout (30 s; 60 s for entries, snapshot and searches) fails with `-32002`.
- When the link is up again, or the server sends `resync`, or the stream was closed or silent for more than 30 seconds when the page becomes visible again (iOS ends background connections), the client **resyncs**: it refetches the tree (a **full** `tree.snapshot` as built; passing `sinceVersion` would leave running and attention flags stale after an offline gap), the open session (`session.get`, then the entries after the last index, or a reload when the epoch changed), the open session's Focus items and the usage strip.
- Notifications produced while the link is down are dropped; the resync covers them.

**FR.25.30 — PWA:** The app ships a web manifest (name and short name `Tessyn`, `display: standalone`, theme and background colours, 192 and 512 px icons plus a maskable one), an Apple touch icon and the iOS web-app meta tags, so Add to Home Screen gives a full-screen app. A minimal service worker caches only the app shell (hashed assets; `index.html` network-first) and never caches `/api/*` or `/link/*`. A production build contains no mock data.

#### Remote API and daemon behaviour

**FR.25.31 — Remote API v1:** The daemon exposes these JSON-RPC 2.0 methods (TDCP; `docs/refactoring/tdcp-spec.md` §8 has params and results). Each is **remote-callable** (FR.25.06). Only these 21 are reachable through the relay.

| Method | What it does |
|---|---|
| `client.hello` | Protocol version, daemon version, machine name, run id, server time. |
| `tree.snapshot` | The tree (directories, groups, top-level sessions) with a version; `unchanged` when the client's version is current. |
| `tree.workers` | The workers of one supervisor. |
| `tree.search` | Content search with the semantics of FR.25.11; returns ranked top-level matches with the matching workers. |
| `tree.createSession` | Creates a session relative to an anchor (FR.25.12). |
| `recent.list` | The Recent rows of FR.25.14. |
| `recent.hide` | Hides a non-pinned session from Recent (`RecentIgnoredAt`). |
| `tags.search` | The chips and the session groups of FR.25.15. |
| `session.get` | Header data, tags, attention summary, state, last used settings, streaming mode, entry count, epoch. |
| `session.entries` | A page of transcript entries (FR.25.18). |
| `session.entry` | One full entry; fails when the epoch differs. |
| `session.send` | Starts a turn (FR.25.33) and returns at once with `queued`. |
| `session.stop` | Stops the Persistent Session Process and cancels the turn. |
| `session.compact` | Compaction (FR.25.26). |
| `session.setFavorite` | Sets the favourite flag. |
| `session.setStreaming` | Sets Streaming Session Mode (FR.25.25). |
| `attention.list` | Focus items and summary of a session. |
| `attention.reply` | Answers one or more items and starts the turn (FR.25.22). |
| `attention.dismiss` | Dismisses one item. |
| `catalog.get` | Models, efforts and profiles for the Settings sheet. |
| `usage.get` | The usage windows of a profile (FR.25.27). |

The daemon builds every result from a snapshot taken under the registry lock; live models are never serialised. Errors: `-32700` parse, `-32600` invalid request, `-32601` method not found or not remote-callable, `-32602` invalid params, `-32000` daemon error (the message is user-facing, e.g. `Session not found`), `-32603` internal; server-made `-32001` daemon offline and `-32002` daemon timeout. The dispatcher shared by the loopback `/tdcp` endpoint and the relay answers every input with a complete JSON-RPC response and never throws. **Legacy loopback behaviour change (2026-11-13):** the 17 existing loopback methods now answer a missing or mistyped param with `-32602` (was `-32000`), and a malformed request or unexpected handler exception gives a JSON-RPC error (`-32603 "Internal error"`, logged, the message never leaks) with HTTP 200 instead of an HTTP 500.

**FR.25.32 — Remote Notifications (daemon → browsers):** The daemon publishes these on an event bus, which the Relay Connector forwards. Producers never block; bursts are coalesced.

| Notification | Params | Trigger |
|---|---|---|
| `tree.changed` | `version` | a structural tree change (debounced 300 ms) |
| `session.changed` | `state` | running flag, attention, favourite, streaming or an entry changed (debounced 250 ms per session) |
| `session.appended` | `nodeId`, `epoch` | an entry was appended (debounced 250 ms per session) |
| `session.reset` | `nodeId`, `epoch` | the session file was rewritten |
| `turn.completed` | `nodeId`, `cancelled`, `error?` | a turn ended (sent at once, not coalesced) |
| `attention.changed` | `nodeId`, `summary` | the Focus items changed |
| `tags.changed` | `nodeId?` | tags changed |
| `usage.changed` | `profileId` | new usage data |
| `index.changed` | — | the search index was updated (debounced 2 s) |

The server adds `link.status { online, since }` and `resync`.

**Ordering (decided 2026-11-13, tightened by the core fix):** `turn.completed` is published off the raising thread with a zero window, while `session.appended` and `session.changed` use 250 ms windows. Before `turn.completed` goes out, the daemon **flushes** that session's pending `session.appended` and `session.changed` (`IRemoteEventBus.FlushPending(method, key)`), so they normally arrive **before** it. One small gap remains: a timer that is already firing a coalesced `session.changed` while `turn.completed` flushes can still land just after it. A client therefore refreshes the session on `turn.completed` **and** keeps handling a late `session.appended`/`session.changed` rather than treating `turn.completed` as the final word. `session.reset` reads the epoch after the bump (the epoch tracker's subscription is registered first). `session.changed` is published for the running flag, attention, favourite/streaming changes (`SessionSettingsChanged`) and an appended entry; coalescing windows: tree 300 ms, session 250 ms, attention 250 ms, tags 250 ms, index 2 s.

**FR.25.33 — Web turns:**
- `session.send` and `attention.reply` start a turn through the **headless turn path** (`RunTurnAsync`) with the new source `TurnSource.Web`, redirected into an already-alive Persistent Session Process when there is one (FR.19.13). The call returns at once; the turn runs in the background. Failures become SYSTEM entries (as today) and `turn.completed { error }`.
- **Per-turn options (amended 2026-11-12):** a turn may carry model, effort and profile (invalid values: `-32602`). `modelId: default` means no `--model` flag; an absent value keeps the existing headless resolution. `effort` is passed as `--effort` (absent or `default` means none) and is stamped on the first ASSISTANT entry (FR.2.3.5). The profile is resolved to its display name and used for the turn. These options cannot change a process that is already alive (FR.19.13, FR.19.8); the web locks the selectors in that case (FR.25.24).
- **Profile stamp (amended 2026-11-12):** every headless turn (Telegram, scheduled, orchestrated, web) writes `profile="<display name>"` on its USER entry, the name actually used for the turn, so `lastUsed` (FR.25.24) and the row's profile line (FR.25.09) are correct for them too.
- **Redirect ignores per-turn options (decided 2026-11-13):** when FR.19.13 redirects the turn into an already-alive streaming process, the model, effort and profile options are ignored and logged at Information level; the USER entry is still stamped.
- The desktop shows a web turn live, like a Telegram turn. A message sent while a turn runs waits for the per-node turn lock (FR.14.9); `queued` tells the web that this happened.

**FR.25.34 — Session epochs (daemon side):** `ISessionFileService` raises `SessionFileRewritten` whenever it rewrites or replaces a file (compaction, repair). An epoch tracker keeps one counter per file in memory. The notification publisher turns the event into `session.reset`.

#### Relay link, Relay Connector and deployment

**FR.25.35 — Relay link protocol (server ⇄ daemon):**
- `GET /link/stream`: headers `Authorization: Bearer <link token>`, `X-Tessyn-Daemon: <machine name>`, `X-Tessyn-Protocol: 1`. The response is an SSE stream (`X-Accel-Buffering: no`): each frame is one JSON-RPC request (`data: {…}`); a `: ping` comment every 15 seconds. **One link at a time:** a new connection replaces the old one; the old stream ends and its pending requests fail with `-32001`. 401 for a wrong token.
- `POST /link/reply`: bearer token; a JSON array of JSON-RPC responses; 204.
- `POST /link/events`: bearer token; `{ "events": [ { "method", "params" } ] }`; 204.
- The server maps request ids (`r-<n>`) back to the browser's id (the browser id is only echoed back, so a browser cannot forge a daemon reply). Request timeout 30 s, 60 s for `session.entries`, `tree.snapshot`, `tags.search` and `tree.search` (`Link:RequestTimeoutSeconds`, `Link:LongRequestTimeoutSeconds`).
- **Frame details as built (2026-11-13).** Request frames are plain `data: {"jsonrpc":"2.0","id":"r-N","method":…,"params":…}` (no `event:` line; `params` omitted when absent). A browser-side `resync` is a normal data frame `{"method":"resync","params":{}}` carrying `id:` = the latest sequence number (not an SSE `event:`); the first `link.status` frame on connect has no `id:`. `/link/events` ignores `link.status` and `resync` posted by the daemon (server-made only) and any `seq` in posted events. The ring buffer size and ping are `Link:RingBufferEvents`, `Link:RingBufferMinutes` and `Link:PingSeconds`.
- The daemon is the JSON-RPC server although it opened the connection (`tdcp-spec.md` §3).

**FR.25.36 — Relay Connector (desktop):**
- `RelayConnectorService` in `TessynDesktop.Core` dials the server, reads requests, dispatches them (up to 8 at a time) through the shared dispatcher on the relay transport, and posts replies and notifications. Notifications are coalesced for 250 ms per POST.
- **Reconnect:** after a drop it retries after 1, 2, 5, 10 and then every 30 seconds, and the backoff resets after 60 seconds of connection. 45 seconds without any frame (request or ping) counts as a dead link. Nothing in the connector may throw out of it or delay the start of the app; while web access is off or not configured it does nothing.
- **Config.** `%AppData%\TessynDesktop\web-relay.dat` holds `{ "enabled", "url", "token" }` encrypted with DPAPI (current user). A missing or unreadable file means disabled. The relay settings are not in `appsettings.json` and are never logged.
- **Status.** The connector exposes its state (Disabled, Connecting, Connected, **Error** with a short reason such as `Link token rejected`, `Invalid web server URL` or `Link closed by the server`; last connect time) to the Settings section. A 401 jumps the retry delay to the 30 s cap. The status is read from the store at start; a hand-edited `web-relay.dat` needs a Restart or the Settings **Save**.
- **Events.** The connector subscribes to the event bus only while connected; its queue is bounded and drops the oldest, and a failed events POST is logged and dropped (the resync covers it).

**FR.25.37 — Settings section "Web access":** The desktop Settings window gets a section **Web access** with the enabled checkbox, the server URL, the link token (a write-only masked box; the line reads *Not set* or *Configured*), the current status line (*Connected since 14:02* in local time, *Connecting…*, *Error: …*, *Disabled*) and a **Save** button (it commits enabled, URL and token together). Saving writes `web-relay.dat` and **starts, stops or restarts the connector without restarting the app**; an empty token box keeps the stored token. The token is never shown again after it is saved.

**FR.25.38 — Deployment and configuration:**
- **Host (amended 2026-11-13):** server `safe`, `/var/www/tessyn` (owner the deploy user, group `tessyn-web`, mode 750, files 640, read-only to the service), systemd unit `tessyn-web` running `/var/www/tessyn/Tessyn.Web` as the **dedicated system user `tessyn-web`** (no home, `nologin`; not `www-data`, which every other site on the shared box runs as and which could otherwise read the Data Protection keys and forge login cookies) on `http://127.0.0.1:5320`; nginx virtual host `tessyn.vvinogradoff.com` with a Let's Encrypt certificate (certbot, expires 2027-02-10, auto-renewed) and HSTS; `proxy_buffering off` and long timeouts for `/api/events` and `/link/stream`; `X-Forwarded-For $remote_addr` (overwrite) in both locations; `client_max_body_size 4M`. DNS: an A record `tessyn` for `vvinogradoff.com`.
- **Sandboxing.** The unit sets `NoNewPrivileges`, `PrivateTmp`, `PrivateDevices`, `ProtectSystem=strict`, `ProtectHome`, `ProtectKernelTunables/Modules/Logs`, `ProtectControlGroups`, `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX`, an empty `CapabilityBoundingSet` and `AmbientCapabilities`, `LockPersonality`, `RestrictNamespaces`, `RestrictRealtime`, `RestrictSUIDSGID`, `SystemCallFilter=@system-service`, `SystemCallArchitectures=native`, `UMask=0077` (no `MemoryDenyWriteExecute`: it breaks the .NET JIT). `systemd-analyze security` scores 1.8 (OK).
- **Build and redeploy:** `code/Tessyn.Web/deploy/publish.ps1` (`-SkipClient` to skip the SPA build) builds the app (`npm ci`, `npm run build`) and publishes it self-contained for `linux-x64` (`net10.0`; the box has no .NET 10 runtime), then packs `artifacts/tessyn-web.tar.gz`. `code/Tessyn.Web/deploy/redeploy.sh` uploads it, creates the `tessyn-web` user (idempotent), installs the unit, applies the permissions, replaces `/var/www/tessyn`, restarts the service and checks health.
- **Config:** `/etc/tessyn-web/appsettings.Production.json` (`root:tessyn-web`, mode 640; directory `root:tessyn-web` 750; the unit points to it with `TESSYN_WEB_CONFIG`) holds the section `TessynWeb` (`Auth:Email`, `Auth:PasswordHash`, `Auth:CookieDays` 14, `Auth:AbsoluteDays` 30, `Link:TokenSha256`, `DataProtectionKeysPath`). A value in this file overrides the code default. Keys live in `/var/lib/tessyn-web/keys` and the auth epoch in `/var/lib/tessyn-web/auth-epoch` (owner `tessyn-web`, 700 and 600). The Data Protection keys are **not encrypted at rest** (a startup warning says so); only `tessyn-web` can read them (follow-up in the todo).
- **CLI:** `Tessyn.Web hash-password` and `Tessyn.Web new-link-token` (FR.25.03, FR.25.06).
- **Health (amended 2026-11-13).** `GET /healthz` returns `{ "ok": true }` publicly, without authentication. `linkOnline` is added **only when the connection comes from loopback** (after the forwarded-header processing; a spoofed `X-Forwarded-For: 127.0.0.1` does not count), so the public site does not reveal whether the PC is on.
- Step-by-step operations are in `docs/features/web-client.md`.

**FR.25.39 — Out of scope for the first version:** a second user or roles; a second factor at login (recommended as a follow-up: TOTP, see the todo); push notifications to the phone (Telegram keeps that job); instruction toggles, the MD/JSONL view and in-session search; profile management; Add directory and tree editing; browsing or editing files; more than one desktop behind one site; a native iOS app (the same API is meant to serve it later).

**FR.25.40 — Known limits and follow-ups (recorded 2026-11-13):**
- **Not verified on a real device:** the phone pass (iPhone Safari, Add to Home Screen, the service worker over HTTPS, the first login on the live site) is still open (todo Phase 40).
- **Daemon-side parameter validation.** The relay forwards `params` verbatim, so the daemon methods that start work (`session.send`, `tree.createSession`, `attention.reply`, `session.compact`) validate and cap everything they accept and take no caller-chosen permission mode, tool list, working directory, profile directory or environment. Whoever holds the link token can drive the desktop, so the relay host is part of the trusted base and the token stays DPAPI-protected on the desktop.
- **Core performance (fixed 2026-11-13, `101-core-fix`).** The activity of a session (last prompt, last response, last draft edit, profile) is cached per file stamp. A file that only **grew** is parsed from the cached length (`ISessionFileService.ReadEntriesFrom`, line-boundary safe) and merged into the cached values (newer wins); a shorter or same-length rewrite resets the entry. A cold entry parses the 64 KB tail, widened once to 4 MB (`Constants.Remote.ActivityColdParseMaxBytes`) when it holds no USER entry; beyond that the prompt is reported as null (the full-file fallback is gone). A draft file is read only when its stamp changes (`IDraftService.GetDraftStamp`: file info only, so 3,300 rows cost 3,300 stats and no reads). `LastResponseUtc` may read as null when the last response is older than the tail's last prompt; it never affects Last Activity.
- **Not yet in the web:** write-back of tool lines in Core, stopping a desktop-interactive one-shot turn, virtualised long transcripts (the transcript pages by 50 entries with **Load earlier**), TOTP, nginx `limit_req`, encrypted Data Protection keys (todo).
- **Dispatcher error mapping (amended 2026-11-13).** A bug inside a handler that throws `FormatException`, `JsonException` or `InvalidCastException` is no longer reported as `-32602`: it is `-32603 "Internal error"` and logged. Parameter problems are reported as `-32602` by `TdcpParams` and the modules (`TdcpInvalidParamsException`).
- **Cosmetic parity:** an assistant entry of a send without model and effort is stamped `default`/`default`, as on the desktop.

---

---

## Amendments and additions (batch 2026-11-17)

**FR.25.41 — Relay link settings through the Remote API.** The relay link (URL, link token, on/off) can be configured through the Remote API; the link token is write-only and must be sent again when the URL moves to another origin.
---

## Amendments and additions (2026-11-18)

| Existing | Amended by | Change |
|---|---|---|
| FR.25.24 Per-session choices and locking | FR.12.35 | `session.get.lastUsed` reports the session's model and effort settings: what the next turn without a pick uses, no longer the last ASSISTANT entry's values. A choice the web sends that differs from the session's setting is stored on the session (a sticky pick). |
