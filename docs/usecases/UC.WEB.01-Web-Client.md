# UC.WEB.01 — Web client (Tessyn Web)

Related requirements: FR.25.01–FR.25.39, and the amended FR.1.8, FR.1.17, FR.3.3, FR.11.6, FR.19.11 in [requirements.md](/docs/requirements.md). Related: [UC.FV.01](/docs/usecases/UC.FV.01-Focus-View.md) (Focus items), [UC.RT.01](/docs/usecases/UC.RT.01-Recent-Tab.md) (Recent), [UC.TG.01](/docs/usecases/UC.TG.01-Session-Tags.md) (tags), [UC.21](/docs/usecases/UC.21-Telegram-Bridge.md) (the other remote client). Test cases: [TC.WEB.01](/docs/testcases/TC.WEB.01-Web-Client.md). Remote API: [tdcp-spec.md §8](/docs/refactoring/tdcp-spec.md). Operations: [web-client.md](/docs/features/web-client.md).

## Actors

- **User** (the Architect) — works from the phone, mostly in portrait.
- **Web app** — the single-page app in the phone's browser or installed as a PWA.
- **Web Relay** — the Tessyn.Web server on `safe` (authentication, relay, app host).
- **Daemon** — the daemon inside TessynDesktop on the PC (Relay Connector, Remote API).
- **Desktop** — the TessynDesktop window, which shows the same sessions.

## Preconditions (common)

- TessynDesktop runs, web access is enabled in its Settings and the Daemon Link is up (the status dot in the top bar is green), unless a use case says otherwise.
- The User is logged in unless the use case is about logging in.
- Terms: Tessyn Web, Web Relay, Daemon Link, Link Token, Relay Connector, Remote API, Remote Notification, Session Epoch, Pending Bubble (see the glossary).

---

## UC.WEB.01 — Log in

**Actors:** User, Web app, Web Relay.
**Preconditions:** The User has no valid cookie.

**Main flow:**
1. The User opens `https://tessyn.vvinogradoff.com`. The Web app calls `GET /api/auth/me`, gets 401 and shows the login page (email, password).
2. The User enters the email and password from the server config and taps **Log in**.
3. The Web Relay checks the password against the PBKDF2 hash in constant time and answers 200 with the `__Host-Tessyn.Auth` cookie (and the trusted-device cookie `__Host-Tessyn.Device`).
4. The Web app shows the main screen: top bar, empty state with a button that opens the sessions panel, no usage strip.

**Alternative flows:**
- **A1 — Wrong password or unknown email:** 401, the same error text for both; nothing else is revealed.
- **A2 — Valid cookie:** step 1 gets 200 and the app goes straight to the main screen; each request slides the cookie's 14-day idle limit, up to an absolute 30 days after login; a logout on any device, or a password change, ends it on every device.
- **A3 — Throttled:** see UC.WEB.21.
- **A4 — Cookie expired or removed while using the app:** the next API call returns 401 and the app shows the login page.

**Postconditions:** the browser holds the cookie; no password is stored in the browser.
**Covers:** FR.25.03, FR.25.05, FR.25.08.

---

## UC.WEB.02 — Browse the tree and open a session

**Actors:** User, Web app, Daemon.

**Main flow:**
1. The User taps ☰. The sessions panel covers the page, on the Tree tab (or the tab used last).
2. The Web app has fetched `tree.snapshot`. It shows the directories with their labels, groups and top-level sessions in the desktop's order.
3. The User taps a directory. The row is selected and its content expands; the state is stored in the browser.
4. The User taps a supervisor session's chevron. The Web app calls `tree.workers` and shows the workers nested under it.
5. The User taps a session. The panel collapses and the session opens on the Session tab (UC.WEB.07).

**Alternative flows:**
- **A1 — First visit:** directories and groups start expanded or collapsed as on the desktop (`isExpanded`); later the browser's own state wins.
- **A2 — Tree changes on the desktop:** a `tree.changed` notification makes the Web app fetch a full `tree.snapshot` (as built, the app does not send `sinceVersion`; the daemon would answer `unchanged` for a current version).
- **A3 — Rows:** a session row shows the status block (top half dark red while running), the attention pill, ✏ for a draft, ★ for a favourite, the profile and the last prompt time, and a green background by recency.

