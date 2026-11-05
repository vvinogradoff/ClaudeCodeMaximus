# UC.FV.01 — Focus view and the Attention Ledger

Related requirements: FR.23.1–FR.23.17 and FR.9.8 in [requirements.md](/docs/requirements.md). Related: [UC.21](/docs/usecases/UC.21-Telegram-Bridge.md) (`notify_user`, Telegram and desktop notifications, FR.21.19, FR.21.24). Test cases: [TC.FV.01](/docs/testcases/TC.FV.01-Focus-View.md).

## Actors

- **User** (the Architect) — reads and answers items in the Focus view, closes them.
- **Session** — a top-level Claude session that posts items through the `cmx` MCP tools (`post_attention`, `resolve_attention`, `list_attention`, `notify_user`).
- **Worker Session** — a session spawned by an orchestrator (`OrchestrationDepth > 0`).
- **Scheduler** — starts scheduled turns in a session.
- **Tessyn** — keeps the Attention Ledger, builds the hidden instruction block, notifies.

## Preconditions (common)

- Agent tools are enabled, so the session has the `cmx` MCP server.
- The Session view is selected unless a use case says otherwise.
- Terms: Attention Item, Attention Ledger, Focus view, Focus badge, Pinned list (see the glossary).

---

## UC.FV.01 — A session pins an ask and the User reads it in Focus

**Actors:** Session, User, Tessyn.
**Preconditions:** The ledger of the session is empty.

**Main flow:**
1. The Session calls `post_attention` with one item: kind `ask`, a title, a markdown body, two options and a recommendation.
2. Tessyn creates item `A1` (status Open) in the ledger `%AppData%\TessynDesktop\attention\<NodeId>.json`.
3. Tessyn appends a SYSTEM entry `[Attention A1 (ask)] <title>` (body, `Options:`, `Recommendation:` lines follow) to the session file.
4. The tool returns `A1 posted (ask).` plus the closing line `Pinned items stay in the user's Focus view until resolved - don't restate them; refer to them by id.`
5. The Focus badge appears next to the Focus switch with `1` on a goldenrod background.
6. The User switches to Focus. The card shows the kind chip, `A1`, the title, the posted time, the markdown body, the option buttons, "Use recommendation: …", a reply box and the **Dismiss** button.

**Alternative flows:**
- **A1 — No notify:** an `ask` posted without `notify` sends no Telegram or desktop notification.
- **A2 — Other kinds:** `todo` (Dismiss button), `alert` and `decided` (Got it button) are pinned the same way; `decided` goes to the "Decisions (n)" expander.
- **A3 — Invalid item:** an unknown kind or an empty title is a tool error; nothing is posted and no SYSTEM entry is written.
- **A4 — Limits:** a title with newlines is flattened to one line and cut to 200 characters; options are trimmed, empty ones dropped, each cut to 200 characters; behaviour above 10 options is an open question (see TC.FV.05 and the report).

**Postconditions:** the ledger holds `A1` with `NextNumber = 2`; the session file holds the SYSTEM entry; the badge shows 1.
**Covers:** FR.23.1, FR.23.2, FR.23.3, FR.23.4, FR.23.8, FR.23.13, FR.23.14.

---

## UC.FV.02 — A session posts a critical item: immediate notification and dark-red badge

**Actors:** Session, User, Tessyn.

**Main flow:**
1. The Session calls `post_attention` with one `critical` item (no `notify` flag).
2. Tessyn creates the item and, because a created item is `Critical`, sends one notification through the `notify_user` fan-out: Telegram `NotifyAsync` (mention and sound) and the desktop toast, each gated by `TelegramEnabled` / `NotificationsEnabled`.
3. The message is `A1 (critical): <title>` + blank line + body.
4. The tool result ends with `Notified: Telegram, desktop.` (the channels that delivered) before the closing line.
5. The Focus badge turns dark red (`#8B0000`) with a white number.

**Alternative flows:**
- **A1 — No channel enabled:** the result says `Notification not sent - neither Telegram nor desktop notifications are enabled.`; the item is still pinned.
- **A2 — Several items in one call:** if at least one created item is `Critical` (or `notify` is true), exactly one notification is sent: `<n> new items in the Focus view:` and one `- A12 (critical): <title>` line per created item.
- **A3 — `notify: true`:** a non-critical item with `notify` true also notifies.
- **A4 — Update only:** if every item in the call is a dedupe update, nothing is sent (see UC.FV.03).

