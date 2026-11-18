> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.23 — Focus View (Attention Items)

Sessions have no record that the user has seen an ask, so they re-raise it: in the "HSE-150 Android backend" session one ask (the production app's open release switch) was restated 6 times in 50 minutes of scheduled turns, and the Architect finally had to ask "tell me about the urgent issue"; on 2026-11-02 a question went to Telegram only (`notify_user` writes nothing to the session file) and the Architect had to ask "what's the question, can you post here too?". The fix: sessions *post* items to a per-session list that Tessyn keeps (the Attention Ledger), a new **Focus** view shows it beside the unchanged raw output (the **Session** view), and every turn is told which items are already pinned. No extra LLM calls.

**FR.23.1 — Attention Items and kinds:** `AttentionKind`, from most to least severe. Any kind may carry answer options and a recommendation.

| Kind | Meaning | Badge colour (FR.23.13) |
|---|---|---|
| `Critical` | Something the user must know immediately. Posting one always notifies the user (FR.23.9). | DarkRed `#8B0000`, white number |
| `Ask` | The session needs the user's answer, decision or approval. | Goldenrod `#DAA520`, black number |
| `Todo` | Something only the user can do: rotate a password, add a DNS record, merge a PR. | Background = the theme's foreground colour; number in the theme's background colour |
| `Alert` | Worth knowing; no reply needed. | Transparent background; number in the normal foreground colour |
| `Decided` | A call the session made for the user so work isn't blocked; the user may override it. | Not counted in the Focus badge. Counted in the session-row badge (FR.23.17): grey `#707070`, white number, when it is the only open kind |

**FR.23.2 — Item fields, ids and statuses:**
- **Fields:** id, kind, title (one line), body (markdown, self-contained), options, recommendation, key, status, source, answer, resolution note, user-close-unreported flag, created/updated/closed timestamps (`AttentionItemModel`).
- **Ids:** `A{n}` per session. `n` comes from the ledger's `NextNumber`: it starts at 1, increments on every new item and is never reused.
- **`AttentionStatus`:** `Open` — active. `Answered` — the user answered it, either from the Focus view or in conversation, recorded by the session through `resolve_attention`. `Resolved` — done or no longer applies; the session closed it. `Dismissed` — the user closed it from the Focus view without an answer (Dismiss or Got it, FR.23.15), whatever its kind.
- **`AttentionSource`:** `Tool` (`post_attention`) or `NotifyUser` (captured `notify_user`, FR.23.7).
- **Ordering**, used everywhere a list is shown: open items first, by kind severity (Critical, Ask, Todo, Alert, Decided — the enum order), then number ascending; then closed items, newest `ClosedUtc` first. The ordering helper and the summary computation (open count excluding Decided, most severe open kind) live in Core so the UI does not reimplement them.

**FR.23.3 — Attention Ledger storage:**
- **Location:** `%AppData%\TessynDesktop\attention\<NodeId>.json`. Folder name `Constants.Attention.FolderName = "attention"`, built the same way as `McpTokenStore`'s path (`Path.Combine(appData, Constants.AppDataFolderName, …)`). Not in `appsettings.json` (already about 2.6 MB and saved synchronously) and not in any database.
- **Keyed by NodeId**, so it survives compaction, detach and Cross-Profile Session Adoption.
- **Writes:** temp file then rename (atomic, like FR.4.3), serialised per service instance.
- **Loading:** lazily on first use, then cached in memory. A missing file is an empty ledger. An unreadable or corrupt file is logged as a warning, moved aside to `<NodeId>.json.bad-<yyyyMMddHHmmss>`, and the ledger starts empty — never overwritten silently.
- **Never deleted by Tessyn**, the same no-cascade policy as FR.1.7.
- The store takes its root directory in the constructor, so tests use a temp dir. `IAttentionService` is a Singleton (it owns the cache and serialises writes).

**FR.23.4 — `post_attention` tool (cmx MCP server):** Input schema:

```
items   (array, required, 1–30) of objects:
   kind            string, required: critical | ask | todo | alert | decided   (case-insensitive)
   title           string, required: one line (newlines → spaces), trimmed, cut to 200 chars; empty after trim = error
   body            string, optional (default ""): markdown, cut to 20,000 chars
   options         array of string, optional: ≤ 10, each trimmed, empty ones dropped, each cut to 200
   recommendation  string, optional: cut to 1,000
   key             string, optional: stable dedupe key chosen by the session, trimmed, cut to 100
notify  boolean, optional (default false)
```

Behaviour per item:
- **Dedupe:** among **Open** items, an item with the same `key` (case-insensitive, both non-empty) is the match. Failing that, an item with the same normalised title (trimmed, inner whitespace collapsed, case-insensitive) is the match. **Match:** the existing item is updated (kind, title, body, options, recommendation, plus the key if one is given; `UpdatedUtc`); its id doesn't change, and the update is reported as "already pinned". **No match:** a new item `A{NextNumber}` is created.
- **Validation:** an invalid kind, an empty title or an empty `items` array is a tool error (`McpToolException`) naming the problem. Nothing is posted if any item is invalid.
- **Session file:** each created or updated item gets a SYSTEM entry (FR.23.8).
- **Notification:** at most one per call. It is sent when at least one item was **created** and either a created item is `Critical` or `notify` is true. Updates never notify (delivery: FR.23.9).

Tool description (this wording):

> Pin items in the user's Focus view, next to this session's output, where they stay visible until resolved. Kinds: critical (the user must know immediately — always notifies them), ask (you need their answer or approval), todo (something only they can do), alert (worth knowing, no reply needed), decided (a call you made for them that they may override). Write each body so it stands on its own. Post an item once: re-posting an open item with the same key or title updates it instead of adding a copy. Don't restate pinned items in later replies — refer to them by id (A1, A2, …). Set notify=true to also ping the user's Telegram and desktop, only when they must act soon. Top-level sessions only — a worker's call is refused; report to your supervisor instead.

Result text, one line per item, in order:
- `A12 posted (critical).`
- `A7 already pinned (ask) - updated, not re-posted.`

Then, only if a notification was attempted: `Notified: Telegram, desktop.` (the channels that delivered) or `Notification not sent - neither Telegram nor desktop notifications are enabled.`
Then always: `Pinned items stay in the user's Focus view until resolved - don't restate them; refer to them by id.`

**FR.23.5 — `resolve_attention` and `list_attention` tools:**

`resolve_attention` — input: `id` (string, required, case-insensitive, e.g. `a12`); `status` (string, required: `answered` | `resolved`); `note` (string, optional, cut to 2,000). `answered`: the user answered it in conversation; sets `Answer` = note. `resolved`: done, withdrawn or no longer applies; sets `ResolutionNote` = note. Writes a SYSTEM entry (FR.23.8). Results:
- `A12 marked answered.` or `A12 resolved.`
- If already closed: `A12 is already <status lower-case>.` (no change, no SYSTEM entry).
- Unknown id: tool error `Unknown attention item 'A99'.`

Description:

> Close a pinned Focus item: status=answered when the user answered it in conversation (note = their answer), status=resolved when it is done or no longer applies (note = why).

`list_attention` — input: `status` (string, optional: `open` (default) | `all`). Output is plain text, one line per item, ordered as in FR.23.2:
- open item: `A12 [critical, open] <title>`
- closed item: `A3 [ask, answered] <title> - answer: <answer>`, `A4 [todo, resolved] <title> - <note>`, or `A5 [alert, dismissed] <title>`
- empty: `No open items.` (open) or `No items.` (all)

Description:

> List this session's Focus items — open (default) or all — with ids, kinds, statuses and answers.

**FR.23.6 — Worker Sessions refused:** A Worker Session (`OrchestrationDepth > 0`) calling `post_attention` or `resolve_attention` gets a normal text result (not an error), and nothing is stored or written:

`Not posted - worker sessions can't post to the user's Focus view. Report to your supervisor instead.`

This matches the `notify_user` policy (FR.21.19). `list_attention` returns `No items.` for a worker.

**FR.23.7 — `notify_user` capture:**
- Every `notify_user` call from a top-level session also posts an item: kind `Ask`, source `NotifyUser`. Its title is the message's first non-empty line (trimmed, cut to 200); its body is the full message. The same dedupe and SYSTEM entry as `post_attention` apply.
- The capture never sends a second notification: `notify_user` keeps its own Telegram and desktop fan-out unchanged (FR.21.3, FR.21.5).
- Its result text gets ` Pinned as A12.` appended, or ` Already pinned as A12.` on a dedupe update.
- Worker calls stay dropped exactly as today (FR.21.19): no item.

**FR.23.8 — Session-file records:**
- **SYSTEM entries.** The service appends these through `ISessionFileService.AppendMessage` with `Constants.SessionFile.RoleSystem`, the way `workflow_phase` does. ASCII only.
  - Created: first line `[Attention A12 (critical)] <title>`, then the body (if any) on the following lines, then `Options: <o1> | <o2>` (if any), then `Recommendation: <text>` (if any).
  - Updated: the same layout, with first line `[Attention A12 (ask) updated] <title>`.
  - Session close: `[Attention A12 answered] <note>` or `[Attention A12 resolved] <note>`. With an empty note, there is no trailing space.
  - User close from the Focus view, any kind: `[Attention A12 dismissed by the user]`.
  - User answers from the Focus view write **no** SYSTEM entry: the USER prompt (FR.23.15) is the record.
- **Tool lines.** `ClaudeSessionImportService.ExtractToolDescription` (the JSONL → `[Tool: …]` line) learns three new input shapes: `message` is added to the generic string fallback list, so the `notify_user` line shows its text, cut to 120 like the others; an `items` array of objects with `title` → `<n> item(s): <title1>; <title2>`, cut to 120; `id` + `status` strings → `<id> <status>`.

**FR.23.9 — Notifications:**
- When FR.23.4 calls for a notification, it goes through exactly the fan-out `notify_user` uses (FR.21.5): Telegram `NotifyAsync` (mention and sound) plus the desktop toast, each gated as today (`TelegramEnabled`, `NotificationsEnabled`). That fan-out is extracted into one private helper shared by both tools; it is not duplicated.
- Message text: one created item: `A12 (critical): <title>` + blank line + body; several created items: `<n> new items in the Focus view:` then one `- A12 (critical): <title>` line each.
- Worker Sessions never notify (FR.23.6).
- Independently of the notification, every **created** item plays the Focus item sound (FR.22.1; one per burst, per-session switch); updates and closes are silent.

**FR.23.10 — Guidance (hidden instruction block, once per fresh session):**
- New `Constants.Instructions.AttentionGuidance`, emitted when `IsAgentToolsEnabled && IsNewSession && !IsWorkerSession`. It is session policy, so it uses the same gate as `NoWorkflowTool`. The `mcp__cmx__` prefix is built from `Agent.McpServerName`, as the existing constants do. Text:

  > The user has a Focus view beside this session's output. Pin anything they need there with `mcp__cmx__post_attention`: `critical` — they must know immediately (it notifies them at once); `ask` — you need their answer, decision or approval; `todo` — something only they can do; `alert` — worth knowing, no reply needed; `decided` — a call you made for them so work isn't blocked, which they may want to override. Write each item so it stands on its own: the user may read it days later without this conversation. Post each item once — it stays pinned until resolved, so don't restate pinned items in later replies; refer to them by id. When the user answers an item in conversation, or it no longer applies, call `mcp__cmx__resolve_attention`.

- **Amends `TelegramNotifyGuidance` (FR.21.3).** Gate unchanged. New text:

  > A Telegram bridge is available: `critical` items, and items you post with `notify: true`, also ping the user's Telegram and desktop. Use `notify: true` only when the user must act soon — never for progress updates — and only from this top-level session, never from a worker or Task-tool subagent (report back to your supervisor instead). `mcp__cmx__notify_user` still sends a plain message and is also pinned as an ask, but prefer `post_attention`. There is no blocking "ask and wait" tool — after posting an ask, end your turn normally instead of waiting idle for a reply. The user's reply arrives later as an ordinary new prompt in this same session.

- **Amends `ScheduledTurnNotifyReminder` (FR.21.24).** Gate unchanged. New text:

  > The scheduler started this turn, so the user may not be watching and is not told when it finishes. If — and only if — your reply needs the user to decide or act and it isn't pinned yet, post it with `mcp__cmx__post_attention` (`notify: true` if they must act soon). Don't restate items that are already pinned.

- **Amends the `cmx-orchestration` skill (`Constants.Agent.OrchestrationSkillMarkdown`, FR.15.11).** Supervisor section: where it says to call `notify_user` once when the user must decide or act, it says instead to post the ask once with `post_attention` (`notify: true` when they must act soon); it stays pinned, so don't restate it in later delivery turns. Worker section: don't call `notify_user` **or** `post_attention`; report to the supervisor.

**FR.23.11 — Pinned list (hidden instruction block, every turn):**
- **Gate:** emitted when `IsAgentToolsEnabled && !IsWorkerSession` and the session has open items **or** unreported user closures. It describes this turn's state, so it is **not** gated on `IsNewSession`, like FR.21.24.
- Exact lines appended to the block, after the existing lines. `<…>` are placeholders. The two-space indent on the item lines is part of the format.

```
- Pinned in the user's Focus view - they can see these, so don't restate them; refer to them by id, and call `mcp__cmx__resolve_attention` when one is answered in conversation or no longer applies:
  - A12 critical: <title>
  - A3 ask: <title>
- Closed by the user in the Focus view since your last turn: A5 todo "<title>" dismissed without an answer; A4 ask "<title>" dismissed without an answer.
```

- **Ordering and caps:** severity (Critical, Ask, Todo, Alert, Decided), then number ascending. Decided items are included: the session must not re-announce them either. At most 20 item lines; when more are open, add `  - ...and <n> more (call mcp__cmx__list_attention)`. Titles are cut to 120 chars plus `...`. The first line appears only when there are open items; the "Closed by the user" line only when there are unreported closures.
- **Closure wording:** every user-closed item, whatever its kind, is `dismissed without an answer`. Items the user answered from the Focus view (Reply or Send answers) are not listed: their answers arrive in the prompt itself.
- **Plumbing:** `InstructionOptionsModel` gains two optional parameters, `IReadOnlyList<AttentionItemModel>? PinnedAttentionItems = null` and `IReadOnlyList<AttentionItemModel>? UserClosedAttentionItems = null`, filled by all three call sites: `SessionTurnService.RunTurnAsync` ×2 and `SessionPromptComposer`. `IAttentionService.TakeUnreportedUserClosures(nodeId)` returns those items and clears their flag, so each closure is reported once.

**FR.23.12 — Session/Focus switch:**
- **Placement.** In the session view header (`SessionView.axaml` Row 0, the right-docked `DockPanel` group), immediately **right of the "Search output..." box**. Two toggle buttons labelled **Session** and **Focus** act as one segmented switch: exactly one is checked. Left to right: `[Search box] [Session][Focus <badge>] [MD] [JSONL]`. In the DockPanel, declare JSONL, MD, the switch, then the search box. Style them like the MD/JSONL toggles (`Padding="8,3"`, `FontSize="11"`).
- **Session** (default) shows today's output, unchanged.
- **Focus** replaces the output area (the message `ScrollViewer` and its search overlay) with the Focus view. The thinking/busy indicator stays visible. The **MD and JSONL toggles are hidden**.
- **Search.** Pressing Enter in the search box while Focus is selected switches to Session and then runs the search. Search always searches the Session output.
- **Selection** is kept per session in memory (on the session's view model) and is **not persisted**. A newly opened session starts on Session.

**FR.23.13 — Focus badge:**
- **Visible** only while **Session** is selected and the count is > 0.
- **Count:** open items whose kind is not `Decided`.
- **Colour:** the background is set by the most severe open kind among the counted items (Critical > Ask > Todo > Alert), using the colours in FR.23.1. Todo uses the theme's foreground colour as background, with the number in the theme's background colour, so it adapts to dark and light themes. Hex colours live in `Constants` (no magic strings).
- **Shape:** a small rounded rectangle (corner radius 3, `Constants.Focus.BadgeCornerRadius`), not a circle, next to the "Focus" text (font about 10–11, minimum width about 16). The kind chips on cards (FR.23.14) keep their pill shape.
- **Updates** live as items change (FR.23.16).

**FR.23.14 — Focus view layout:** Top to bottom:
1. A **"Send answers (n)"** button, enabled when n ≥ 1 reply boxes hold text and the session can send (the same rule as the main Send button, which stays enabled during a turn because concurrent sends are allowed).
2. **Open items** (Critical, Ask, Todo, Alert), as cards ordered as in FR.23.2.
3. A collapsed **"Decisions (n)"** expander with open Decided cards. Shown only when n > 0.
4. A **"Closed (n)"** expander with closed items, newest closed first, **collapsed by default** so that what is open is what the user sees when scrolling. It starts collapsed for every session, and collapses again each time Focus is selected; expanding it shows the history and it stays expanded while the user stays on Focus (refreshes don't collapse it). Closed cards are **compact**: only the header row and the outcome line (`Answered: <answer>` / `Resolved: <note>` / `Dismissed`). Clicking a closed card's header row shows or hides its body; closed cards start with the body hidden.
5. **Empty state**, when no item other than Decided is open: `Nothing needs your attention in this session.` The Decisions and Closed expanders still show if they have items.

> **Amended 2026-11-17:** item 1 (Send answers) moved to the session header (FR.23.21); the WPF Closed history, Decisions expander and empty state are built (FR.23.23).

Card contents:
- Header row: a kind chip coloured as in FR.23.1 (Decided muted grey), the id, the title (SemiBold) and the posted time (local; `HH:mm` today, otherwise `yyyy-MM-dd HH:mm`).
- The body, rendered as markdown with the existing `MarkdownView` and selectable.
- The options as small buttons.
- "Use recommendation: <text>" (when a recommendation exists).
- A multi-line reply box.
- An action row under the reply box: **Reply** on the left, enabled when the card's reply box holds text, and the close button on the **far right**, away from Reply: **Dismiss** for Ask and Todo, **Got it** for Critical, Alert and Decided. The close button's tooltip says it closes the item without sending anything. (An earlier build labelled the Todo close button **Done**, which read as "send my reply"; it never sent the reply.)

Behaviour: clicking an option puts the option text in the card's reply box (replacing it); the user can edit it before sending. Clicking the recommendation puts `Accepted your recommendation: <recommendation>` there.

**FR.23.15 — Answering and closing from the Focus view:**
- **"Send answers"** builds **one** prompt from every non-empty reply box, ordered as the cards are, with `AttentionPromptBuilder.BuildAnswersPrompt`. Exact format:

  ```
  Answers from the Focus view:
  - A3 (<title>): <answer>
  - A5 (<title>): <answer>
  ```

  Answers keep their line breaks. A multi-line answer's continuation lines are indented by two spaces.
- It is sent through the **same interactive send path as the main input box** (`TurnController`), so it uses the same model, effort, profile and instruction block. It must **not** clear the input box or delete the draft: `SendAsyncCore` currently invokes `InputCleared` and `_draftService.DeleteDraft` unconditionally, so an option is added to skip both for this caller.
- It first marks those items `Answered`, with `Answer` = the text being sent (`IDaemonClient.MarkAttentionAnswered`), clears the sent reply boxes, and then sends. Marking first matters: the send builds the hidden instruction block synchronously, and items still open at that moment would be listed as pinned in the very turn that carries their answers.
- **Reply** (per card) does the same for that card alone: the same prompt format with a single line, the same send path, the same mark-first order. Other cards' unsent text is untouched.
- **Close button** (Dismiss / Got it): `IDaemonClient.CloseAttentionByUser(nodeId, itemId)`. The item becomes `Dismissed`, whatever its kind. Sets `IsUserCloseUnreported = true` and writes the SYSTEM entry (FR.23.8). Sends no prompt; text typed in that card's reply box is discarded, which is why the close button sits apart from Reply.

**FR.23.16 — Live updates and daemon boundary:**
- `IAttentionService` raises `AttentionChanged(nodeId)` after every change. `InProcDaemonClient` relays it as `IDaemonClient.AttentionChanged`, the same way it relays `EntryAppended`.
- The session view model subscribes and refreshes the Focus view and badge for its own node on the UI thread (`IUiDispatcher`). Unsent reply-box text survives refreshes (kept by item id).
- The session tree view model subscribes too and refreshes the session-row badge (FR.23.17) of the changed node on the UI thread.
- The UI reaches the ledger only through `IDaemonClient` (`GetAttentionItems`, `GetAttentionSummary`, `MarkAttentionAnswered`, `CloseAttentionByUser`, `AttentionChanged`), never through `IAttentionService` directly. This keeps the UI a daemon client, as in FR.21.10.

**FR.23.17 — Session-row badge:**
- **Where:** on session rows in the session tree (FR.1), immediately left of the session name, after the status dots and the sub-session bar; and on rows of the top-bar recent-sessions dropdown (FR.9.8), between the status dots and the name. Not on Recent-tab rows, and not in the dropdown's closed box.
- **Count:** every open item, `Decided` included. The Focus badge (FR.23.13) leaves Decided out, so the two numbers can differ.
- **Visible** whenever the count is > 0, whether Session or Focus is selected.
- **Colour:** set by the most severe open kind (Critical > Ask > Todo > Alert > Decided), using the FR.23.1 colours. With only Decided items open: grey `#707070` (`Constants.Focus.ChipDecidedBackground`) with a white number.
- **Shape:** the same rounded rectangle as the Focus badge (FR.23.13).
- **Data:** `AttentionSummaryModel.AllOpenCount` and `AllOpenTopKind`, from `IDaemonClient.GetAttentionSummary`. Loaded for every session at startup and refreshed for the changed node on `AttentionChanged` (FR.23.16).
- **Recent dropdown:** a session with open items is a Pinned Session there (FR.9.8).

**FR.23.18 — References on items (amendment, built 2026-11-15, Focus references step 1):** Design: `docs/features/focus-references-attachments-previews.md`. Implementation report: `.wpf-work/230-focus-refs-core.md`.
- **Refs.** `post_attention` items take an optional `refs` array (at most 10 per item). Kinds: `entry` (`quote` exact text from an entry, optional `session` default = the caller, `occurrence` `last` default or `first`), `file` (`path`, absolute or relative to the session's working directory, must be an existing regular file), `url` (http/https, at most 2000 characters). Every ref may carry a `label` (at most 100 characters). `attention.list` items return the resolved refs.
- **Quote resolution.** Done when the item is posted, against the content store (entry ids, see `docs/refactoring/tdcp-spec.md` "Local client additions"). The search runs from the newest entry (or the oldest) in pages; whitespace runs are collapsed and the match is case-sensitive; the quote must occur exactly once in the entry it lands on. SYSTEM and COMPACTION entries are skipped. The resolved ref stores `nodeId`, `fileId`, `entryId`, `offset` and `length` (characters of the entry content as the API serves it). A ref that cannot be resolved fails the whole call with a message naming it, and nothing is pinned. Re-posting an open item with refs replaces its refs; re-posting without refs keeps them.
- **Jump to an entry.** `session.entries` accepts `aroundEntryId` and returns the page centred on that entry, so a client can open the session at the referenced entry and highlight `offset`/`length`. Back navigation is client state.
- **File handles.** A file ref gets an opaque `refId` (128-bit random) that survives restarts. Clients never send a raw path to read a file: `file.read { handle, offset?, length? }` takes a `refId`, or a preview handle from `file.preview { nodeId, entryId, path }` (the path must occur in that entry's text; the handle lives 1 hour). Responses carry no absolute path; `attention.list` gives only the path relative to the working directory, when the file lies inside it.
- **FileAccess policy** (`AppSettings.FileAccess`). Denied paths are always refused, on every transport (credential and key folders and files, token stores, `.dat` files, the data root's settings and token files). The relay (web) additionally applies a type allowlist (text, images, video, pdf), per-type size caps and a chunk cap (`MaxRelayChunkBytes`, 512 KB), and logs every read. The relay `Mode` is `WorkingDirectory` (default since decision A11: files referenced by a Focus item of that session, attachments of that session (FR.31.8) and any file inside the session's working directory) or the stricter `ReferencedOnly` (referenced files and attachments only). A one-time migration (decision A16, `FileAccess.ModeRevision`) moves a stored `ReferencedOnly` that was only the old default to `WorkingDirectory`. A settings file that already carries the current revision keeps its value, and so does a stored `WorkingDirectory`. No TOTP. Loopback clients (desktop) may read any non-denied file a handle points to.
- **Implemented in WPF (2026-11-15, merge `dc2493b`; report `.wpf-work/340-wpf-focus-previews.md`, fixes 340b and 340c).**
  - **Focus view.** A column beside the transcript lists the open items of the selected session, newest first. A header button "Focus (n)" shows or hides it; it is resizable and remembers its width. A card shows the kind chip (Critical, Ask, Todo, Alert, Decided), the id, the title, the markdown body, the references, the option buttons, a "Recommended" button, a reply box, Reply and Dismiss ("Dismiss" for Ask and Todo, "Got it" for the others). "Send answers (n)" sends every non-empty reply box in one `attention.reply`. The view updates live (`attention.changed`, resync); the card being typed in keeps its focus and text, and unsent text is kept per session and item. Bodies are native elements built from the markdown tree; raw HTML is shown as text and images show as "[image]".
  - **Entry reference.** Opens the target session at the entry (`aroundEntryId`) and highlights the quote (or outlines the entry when the quote cannot be found in the rendered text). A Back button returns across sessions (client stack of 50). A plain tree selection starts a new trail and closes the preview. An entry that no longer exists gives a dismissible notice and changes nothing. After a jump the page loads newer entries on demand, one page per request; a failed request shows "Could not load newer entries" with a Retry button and is never retried by itself.
  - **File reference and preview pane.** Opens the preview pane with the `refId` handle. It reads in 512 KB chunks with progress and Cancel. Limits: text and markdown 2 MB (the first 2 MB are shown, with a note), image 25 MB, video 500 MB, PDF 100 MB; above the limit a message and no content. Markdown is rendered (raw HTML off, http(s) links only, images as alt text) with a Source toggle. Text, code, svg, html and scripts are shown only as text. Images and video play from the preview cache. A PDF is not downloaded for the preview; "Open in default app" fetches it. The pane runs in an in-private WebView2 controller with its own strict policy.
  - **"Open in default app"** is offered only for md, markdown, txt, png, jpg, jpeg, gif, webp, bmp, mp4, webm, mov, m4v, ogv and pdf, and only for a file the client itself wrote to its preview cache under the fixed name `content.<ext>`. Names with control or spoofing characters, non-ASCII extensions, trailing dots, alternate data streams and reparse points are refused. Executables and scripts are never launched.
  - **Url reference.** Opens through the URL policy (http and https only); anything else gives a notice.
- **Implemented on the web (2026-11-15, merge `b59b14f`, live since the A19 deploy; reports `.wpf-work/290-web-refs-ui.md` and `290b-web-refs-fixes.md`).** Focus cards list their refs. An entry ref loads `session.entries { aroundEntryId, limit: 40 }`, scrolls to the entry and marks the quote (the CSS Custom Highlight API for rendered entries, exact offsets for user entries). The window is anchored: live appends are not fetched, "Load later" and "Jump to latest" appear, and sending from an anchored window reloads the tail first. Back is a stack of 40 (across sessions) with a "Back to <session>" bar. A file ref opens the preview sheet (a dialog with a focus trap; `file.read` chunks with progress; limits text and markdown 2 MB, image 30 MB, video 60 MB, PDF 50 MB; markdown sanitised, svg and html only as text; the blob type follows the file kind and the daemon's mime; a PDF needs a click to load). A url ref is a link (`rel=noopener noreferrer`).
- **Web contract.** `file.read`, `file.preview`, `session.entries` with `aroundEntryId` and `attachment.list` are open on the relay; the web contract is 24 methods (`docs/refactoring/tdcp-spec.md` section 9.8). The FR.25 text is not amended by this change.
- **Attachment handles** (FR.31.5) are built.
- **Not built yet.** In WPF: the Closed history and the collapsed Decisions expander of FR.23.14 (Decided items show in the single list, styled grey), a cross-session Focus list, a Settings screen for the media allowlist, The WPF attach UI (FR.31.2) is built.

**FR.23.19 — Reply drafts (kept across restarts):** The unsent text in a Focus card's reply box is a draft, kept like the session's composer draft, so closing the app does not lose it.
- **Key.** One draft per (session, item id). The item id is the Attention Item id (FR.23.2, e.g. `a12`, compared case-insensitively). A session's composer draft and its reply drafts are independent; none overwrites another.
- **Storage.** The daemon keeps reply drafts in the drafts store, next to the composer drafts, one file per (session, item). No database or ledger schema changes. Empty text means no draft (the file is removed). A reply draft is never part of the item (FR.23.2) and never appears in the session file.
- **Save.** Typing in a reply box saves the text on change, debounced (about 500 ms), and every pending save is flushed when the app closes, through the same close path as the composer drafts.
- **Restore.** When a card shows again, including after a restart, its reply box holds the saved text. A change made by another client reaches this one through `draft.changed` and follows the same conflict rule as the composer draft.
- **Delete.** The reply draft is deleted when its reply is sent (Reply, or "Send answers" for every box it sent, FR.23.15), when the item closes (`Answered`, `Resolved` or `Dismissed`, from any client or from the session through `resolve_attention`), and when the session is deleted, as composer drafts are. A draft whose item no longer exists or is no longer `Open` is removed when the session's items are loaded.
- **Remote API (additive).** `draft.get`, `draft.set` and `draft.delete` take an optional `itemId`. Without it they address the composer draft, exactly as before, so older callers are unchanged. With it they address that item's reply draft; an unknown or closed item is rejected with `-32602`. `draft.changed` carries `itemId` only for a reply draft. The methods, the event and the `drafts` capability follow the composer drafts' exposure (loopback and the local stream; not relayed to the web). Revisions are computed as for the composer draft.
- **Unchanged.** "Send answers" and Reply do not touch the composer draft (FR.23.15). A session's `hasDraft` marker (FR.1) reflects the composer draft only.
- **Clients.** The WPF client keeps reply drafts through the Remote API: it writes each box with `draft.set` and its `itemId` (debounced, flushed when the app closes), reads it with `draft.get` when a card shows, follows `draft.changed` events that carry an `itemId`, and treats `-32602` as "the item is closed" (the draft is dropped, not retried). A client that keeps the composer draft ignores `draft.changed` events that carry an `itemId`.

**FR.23.19 coverage:** use cases [UC.FD.01](/docs/usecases/UC.FD.01-Focus-Reply-Drafts.md), test cases [TC.FD.01](/docs/testcases/TC.FD.01-Focus-Reply-Drafts.md).

**Out of scope for FR.23:**
- A cross-session Attention list in the left panel, and a badge on Recent-tab rows (tree and recent-dropdown rows have one, FR.23.17).
- Answering by replying to a Telegram ping, and a Telegram `/attention` command.
- Local-LLM extraction of items from sessions that don't post.
- Capturing Claude's native `AskUserQuestion` as items.
- TDCP methods for the ledger.

---

---

## Amendments and additions (batch 2026-11-17)

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| **FR.23.14** item 1 | FR.23.21 | **"Send answers (n)" moves out of the Focus list into the session header.** The list now starts with Open items; items renumber (Open 1, Decisions 2, Closed 3, Empty state 4). |
| FR.23.12 Placement | FR.23.21 | Send answers is shown only in Focus; in the WPF header it is left of the Session/Focus switch (its find bar is a separate row). |
| **FR.23.15** | FR.23.22 | Adds Ctrl+Enter. |
| FR.23.14 items 3–5, "Not built yet" status line | FR.23.23 | The WPF Closed history, the collapsed Decisions expander and the empty state are **built** (WPF, 2026-11-17); the "Not built yet" sentence for them is deleted. Still not built in WPF: a cross-session Focus list, the media-allowlist Settings screen. |
| FR.2.3.2 | FR.23.20 | Text selectability reaches the Focus cards. |

### New requirements

**FR.23.20 — Selectable Focus text, open and closed.** On every Focus card, **open, Decided and closed**, all rendered text is **selectable and copyable**: the id, the title, the body, the outcome line (`Answered: …`), the options' text and the reply box. A press that ends a selection does not trigger a card action. On a closed card the click that **expands the body** is on the header row; a text drag inside the body never collapses it.

**FR.23.21 — "Send answers (n)" in the header.** The button lives in the **session header**, visible **only while Focus is selected** (hidden in Session), always visible (it never scrolls with the list). It is enabled when n ≥ 1 reply boxes hold text and the session can send (the rule of the main Send button). WPF: left of the Session/Focus switch. Its UI Automation name stays **"Send answers"**.

**FR.23.22 — Ctrl+Enter sends the answers.** **Ctrl+Enter in any card's reply box** (including Decisions cards) does exactly what the button does: the same command, the same mark-first order, the same enabling rule (nothing happens, and no newline is inserted, when n = 0). **Plain Enter inserts a newline**; Shift, Alt and Ctrl+Shift chords are not shortcuts.

**FR.23.23 — Closed, Decisions and the empty state in WPF.**
- *Decisions (n)* is a collapsed expander, shown only when n > 0, holding open Decided cards (full, answerable).
- *Closed (n)* is a collapsed expander, newest closed first (`ClosedUtc`, then `UpdatedUtc`, then number). Cards are compact: header row (kind chip, id, title, posted time) and the outcome line; the body is hidden until the header row is clicked. Body toggles survive refreshes.
- Both expanders **collapse for every session** and **each time Focus is selected**; a refresh leaves them alone.
- The **empty state** shows when no item other than Decided is open (`Nothing needs your attention in this session.`); the expanders still show.
- Header rows of open cards also show the **posted time**.