**Postconditions:** the opened session is part of the address (`#/s/<nodeId>`); the panel is collapsed.
**Covers:** FR.25.08, FR.25.09, FR.25.10.

---

## UC.WEB.03 — Search the tree

**Actors:** User, Web app, Daemon.
**Preconditions:** The sessions panel is open on the Tree tab.

**Main flow:**
1. The User types `invoice export` in the search box. After 300 ms the Web app calls `tree.search` and shows a spinner.
2. The Daemon runs the content search and returns the ranked top-level matches (with their matching workers).
3. The Web app shows only the matches, their ancestor groups and directories (**expanded**, whatever their stored state) and, for a supervisor, the matching workers nested.
4. Order: best match first, containers by their best descendant.
5. The User taps a result. The session opens (UC.WEB.02 step 5).
6. The User taps × in the box. The normal tree comes back with the expansion state as before the search.

**Alternative flows:**
- **A1 — Exact:** a query shorter than 8 characters or starting with `https://` switches **Exact** on and only strict matches are shown; if the User unticks it, it stays unticked until the text changes.
- **A2 — Project toggle:** with a session selected the toggle is on and the search is limited to that session's project; with none selected it is off and disabled.
- **A3 — Index failure:** the Daemon uses the file scan; results are still shown.
- **A4 — No match:** an empty tree with a short "no results" text.
- **A5 — Index changed:** an `index.changed` notification re-runs the active search.

**Covers:** FR.25.11, FR.1.9–FR.1.14.

---

## UC.WEB.04 — Create a session

**Actors:** User, Web app, Daemon, Desktop.
**Preconditions:** The Tree tab is open.

**Main flow (anchor is a session):**
1. The User selects session `S2` in a group that holds `S1`, `S2`, `S3`.
2. The User taps **Create session**, types `Release notes` and confirms.
3. The Web app calls `tree.createSession { anchorId: S2, name }`.
4. The Daemon inserts the new session **right after `S2`** (order `S1`, `S2`, new, `S3`), saves once and answers with the new row.
5. The Desktop's tree shows the new row at the same place without a refresh.
6. The Web app opens the new session; the message box has focus.