**Postconditions:** one notification per call at most; the item is Open.
**Covers:** FR.23.1, FR.23.4, FR.23.9, FR.23.13.

---

## UC.FV.03 — A session re-posts an open item: dedupe, no repeat

**Actors:** Session, Tessyn.
**Preconditions:** `A7` (ask, key `release-switch`, title "Production switch is open") is Open.

**Main flow:**
1. A later turn calls `post_attention` again with the same `key` (or, without a key, the same title in different case/whitespace).
2. Tessyn finds the Open item by key (case-insensitive) and, failing that, by normalised title.
3. The existing item is updated (kind, title, body, options, recommendation, key if given, `UpdatedUtc`); the id stays `A7`; `NextNumber` is unchanged.
4. A SYSTEM entry `[Attention A7 (ask) updated] <title>` is written.
5. The tool returns `A7 already pinned (ask) - updated, not re-posted.`
6. No notification is sent, even if the item is `Critical` or `notify` is true.

**Alternative flows:**
- **A1 — Matching item is closed:** dedupe looks at Open items only; if `A7` was answered, resolved or dismissed, a new item `A8` is created.
- **A2 — Mixed call:** one new and one matching item: the new one is created, the match is updated; the notification rules apply to the created item only.

**Postconditions:** exactly one Open item per key or title.
**Covers:** FR.23.2, FR.23.4, FR.23.8, FR.23.9.

---

## UC.FV.04 — Scheduled turns do not restate pinned items

**Actors:** Scheduler, Session, Tessyn.
**Preconditions:** `A12` (critical) and `A3` (ask) are Open.

**Main flow:**
1. The Scheduler starts a turn in the Session.
2. Tessyn builds the hidden instruction block. Because the session has open items, the block carries the Pinned list:
   - `- Pinned in the user's Focus view - they can see these, so don't restate them; refer to them by id, and call mcp__cmx__resolve_attention when one is answered in conversation or no longer applies:`
   - `  - A12 critical: <title>` and `  - A3 ask: <title>`
3. With the scheduled-turn reminder, the Session is told not to restate pinned items, and to post only what is not yet pinned.
4. The Session replies without restating the ask; it refers to `A3` by id at most.

**Alternative flows:**
- **A1 — More than 20 open items:** the list shows 20 lines and `  - ...and <n> more (call mcp__cmx__list_attention)`.
- **A2 — Long titles:** cut to 120 characters plus `...`.
- **A3 — No open items and no closures:** the Pinned list is absent.
- **A4 — Worker Session or agent tools off:** the Pinned list is not emitted.
- **A5 — Not a fresh session:** the Pinned list is emitted on every turn, not only the first (unlike the guidance, UC.FV.14).

**Postconditions:** the User sees each ask once, in Focus, instead of in every scheduled reply.
**Covers:** FR.23.10, FR.23.11.

---

## UC.FV.05 — The User answers several items from Focus in one prompt

**Actors:** User, Tessyn, Session.
**Preconditions:** `A3` (ask) and `A5` (ask) are Open; the main input box holds an unsent draft.

**Main flow:**
1. In Focus the User clicks an option on `A3`: the option text fills the card's reply box (replacing it). The User edits it.
2. On `A5` the User clicks "Use recommendation: …": the reply box gets `Accepted your recommendation: <recommendation>`.
3. The **Send answers (2)** button is enabled. The User clicks it.
4. Tessyn builds one prompt, cards in order:
   ```
   Answers from the Focus view:
   - A3 (<title>): <answer>
   - A5 (<title>): <answer>
   ```
   Continuation lines of a multi-line answer are indented by two spaces.
5. Before sending, Tessyn marks `A3` and `A5` `Answered` with `Answer` = the text being sent and clears the reply boxes, so the turn's instruction block does not list them as pinned.
6. The prompt is then sent through the same interactive send path as the main input box (same model, effort, profile and instruction block).
7. The main input box and its draft are untouched.
8. The items appear in "Closed (n)" showing `Answered: <answer>`.
9. No SYSTEM entry is written for the answers; the USER prompt is the record.

**Alternative flows:**
- **A1 — Empty reply boxes:** a card with an empty box is not part of the prompt; with no text anywhere the button is disabled.
- **A2 — Session cannot send** (the main Send button is disabled): the button is disabled. A running turn alone does not disable it, because concurrent sends are allowed.
- **A3 — Per-card Reply:** see UC.FV.16.

**Postconditions:** one USER prompt; items Answered; draft preserved.
**Covers:** FR.23.14, FR.23.15, FR.23.16.

---

## UC.FV.06 — The User answers in free text and the session resolves the item

**Actors:** User, Session.
**Preconditions:** `A4` (ask) is Open and pinned.

**Main flow:**
1. The User answers `A4` in the main input box, not in Focus.
2. The Session handles the answer and calls `resolve_attention` with `id: "a4"`, `status: "answered"`, `note: "<their answer>"`.
3. Tessyn sets `A4` to `Answered` with `Answer` = note and writes `[Attention A4 answered] <note>`.
4. The tool returns `A4 marked answered.`
5. The badge count drops live; the item moves to "Closed" with `Answered: <note>`; the next turn's Pinned list no longer lists it.

**Alternative flows:**
- **A1 — `resolved`:** `status: "resolved"` sets `ResolutionNote`; result `A4 resolved.`; SYSTEM entry `[Attention A4 resolved] <note>` (no trailing space when the note is empty).
- **A2 — Already closed:** result `A4 is already answered.`; nothing changes and no SYSTEM entry is written.
- **A3 — Unknown id:** tool error `Unknown attention item 'A99'.`
- **A4 — `list_attention`:** the Session lists items; `open` (default) shows `A12 [critical, open] <title>`, `all` also shows closed lines; empty: `No open items.` / `No items.`

**Postconditions:** the ledger reflects the conversation; the item is not re-raised.
**Covers:** FR.23.2, FR.23.5, FR.23.8, FR.23.11.

---

## UC.FV.07 — The User closes items from Focus and the session learns on its next turn

**Actors:** User, Tessyn, Session.
**Preconditions:** `A5` (todo) and `A4` (ask) are Open.

**Main flow:**
1. The User clicks **Dismiss** on `A5` (Todo) and on `A4` (Ask). The close button sits on the far right of the card, apart from Reply, with a tooltip saying it closes without sending.
2. Tessyn sets both items `Dismissed`, whatever their kind, with `IsUserCloseUnreported = true`. No prompt is sent; any text typed in those cards' reply boxes is discarded.
3. SYSTEM entries `[Attention A5 dismissed by the user]` and `[Attention A4 dismissed by the user]` are written.
4. On the next turn (any source) the instruction block carries: `- Closed by the user in the Focus view since your last turn: A5 todo "<title>" dismissed without an answer; A4 ask "<title>" dismissed without an answer.`
5. The closures are cleared when read, so the turn after that does not repeat the line.

**Alternative flows:**
- **A1 — Critical/Alert/Decided:** **Got it** closes the item as `Dismissed`, reported as `dismissed without an answer`.
- **A2 — Answered from Focus (Reply or Send answers):** answered items are not in the closures line (their answers came in the prompt).
- **A3 — Already closed by the Session:** a stale click on a closed item changes nothing.
- **A4 — Worker Session:** closures are not emitted to a worker's block.

**Postconditions:** the Session knows what the User closed, exactly once.
**Covers:** FR.23.2, FR.23.8, FR.23.11, FR.23.14, FR.23.15.

---

## UC.FV.08 — A `notify_user` message appears in Focus

**Actors:** Session, User, Tessyn.

**Main flow:**
1. The top-level Session calls `notify_user` with a multi-line message (as the Telegram-only question on 2026-11-02 would have been).
2. `notify_user` sends its Telegram and desktop notifications exactly as before; the capture sends no second notification.
3. Tessyn also posts an item: kind `Ask`, source `NotifyUser`, title = first non-empty line (trimmed, cut to 200), body = the full message; SYSTEM entry `[Attention A12 (ask)] <title>` is written.
4. The result gets ` Pinned as A12.` appended.
5. The item is visible in Focus and in the session file.

**Alternative flows:**
- **A1 — Same message again:** dedupe by title; the result gets ` Already pinned as A12.`; no new item.
- **A2 — Worker Session:** the call is dropped as today; no item.

**Postconditions:** a question sent to Telegram is also readable in the session.
**Covers:** FR.23.7, FR.23.8, FR.23.9.

---

## UC.FV.09 — A Worker Session tries to post and is refused

**Actors:** Worker Session, Tessyn.
**Preconditions:** the caller has `OrchestrationDepth > 0`.