**Alternative flows:**
- **A1 — Anchor is a group:** the new session is the **first** in that group.
- **A2 — Anchor is a directory:** the new session is the first in the directory's own session list (index 0).
- **A3 — Anchor is a worker:** it resolves to the worker's top-level supervisor; the new session goes after the supervisor.
- **A4 — Nothing selected:** the button is disabled.
- **A5 — Empty name:** the dialog does not accept it; if it reaches the Daemon, it answers `-32602`.
- **A6 — Defaults:** the new session has the settings a desktop-created session has (empty last used values, streaming off unless the desktop's default says otherwise).

**Postconditions:** one new session node; one save.
**Covers:** FR.25.12, FR.25.13, FR.1.17.

---

## UC.WEB.05 — Recent

**Actors:** User, Web app, Daemon.

**Main flow:**
1. The User opens the panel and taps **Recent**. The Web app calls `recent.list`.
2. The list shows the sessions of the desktop's Recent dropdown, flat, newest activity first: pinned sessions (favourite, open Focus item of any kind, or a schedule) and sessions active in the last 24 hours that are not hidden.
3. A row shows the status block, the attention pill, the name, the directory label, the profile, 🕒 for schedules and ★ for a favourite.
4. The User taps ✕ on a row that is not pinned. The Web app calls `recent.hide`; the row disappears.
5. The User taps another row. The session opens.

**Alternative flows:**
- **A1 — Pinned session:** it has no ✕ and cannot be hidden from the web.
- **A2 — Live:** a `session.changed` or `attention.changed` notification refreshes the list (debounced).
- **A3 — Empty:** an empty list shows a short text.

**Covers:** FR.25.14, FR.9.6–FR.9.8.

---

## UC.WEB.06 — Tags

**Actors:** User, Web app, Daemon.

**Main flow:**
1. The User opens the panel, taps **Tags** and sees the chips of the tags in the selected session's directory (the 📁 toggle is on).
2. The User types `303`. The chips narrow to the tags whose label or key contains it.
3. The User taps `backend PR #303`, clears the text and taps `HSE-254`. Both stay selected; selected chips come first.
4. The session list shows the top-level sessions carrying either tag, grouped by directory.
5. The User collapses one directory header; it stays collapsed after the next refresh.
6. The User taps a session. It opens.

**Alternative flows:**
- **A1 — Scope off:** tags of every directory.
- **A2 — Nothing selected:** the list says `Select tags to list their sessions`.
- **A3 — No sessions:** `No sessions with the selected tags`.
- **A4 — No tags:** `No tags in this directory`, `No tags yet` or `No matching tags`.
- **A5 — Open the tag link:** long-press or ↗ on a chip opens its URL in a new browser tab.

**Covers:** FR.25.15, FR.24.11–FR.24.13.

---

## UC.WEB.07 — Read a session and follow a live turn

**Actors:** User, Web app, Daemon.

**Main flow:**
1. The User opens a session. The Web app calls `session.get` and `session.entries` (the last 50) and shows the header (location, name, tag chips), the Session | Focus switch and the transcript at the bottom.
2. USER entries are right-aligned bubbles; ASSISTANT entries are full-width markdown with code blocks and tables; SYSTEM entries are small grey boxes; a compaction is a separator line.
3. A turn is running (started anywhere: desktop, Telegram, scheduler, web). The top bar shows the running state and the transcript ends with `Claude is thinking… 0:42`.
4. The Daemon publishes `session.appended`; the Web app fetches the entries after the last index and appends them. If the User is at the bottom the view follows; otherwise a **↓ New messages** pill appears.
5. The Daemon publishes `turn.completed` and `session.changed` (not running); the thinking row disappears.

**Alternative flows:**
- **A1 — Load earlier:** the User scrolls to the top and taps **Load earlier**; the previous 50 entries load (`beforeIndex`).
- **A2 — Truncated entry:** an entry above 32,768 characters ends with **Show full message**; tapping it loads the full entry (`session.entry`).
- **A3 — Hidden entries:** entries with empty or whitespace-only content are not shown, except compactions.
- **A4 — Entry with a leading `[`:** a SYSTEM entry such as `[Attention A3 (ask)] …` or a USER entry whose body lines start with `[` shows its full content (FR.3.3 amended).
- **A5 — Epoch change:** see UC.WEB.23.

**Covers:** FR.25.16, FR.25.17, FR.25.18, FR.25.19, FR.1.8, FR.3.3.

---

## UC.WEB.08 — Send a message while the session is idle

**Actors:** User, Web app, Daemon.
**Preconditions:** The session is open on the Session tab and no turn is running.

**Main flow:**
1. The User types a message and taps **Send** (or presses Ctrl+Enter on a keyboard).
2. The Web app shows a Pending Bubble (dimmed) and clears the box and the stored draft.
3. The Web app calls `session.send` with the text and the session's model, effort and profile choice.
4. The Daemon accepts at once (`accepted: true, queued: false`) and starts the turn through the headless path with `TurnSource.Web`.
5. The Daemon appends the USER entry (with `profile="…"` of the profile used) and sets running; the Web app replaces the Pending Bubble with the entry and shows the thinking row.
6. The reply streams in as in UC.WEB.07.

**Alternative flows:**
- **A1 — Failure:** a turn failure is written as a SYSTEM entry and `turn.completed { error }`; the Pending Bubble was already replaced or is removed with an error note.
- **A2 — Per-turn options:** a model other than Default is passed as `--model`; an effort other than Default as `--effort` and shown on the reply as `[model, effort]`.
- **A3 — Draft kept:** a message typed but not sent stays in the box when the User opens another session and comes back.
- **A4 — Streaming session:** with a Persistent Session Process alive, the message goes to that process (FR.19.13) and the selectors are locked (UC.WEB.14 A2).

**Covers:** FR.25.19, FR.25.20, FR.25.24, FR.25.33.

---

## UC.WEB.09 — Send while a turn is running (queue)

**Actors:** User, Web app, Daemon.
**Preconditions:** A turn is running in the session.

**Main flow:**
1. **Send** is enabled. The User types a second message and taps it.
2. The Web app shows a Pending Bubble and calls `session.send`.
3. The Daemon answers `accepted: true, queued: true`; the bubble is marked **queued**.
4. The first turn ends. The second turn takes the per-node turn lock (FR.14.9) and starts; its USER entry appears and replaces the bubble.

**Alternative flows:**
- **A1 — Turn started on the desktop:** the running flag also counts a turn started in the desktop's own session view, so the web message queues behind it.
- **A2 — Stopped before it starts:** UC.WEB.10 cancels the running turn; the queued message then starts.

**Covers:** FR.25.19, FR.25.20, FR.1.8, FR.14.9.

---

## UC.WEB.10 — Stop a running turn

**Actors:** User, Web app, Daemon.
**Preconditions:** A turn is running.

**Main flow:**
1. The **Stop** button is visible. The User taps it.
2. The Web app calls `session.stop`.
3. The Daemon stops the Persistent Session Process if one is alive and cancels the turn.
4. `turn.completed { cancelled: true }` arrives; the thinking row disappears; the Stop button hides.

**Alternative flows:**
- **A1 — Nothing to stop:** `stopped: false`; the Web app just refreshes the state.

**Covers:** FR.25.20, FR.19.5.

---

## UC.WEB.11 — Answer a Focus item (Reply)

**Actors:** User, Web app, Daemon, Session.
**Preconditions:** The session has an open `ask` item `A3` with two options and a recommendation. The Session tab shows a goldenrod badge `1` on the Focus switch.

**Main flow:**
1. The User taps **Focus**. The card shows the kind chip, `A3`, the title, the time, the markdown body, the option buttons, **Use recommendation: …**, the reply box and **Dismiss**.
2. The User taps an option; its text appears in the reply box. (**Use recommendation** fills `Accepted your recommendation: …`.)
3. The User taps **Reply**. The Web app calls `attention.reply` with that one answer and the session's model, effort and profile.
4. The Daemon marks `A3` answered first, builds the answers prompt (the title resolved on the daemon) and starts the turn through the web turn path.
5. The card moves to **Closed (1)** (`Answered: …`); the badge disappears; the transcript gets the USER entry.

**Alternative flows:**
- **A1 — Item already closed:** it is in `skippedIds`; no turn starts if all were skipped.
- **A2 — Refresh keeps text:** an `attention.changed` refresh keeps what was typed into other cards' reply boxes.
- **A3 — Empty box:** **Reply** is disabled.

**Covers:** FR.25.21, FR.25.22, FR.23.14, FR.23.15.

---

## UC.WEB.12 — Send answers to several items

**Actors:** User, Web app, Daemon.
**Preconditions:** Items `A3` (ask) and `A5` (todo) are open.

**Main flow:**
1. The User types text in both cards. The button **Send answers (2)** appears.
2. The User taps it. One `attention.reply` carries both answers.
3. The Daemon marks both answered, builds one prompt and starts one turn.
4. Both cards move to Closed.

**Alternative flows:**
- **A1 — One card:** with text in one card only, the button reads **Send answers (1)**.
- **A2 — A card closed in the meantime:** it is reported in `skippedIds` and left out of the prompt.

**Covers:** FR.25.22.

---

## UC.WEB.13 — Dismiss or acknowledge a Focus item

**Actors:** User, Web app, Daemon.

**Main flow:**
1. A card of kind `ask` or `todo` shows **Dismiss** on the far right; a card of kind `critical`, `alert` or `decided` shows **Got it**.
2. The User taps it. The Web app calls `attention.dismiss`.
3. The Daemon sets the item to Dismissed and writes `[Attention A12 dismissed by the user]` to the session file; no turn starts.
4. The card moves to Closed with `Dismissed`.

**Alternative flows:**
- **A1 — Text in the reply box:** it is discarded without a prompt.
- **A2 — Decided items:** they are in the **Decisions (n)** expander, collapsed by default.
- **A3 — Empty Focus:** `Nothing needs your attention in this session.`

**Covers:** FR.25.21, FR.25.22, FR.23.2.

---

## UC.WEB.14 — Change session settings

**Actors:** User, Web app, Daemon.

**Main flow:**
1. The User taps ⚙. The bottom sheet shows Favourite, Model, Profile, Effort and Streaming.
2. On the first open of the session in this browser, Model, Profile and Effort show the values the desktop would restore (`lastUsed`: from the last ASSISTANT entry and the last USER entry).
3. The User picks Model `claude-sonnet-…`, Effort `High` and taps the ★. Favourite goes to the Daemon (`session.setFavorite`); model and effort stay in the browser for this session.
4. The next send, reply or compact of this session carries them.

**Alternative flows:**
- **A1 — Another profile:** the usage strip changes to that profile's windows (UC.WEB.17).
- **A2 — Process alive:** while a Persistent Session Process is alive, Model, Profile and Effort are disabled until it ends.
- **A3 — Desktop refresh:** changing the favourite or streaming flag refreshes the desktop's session view and row.
- **A4 — Not offered:** no profile actions (re-authenticate, rename, remove), no browser toggle, no sound setting.

**Covers:** FR.25.23, FR.25.24, FR.12.3, FR.12.6, FR.12.12, FR.19.8.

---

## UC.WEB.15 — Turn Streaming off (with and without a live process)

**Actors:** User, Web app, Daemon.

**Main flow (no process alive):**
1. In the sheet the User switches **Streaming** off. `session.setStreaming { enabled: false }` returns `needsConfirm: false` and the setting is saved.

**Alternative flows:**
- **A1 — Process alive:** the Daemon changes nothing and returns `needsConfirm: true`. The Web app asks **Stop the running Claude process for this session?**
  - **Yes:** the Web app repeats the call with `force`; the Daemon stops the process and turns streaming off.
  - **No:** the toggle stays on.
- **A2 — Turning on:** never asks.

**Covers:** FR.25.25, FR.19.6, FR.19.7.

---

## UC.WEB.16 — Compact a session

**Actors:** User, Web app, Daemon.
**Preconditions:** The session has history.

**Main flow:**
1. The User taps ⋯ → **Compact session…**; the dialog asks `Compact this session? The transcript is replaced by a summary.`
2. The User confirms. The Web app calls `session.compact`.
3. The Daemon runs the Core compaction service under the turn lock; the session shows as running.
4. On success the session file is rewritten; the Daemon raises `session.reset` with the new epoch; the Web app reloads the transcript, which now starts with the summary.

**Alternative flows:**
- **A1 — Nothing to compact:** a SYSTEM entry `[Nothing to compact]`; the file is unchanged.
- **A2 — Failure:** a SYSTEM entry `[Compaction failed — session left unchanged: …]`; the file is unchanged.
- **A3 — Turn running:** the compaction waits for the turn lock (`queued: true`).

**Covers:** FR.25.26, FR.11.6, FR.25.18, FR.25.34.

---

## UC.WEB.17 — Usage strip

**Actors:** User, Web app, Daemon.

**Main flow:**
1. A session is open. The strip at the bottom shows `5h: 37%  resets 14:20` and `7d: 12%  resets Mon` for the session's profile.
2. Every 5 minutes while the page is visible, and when the profile choice changes, the Web app calls `usage.get`.
3. A `usage.changed` notification for that profile refreshes the strip.

**Alternative flows:**
- **A1 — No session open:** the strip is hidden.
- **A2 — State not ok** (`pending`, `unauthorized`, `unavailable`): the strip is hidden.
- **A3 — Stale window:** the bar shows the percentage without "resets".

**Covers:** FR.25.27, FR.18.

---

## UC.WEB.18 — Desktop offline and reconnect

**Actors:** User, Web app, Web Relay, Daemon.

**Main flow:**
1. The PC goes to sleep (or TessynDesktop closes, or web access is switched off). The Daemon Link drops; the Web Relay publishes `link.status { online: false }`.
2. The Web app shows **Desktop offline**, the grey status dot, and disables Send. Requests that were pending fail with `-32001`.
3. The Connector on the PC reconnects with backoff (1, 2, 5, 10, then every 30 seconds).
4. The link is up; `link.status { online: true }` arrives. The Web app resyncs (tree, open session, Focus, usage) and re-enables Send.

**Alternative flows:**
- **A1 — Timeout:** a request with no answer within its limit fails with `-32002` and the Web app shows a short error.
- **A2 — Dead link detected by the Connector:** 45 seconds without a frame; it reconnects by itself.
- **A3 — Another link connects:** the new link replaces the old one; the old one's pending requests fail with `-32001`.
- **A4 — Wrong link token:** the server answers 401; the Connector shows **Error: Link token rejected** in the Settings section and keeps retrying (30 s delay).

**Covers:** FR.25.28, FR.25.29, FR.25.35, FR.25.36.

---

## UC.WEB.19 — Phone backgrounded and resumed (resync)

**Actors:** User, Web app, Web Relay.

**Main flow:**
1. The User switches to another app for a few minutes. iOS ends the event stream.
2. The User comes back. The page becomes visible; the Web app sees that the stream was closed (or silent for more than 30 seconds), reopens `GET /api/events` with `Last-Event-ID` and resyncs.
3. If the Web Relay still has the missed events, it replays them; otherwise it sends `resync`. Either way the Web app refetches the tree (a full snapshot), the open session (entries after the last index), its Focus items and the usage strip.
4. A turn that finished meanwhile shows its entries.

**Alternative flows:**
- **A1 — Short absence:** the stream is still alive; nothing is refetched.
- **A2 — Epoch changed meanwhile:** the transcript reloads.

**Covers:** FR.25.28, FR.25.29.

---

## UC.WEB.20 — Log out

**Actors:** User, Web app, Web Relay.

**Main flow:**
1. The User taps ⋯ → **Log out**.
2. The Web app calls `POST /api/auth/logout`; the Web Relay clears the cookie (204).
3. The Web app closes the event stream and shows the login page. The next API call without login gets 401.

**Alternative flows:**
- **A1 — Drafts and choices:** per-session drafts and settings stay in the browser's local storage.

**Covers:** FR.25.03.

---

## UC.WEB.21 — Throttled login

**Actors:** User (or an attacker), Web Relay.

**Main flow:**
1. Five wrong passwords come from one IP within 15 minutes.
2. The sixth attempt gets 429 with `Retry-After`; the correct password is not checked either.
3. The login page shows that the User must wait, and for how long.
4. After the window the User logs in normally.

**Alternative flows:**
- **A1 — Global limit:** 20 failures in an hour from any IPs also give 429 for everyone until the window moves on.
- **A2 — Missing header:** a login POST without `X-Tessyn-Client: web` gets 400 and does not count as a password check.

**Covers:** FR.25.04, FR.25.05.

---

## UC.WEB.22 — Enable web access on the desktop

**Actors:** User, Desktop, Daemon, Web Relay.
**Preconditions:** The server runs. The User has the Link Token from `Tessyn.Web new-link-token`, and its hash is in the server config.

**Main flow:**
1. In the desktop Settings, section **Web access**, the User ticks Enabled, enters `https://tessyn.vvinogradoff.com`, pastes the Link Token and taps **Save**.
2. The desktop writes `web-relay.dat` (DPAPI) and starts the Relay Connector without restarting.
3. The status moves from Connecting to Connected; the web app shows the green dot.

**Alternative flows:**
- **A1 — Disable:** unticking Enabled and saving stops the Connector; the web app shows **Desktop offline**.
- **A2 — Rotate the token:** the User runs `new-link-token`, puts the hash in the server config, restarts the server, pastes the new token in Settings and saves. The old token no longer works.
- **A3 — No file or unreadable file:** the Connector stays disabled; the desktop starts normally.
- **A4 — Wrong token or URL:** the status shows **Error** with a short reason; no crash, no error dialog.

**Covers:** FR.25.36, FR.25.37, FR.25.06.

---

## UC.WEB.23 — Session history rewritten while open (epoch)

**Actors:** User, Web app, Daemon.

**Main flow:**
1. The session is open in the Web app at epoch 0, showing entries 0–120.
2. The session is compacted (on the desktop or from the web). The file is rewritten and the Daemon publishes `session.reset { epoch: 1 }`.
3. The Web app drops its entries and reloads the last 50 of the new file.

**Alternative flows:**
- **A1 — Full entry requested with an old epoch:** `session.entry` fails with `Session history changed`; the Web app reloads.
- **A2 — TessynDesktop restart:** epochs start at 0 again; the Web app resyncs on `link.status` and reloads when the epoch differs from what it holds.

**Covers:** FR.25.18, FR.25.34.

---

## UC.WEB.24 — Install the app on the phone (PWA)

**Actors:** User, Web app.

**Main flow:**
1. On the iPhone the User opens the site in Safari, taps Share → **Add to Home Screen**.
2. The Tessyn icon appears. Opening it shows the app full-screen without the browser bars, in portrait, with the safe areas respected.
3. The service worker caches the app shell; a later start shows the shell at once and then loads data; API calls are never served from the cache.

**Alternative flows:**
- **A1 — New version deployed:** `index.html` is fetched network-first, so the next launch loads the new version.
- **A2 — Offline start:** the shell loads, the login or main screen shows an error or **Desktop offline**.

**Covers:** FR.25.08, FR.25.30.