**Main flow:**
1. The Worker calls `post_attention` (or `resolve_attention`).
2. Tessyn returns a normal text result, not an error: `Not posted - worker sessions can't post to the user's Focus view. Report to your supervisor instead.`
3. Nothing is stored, no SYSTEM entry written, no notification sent.

**Alternative flows:**
- **A1 — `list_attention`** by a worker returns `No items.`
- **A2 — `notify_user`** from a worker stays dropped; no item.

**Postconditions:** the ledger of the worker's node is unchanged.
**Covers:** FR.23.6, FR.23.7, FR.23.9.

---

## UC.FV.10 — The badge shows the most severe open kind

**Actors:** User, Tessyn.

**Main flow:**
1. With only `Alert` items open, the badge is transparent with a normal-foreground number.
2. A `Todo` opens: the background becomes the theme's foreground colour, the number the theme's background colour (adapts to dark and light).
3. An `Ask` opens: goldenrod `#DAA520`, black number.
4. A `Critical` opens: dark red `#8B0000`, white number.
5. When the Critical is closed, the badge falls back to the next most severe open kind.

**Alternative flows:**
- **A1 — Decided only:** `Decided` items are not counted; with only Decided open the badge is hidden.
- **A2 — Focus selected:** the badge is hidden while Focus is selected, and returns when Session is selected again.
- **A3 — Count 0:** hidden.

**Postconditions:** the count is open items except Decided; colour follows the top kind.
**Covers:** FR.23.1, FR.23.13, FR.23.16.

---

## UC.FV.11 — Session/Focus switching and the output toolbar

**Actors:** User.

**Main flow:**
1. The header reads `[Search box] [Session][Focus <badge>] [MD] [JSONL]`. Session is checked by default; a newly opened session starts on Session.
2. The User selects Focus: the output area (message list and its search overlay) is replaced by the Focus view; the busy/thinking indicator remains; MD and JSONL are hidden.
3. The User selects Session: the output returns unchanged and MD/JSONL reappear.

**Alternative flows:**
- **A1 — Per-session selection:** switching sessions keeps each session's own selection in memory; restarting the app resets all to Session.
- **A2 — Exactly one checked:** clicking the already checked button leaves it checked.

**Covers:** FR.23.12, FR.23.14.

---

## UC.FV.12 — Enter in search returns to Session

**Actors:** User.
**Preconditions:** Focus is selected.

**Main flow:**
1. The User types a term in "Search output..." and presses Enter.
2. The view switches to Session and then runs the search on the Session output.

**Alternative flows:**
- **A1 — Session selected:** Enter searches as today.
- **A2 — Empty term:** the view still switches to Session.

**Covers:** FR.23.12.

---

## UC.FV.13 — Live refresh keeps unsent reply text

**Actors:** User, Session.
**Preconditions:** Focus is selected; the User has typed text in the reply boxes of `A3` and `A5`.

**Main flow:**
1. While the User types, the Session posts `A9` or closes `A7`.
2. `AttentionChanged(nodeId)` reaches the session view model through `IDaemonClient`; the Focus view and badge refresh on the UI thread.
3. The text in the reply boxes of `A3` and `A5` is still there; the Send answers count is unchanged.

**Alternative flows:**
- **A1 — The item with typed text is closed by the Session:** its card moves to Closed and the unsent text is dropped with it.
- **A2 — Change in another session:** the view does not refresh.

**Covers:** FR.23.13, FR.23.14, FR.23.16.

---

## UC.FV.14 — Guidance is given to a fresh session

**Actors:** Session, Tessyn.

**Main flow:**
1. A new top-level session with agent tools enabled gets its first turn.
2. The hidden instruction block includes `AttentionGuidance` (the Focus view, the five kinds, "post once, refer by id, call `resolve_attention` when answered").
3. On later turns the guidance is absent; only the Pinned list (UC.FV.04) is repeated.
4. The Telegram guidance (when Telegram is enabled) now mentions that `critical` and `notify: true` items notify, and that `notify_user` is also pinned.

**Alternative flows:**
- **A1 — Worker Session or agent tools off:** no guidance.
- **A2 — Orchestration skill:** `cmx-orchestration` tells supervisors to post the ask once with `post_attention` and workers not to call `notify_user` or `post_attention`.

**Covers:** FR.23.10.

---

## UC.FV.15 — The ledger survives restarts, adoption and corruption
**Actors:** Tessyn, User.

**Main flow:**
1. Items are persisted in `%AppData%\TessynDesktop\attention\<NodeId>.json`, written via temp file then rename.
2. After an app restart (or compaction, detach, Cross-Profile Session Adoption) the same NodeId reloads the same items, and `NextNumber` continues without reuse.

**Alternative flows:**
- **A1 — Missing file:** an empty ledger.
- **A2 — Corrupt file:** a warning is logged; the file is moved to `<NodeId>.json.bad-<yyyyMMddHHmmss>`; the ledger starts empty; the bad file is never silently overwritten.
- **A3 — Session deleted:** Tessyn never deletes the ledger file.

**Covers:** FR.23.2, FR.23.3.

---

## UC.FV.16 — The User replies to one item from its card

**Actors:** User, Tessyn, Session.
**Preconditions:** `A3`, `A4` and `A5` (asks) are Open; the main input box holds an unsent draft; `A5` has unsent text in its reply box.

**Main flow:**
1. The User types an answer in `A3`'s reply box. **Reply** on that card becomes enabled (it is disabled while the box is empty).
2. The User clicks **Reply**.
3. Tessyn marks `A3` `Answered` with `Answer` = the text being sent and clears its box, then sends the prompt `Answers from the Focus view:` + `- A3 (<title>): <answer>` through the same interactive send path as the main input box.
4. `A4` and `A5` are untouched: `A5`'s unsent text is still in its box. The main input box and its draft are untouched.
5. `A3` moves to Closed showing `Answered: <answer>`. No SYSTEM entry is written.

**Alternative flows:**
- **A1 — Send answers instead:** Send answers (n) sends every non-empty box in one prompt (UC.FV.05).
- **A2 — Dismiss instead:** the close button (far right of the card, tooltip "closes without sending") closes the item as `Dismissed`, sends nothing and discards the typed text (UC.FV.07).

**Postconditions:** one USER prompt with one answer line; only that item answered.
**Covers:** FR.23.14, FR.23.15.

---

## UC.FV.17 — The User browses closed history

**Actors:** User.
**Preconditions:** several items are closed.

**Main flow:**
1. The User selects Focus. The Closed (n) expander is collapsed, so open items are what is seen when scrolling.
2. The User expands it: compact cards, newest closed first, each with a header row and an outcome line (`Answered: <answer>` / `Resolved: <note>` / `Dismissed`).
3. The User clicks a closed card's header row: its body shows; clicking again hides it.

**Alternative flows:**
- **A1 — Live refresh:** a refresh keeps the expander as the User left it.
- **A2 — Re-selecting Focus** or opening another session: the expander is collapsed again; it is collapsed for every session at start.

**Covers:** FR.23.14.

---

## UC.FV.18 — The User spots sessions that need attention from the tree or the recent dropdown

**Actors:** User, Tessyn.
**Preconditions:** several sessions have open items, one of them with only Decided items open; one session with an open Ask has had no activity for more than 24 hours.

**Main flow:**
1. In the session tree, each of those sessions shows a badge left of its name: the count of all its open items, Decided included, coloured by the most severe open kind.
2. The User opens the top-bar recent-sessions dropdown. Every session with open items is listed, the inactive one included, with the same badge between the status dots and the name and no hide button.
3. The User picks a row. The tree scrolls to that session and opens it. Its Focus badge leaves Decided out, so it can show a lower number than the row badge.
4. The User dismisses the session's last open items from Focus. The row badge disappears at once. The next time the dropdown opens, the session is listed only if it is still pinned (favorite or schedules) or was active within the last 24 hours and is not hidden.

**Alternative flows:**
- **A1 — Decided only:** the row badge is grey with a white number; the Focus badge is hidden.
- **A2 — A session posts while the dropdown is open:** that row's badge updates in place; a session that becomes pinned joins the list the next time the dropdown opens.
- **A3 — Schedules:** a session with a schedule is listed the same way, regardless of recency and hidden state, with the black alarm-clock icon and no hide button.
- **A4 — Favorite with open items:** un-favoriting it removes the star, and no hide button appears while it still has open items.

**Covers:** FR.9.6, FR.9.7, FR.9.8, FR.23.16, FR.23.17.

---

## Out of scope

- A cross-session Attention list in the left panel; a badge on Recent-tab rows.
- Answering by replying to a Telegram ping; a Telegram `/attention` command.
- Local-LLM extraction of items from sessions that don't post.
- Capturing Claude's native `AskUserQuestion`.
- TDCP methods for the ledger.
