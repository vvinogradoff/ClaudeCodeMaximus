# TC.FV.01 — Focus view and the Attention Ledger

Related requirements: FR.23.1–FR.23.17 and FR.9.8 in [requirements.md](/docs/requirements.md). Related use cases: [UC.FV.01](/docs/usecases/UC.FV.01-Focus-View.md).

**Legend.** AUTOMATED = a Core integration test in `code/TessynDesktop.Tests` (xUnit); the intended test class is named in each case. MANUAL = a UI test in Avalonia (the UI has no automated tests). Notification tests use fake Telegram and desktop notifiers; the server is built the way `AgentMcpServerNotifyUserTests` builds it. File-based tests use a temp-dir root. Exact strings below are the expected results; assert them verbatim.

Notation: `<t>` = title, `<b>` = body.

## Summary

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.FV.01 | Ledger store | AUTOMATED | UC.FV.15 | FR.23.3 |
| TC.FV.02 | Ids and NextNumber | AUTOMATED | UC.FV.01 | FR.23.2 |
| TC.FV.03 | Dedupe | AUTOMATED | UC.FV.03 | FR.23.4 |
| TC.FV.04 | Session resolve | AUTOMATED | UC.FV.06 | FR.23.5 |
| TC.FV.05 | `post_attention` validation and limits | AUTOMATED | UC.FV.01 | FR.23.4 |
| TC.FV.06 | `post_attention` results and SYSTEM entries | AUTOMATED | UC.FV.01, UC.FV.03 | FR.23.4, FR.23.8 |
| TC.FV.07 | Notification rules | AUTOMATED | UC.FV.02 | FR.23.4, FR.23.9 |
| TC.FV.08 | Worker refusal | AUTOMATED | UC.FV.09 | FR.23.6 |
| TC.FV.09 | `resolve_attention` / `list_attention` texts | AUTOMATED | UC.FV.06 | FR.23.5, FR.23.8 |
| TC.FV.10 | `notify_user` capture | AUTOMATED | UC.FV.08 | FR.23.7 |
| TC.FV.11 | User close and unreported closures | AUTOMATED | UC.FV.07 | FR.23.2, FR.23.8, FR.23.11, FR.23.15 |
| TC.FV.12 | Summary | AUTOMATED | UC.FV.10 | FR.23.1, FR.23.13 |
| TC.FV.13 | `AttentionChanged` | AUTOMATED | UC.FV.13 | FR.23.16 |
| TC.FV.14 | Guidance gate | AUTOMATED | UC.FV.14 | FR.23.10 |
| TC.FV.15 | Pinned list | AUTOMATED | UC.FV.04, UC.FV.07 | FR.23.11 |
| TC.FV.16 | Amended guidance texts | AUTOMATED | UC.FV.14, UC.FV.04 | FR.23.10 |
| TC.FV.17 | `ExtractToolDescription` | AUTOMATED | UC.FV.01 | FR.23.8 |
| TC.FV.18 | `AttentionPromptBuilder` | AUTOMATED | UC.FV.05 | FR.23.15 |
| TC.FV.19 | `InProcDaemonClient` | AUTOMATED | UC.FV.05, UC.FV.13 | FR.23.15, FR.23.16 |
| TC.FV.20 | Switch placement and default | MANUAL | UC.FV.11 | FR.23.12 |
| TC.FV.21 | Focus hides MD/JSONL, keeps busy indicator | MANUAL | UC.FV.11 | FR.23.12 |
| TC.FV.22 | Enter in search returns to Session | MANUAL | UC.FV.12 | FR.23.12 |
| TC.FV.23 | Badge colour per top kind | MANUAL | UC.FV.10 | FR.23.1, FR.23.13 |
| TC.FV.24 | Badge count and visibility | MANUAL | UC.FV.10 | FR.23.13 |
| TC.FV.25 | Focus layout | MANUAL | UC.FV.01 | FR.23.14 |
| TC.FV.26 | Options and recommendation | MANUAL | UC.FV.05 | FR.23.14 |
| TC.FV.27 | Send answers | MANUAL | UC.FV.05 | FR.23.15 |
| TC.FV.28 | Close buttons | MANUAL | UC.FV.07 | FR.23.14, FR.23.15 |
| TC.FV.29 | Live refresh keeps reply text | MANUAL | UC.FV.13 | FR.23.16 |
| TC.FV.30 | Selection per session, not persisted | MANUAL | UC.FV.11 | FR.23.12 |
| TC.FV.31 | End to end: repeated ask in scheduled turns | MANUAL | UC.FV.01–UC.FV.07 | FR.23.4, FR.23.10, FR.23.11 |
| TC.FV.32 | Single-answer mark-first (Reply core) | AUTOMATED | UC.FV.16 | FR.23.11, FR.23.15 |
| TC.FV.33 | Per-card Reply | MANUAL | UC.FV.16 | FR.23.14, FR.23.15 |
| TC.FV.34 | Dismiss placement, tooltip and discarded text | MANUAL | UC.FV.07, UC.FV.16 | FR.23.14, FR.23.15 |
| TC.FV.35 | Closed history collapsed and compact | MANUAL | UC.FV.17 | FR.23.14 |
| TC.FV.36 | All-open summary (session-row badge inputs) | AUTOMATED | UC.FV.18 | FR.23.17 |
| TC.FV.37 | Recent-dropdown membership rule | AUTOMATED | UC.FV.18 | FR.9.8 |
| TC.FV.38 | Session-row badge in the tree | MANUAL | UC.FV.18 | FR.23.16, FR.23.17 |
| TC.FV.39 | Recent dropdown: Pinned Sessions and row badge | MANUAL | UC.FV.18 | FR.9.6, FR.9.7, FR.9.8, FR.23.17 |
| TC.FV.40 | Badge shape | MANUAL | UC.FV.10, UC.FV.18 | FR.23.13, FR.23.17 |

---

## Automated tests

### TC.FV.01 — `AttentionLedgerStoreTests` — FR.23.3 (UC.FV.15)

- **TC.FV.01-1 Round trip:** save a ledger (`NextNumber = 4`, three items with every field set including `Options`, `Recommendation`, `Key`, `Answer`, `ClosedUtc`), load with a fresh store on the same root: equal.
- **TC.FV.01-2 Missing file:** load for an unknown NodeId returns an empty ledger (`NextNumber = 1`, no items), `NodeId` set; no file is created by the load.
- **TC.FV.01-3 Corrupt file:** a file containing `{ not json` → load returns an empty ledger; the original is moved to `<NodeId>.json.bad-<yyyyMMddHHmmss>` (matches the pattern; content unchanged); a warning is logged; the next save writes a new `<NodeId>.json` and does not touch the `.bad-*` file.
- **TC.FV.01-4 Atomic write:** after a save the folder holds only `<NodeId>.json` (no `.tmp` leftover); if the rename step fails (target locked or temp file injected as a directory) the previous `<NodeId>.json` still loads intact.
- **TC.FV.01-5 Temp-dir root and keying:** the store uses the root from its constructor and creates the folder when missing; two NodeIds produce two files and do not see each other's items.
- **TC.FV.01-6 Serialised writes:** parallel saves for the same node (e.g. 20 concurrent posts through the service) leave a valid file with 20 distinct items.

### TC.FV.02 — `AttentionServiceTests` (ids) — FR.23.2 (UC.FV.01)

- **TC.FV.02-1:** three posts → ids `A1`, `A2`, `A3`, `Number` 1, 2, 3, status `Open`; `NextNumber == 4` in the file.
- **TC.FV.02-2 Never reused:** close `A3` (any way), post a new item → `A4`, not `A3`.
- **TC.FV.02-3 Persisted:** a new service instance over the same root continues at the persisted `NextNumber`.
- **TC.FV.02-4 Per node:** two NodeIds each start at `A1`.
- **TC.FV.02-5 Ordering:** with items Alert A1, Critical A2, Ask A3, Todo A4, Ask A5, Decided A6, plus closed A7 (closed earlier) and A8 (closed later): `GetItems` returns A2, A3, A5, A4, A1, A6, then A8, A7.

### TC.FV.03 — `AttentionServiceTests` (dedupe) — FR.23.4 (UC.FV.03)

- **TC.FV.03-1 By key:** post key `K` then post key `k` with a different title → one item; id unchanged; kind, title, body, options, recommendation updated; `UpdatedUtc` advanced; `CreatedUtc` unchanged; `NextNumber` unchanged.
- **TC.FV.03-2 By normalised title:** `"  Deploy   switch IS open "` matches `"deploy switch is open"` (trim, inner whitespace collapsed, case-insensitive) when no key is given.
- **TC.FV.03-3 Key wins over title:** an item whose key matches is used even if another open item has the same title.
- **TC.FV.03-4 Empty keys never match:** two items with no key and different titles stay two items.
- **TC.FV.03-5 Key given on update:** a title-matched item with no key receives the new key.
- **TC.FV.03-6 Open only:** if the match is `Answered`, `Resolved` or `Dismissed`, a new item is created.
- **TC.FV.03-7 Within one call:** two items with the same title in one `items` array produce one created item and one "already pinned" update (result lines `A1 posted (ask).`, `A1 already pinned (ask) - updated, not re-posted.`).

### TC.FV.04 — `AttentionServiceTests` (session resolve) — FR.23.5 (UC.FV.06)

- **TC.FV.04-1 Answered:** `ResolveBySession("a2", answered, "Use plan B")` → status `Answered`, `Answer = "Use plan B"`, `ClosedUtc` set, case-insensitive id.
- **TC.FV.04-2 Resolved:** status `Resolved`, `ResolutionNote` set, `Answer` null.
- **TC.FV.04-3 Already closed:** a second resolve returns the already-closed outcome (status named), item unchanged, no SYSTEM entry written, no `AttentionChanged`.
- **TC.FV.04-4 Unknown id:** reports unknown (`Unknown attention item 'A99'.` via the tool, TC.FV.09).
- **TC.FV.04-5 Note cut:** a 3,000-char note is cut to 2,000.
- **TC.FV.04-6 Empty note:** allowed; stored as null/empty.

### TC.FV.05 — `AgentMcpServerAttentionToolsTests` (validation and limits) — FR.23.4 (UC.FV.01)

- **TC.FV.05-1 Invalid kind:** `kind: "urgent"` → `McpToolException` naming the problem (the kind and the allowed values).
- **TC.FV.05-2 Empty title:** `"   "` or newlines only → `McpToolException`.
- **TC.FV.05-3 Empty `items`:** `[]` → `McpToolException`.
- **TC.FV.05-4 All or nothing:** `items = [valid ask, invalid kind]` → exception; the ledger is unchanged (no item, `NextNumber` 1), no SYSTEM entry, no notification.
- **TC.FV.05-5 Kind is case-insensitive:** `"CRITICAL"` and `"Ask"` are accepted.
- **TC.FV.05-6 Title normalisation:** `"Line one\nline two"` → `Line one line two`; trimmed; 250 chars → 200.
- **TC.FV.05-7 Body:** default `""`; 25,000 chars → 20,000.
- **TC.FV.05-8 Options:** trimmed; empty ones dropped; each cut to 200; `recommendation` cut to 1,000; `key` trimmed and cut to 100.
- **TC.FV.05-9 Item count:** 30 items accepted; 31 rejected with `McpToolException` (nothing posted).
- **TC.FV.05-10 Options count:** 10 options kept; more than 10 — behaviour per open question 1 in the report (assert once the Architect decides: error vs. keep first 10).

### TC.FV.06 — `AgentMcpServerAttentionToolsTests` (results, SYSTEM entries) — FR.23.4, FR.23.8 (UC.FV.01, UC.FV.03)

Result text (one line per item, then the closing line) and session-file entries use `Constants.SessionFile.RoleSystem`, ASCII only.

- **TC.FV.06-1 New item result:** a `critical` post returns lines: `A12 posted (critical).` then (notification attempted, see TC.FV.07) then `Pinned items stay in the user's Focus view until resolved - don't restate them; refer to them by id.`
- **TC.FV.06-2 Update result:** `A7 already pinned (ask) - updated, not re-posted.`
- **TC.FV.06-3 Order:** a call with two new items returns the two lines in input order, then the closing line.
- **TC.FV.06-4 Created SYSTEM entry:** one `AppendMessage` with role System:
  ```
  [Attention A12 (critical)] <t>
  <b>
  Options: <o1> | <o2>
  Recommendation: <text>
  ```
  The body line(s), `Options:` and `Recommendation:` lines appear only when present.
- **TC.FV.06-5 Updated SYSTEM entry:** same layout with first line `[Attention A12 (ask) updated] <t>`.
- **TC.FV.06-6 One entry per item:** a call creating one and updating one writes two entries.
- **TC.FV.06-7 ASCII:** the entry text contains only ASCII characters when the input is ASCII.
- **TC.FV.06-8 Tool description:** `post_attention`'s description equals the wording in FR.23.4; `resolve_attention` and `list_attention` equal the wording in FR.23.5; the input schema lists `items` (1–30) with `kind`, `title`, `body`, `options`, `recommendation`, `key`, and `notify`.

### TC.FV.07 — `AgentMcpServerAttentionToolsTests` (notifications) — FR.23.4, FR.23.9 (UC.FV.02)

- **TC.FV.07-1 Critical always notifies:** one created `critical`, `notify` omitted → Telegram `NotifyAsync` called once (mention and sound) and desktop toast once; result has `Notified: Telegram, desktop.`
- **TC.FV.07-2 Ask without notify:** no notification; no `Notified:` line and no `Notification not sent` line.
- **TC.FV.07-3 `notify: true`:** a created `alert` notifies.
- **TC.FV.07-4 Updates never notify:** re-post a `critical` with the same key and `notify: true` → no notification.
- **TC.FV.07-5 One per call:** three created items including a critical → exactly one Telegram call and one toast.
- **TC.FV.07-6 Message, single item:** `A12 (critical): <t>` + blank line + body.
- **TC.FV.07-7 Message, several items:**
  ```
  3 new items in the Focus view:
  - A12 (critical): <t1>
  - A13 (ask): <t2>
  - A14 (todo): <t3>
  ```
  Only created items are listed; an updated item in the same call is not.
- **TC.FV.07-8 Channel gating:** `TelegramEnabled=false` → only `Notified: desktop.`; `NotificationsEnabled=false` → only `Notified: Telegram.`; both off → `Notification not sent - neither Telegram nor desktop notifications are enabled.`; the item is pinned in every case.
- **TC.FV.07-9 Shared fan-out:** `notify_user` and `post_attention` both go through the same helper (assert identical channel behaviour, including the mention and sound arguments).

### TC.FV.08 — `AgentMcpServerAttentionToolsTests` (worker refusal) — FR.23.6 (UC.FV.09)

For a server built with `OrchestrationDepth = 1`:
- **TC.FV.08-1:** `post_attention` returns exactly `Not posted - worker sessions can't post to the user's Focus view. Report to your supervisor instead.` as a normal result (no exception); ledger unchanged, no SYSTEM entry, no notification (even for `critical`).
- **TC.FV.08-2:** `resolve_attention` returns the same text; the ledger is unchanged.
- **TC.FV.08-3:** `list_attention` returns `No items.` even when the node has items.

### TC.FV.09 — `AgentMcpServerAttentionToolsTests` (resolve and list) — FR.23.5, FR.23.8 (UC.FV.06)

- **TC.FV.09-1:** `resolve_attention(id "a12", status "answered", note "Go with B")` → `A12 marked answered.`; SYSTEM entry `[Attention A12 answered] Go with B`.
- **TC.FV.09-2:** status `resolved`, note "no longer needed" → `A12 resolved.`; SYSTEM entry `[Attention A12 resolved] no longer needed`.
- **TC.FV.09-3 Empty note:** SYSTEM entry is exactly `[Attention A12 resolved]` (no trailing space).
- **TC.FV.09-4 Already closed:** `A12 is already answered.` (status lower-case); no SYSTEM entry, no change.
- **TC.FV.09-5 Unknown id:** `McpToolException` with message `Unknown attention item 'A99'.`
- **TC.FV.09-6 Bad status:** a value other than `answered`/`resolved` → `McpToolException`.
- **TC.FV.09-7 `list_attention` open:** ordered per FR.23.2; lines like `A12 [critical, open] <t>`.
- **TC.FV.09-8 `list_attention` all:** closed lines — `A3 [ask, answered] <t> - answer: <answer>`, `A4 [todo, resolved] <t> - <note>`, `A5 [alert, dismissed] <t>`; open items first, then closed newest first.
- **TC.FV.09-9 Empty:** `No open items.` (default and `status: "open"`), `No items.` (`status: "all"`).

### TC.FV.10 — `AgentMcpServerAttentionToolsTests` (`notify_user` capture) — FR.23.7 (UC.FV.08)

- **TC.FV.10-1:** a top-level `notify_user("\n  Need a decision\nSecond line")` creates an item: kind `Ask`, source `NotifyUser`, title `Need a decision` (first non-empty line, trimmed), body = the full message; result ends with ` Pinned as A1.`
- **TC.FV.10-2 Title cut:** a 300-char first line → 200 chars.
- **TC.FV.10-3 Still sends:** Telegram and desktop notifications exactly as today (existing `AgentMcpServerNotifyUserTests` still pass); exactly one fan-out, i.e. the capture adds none.
- **TC.FV.10-4 SYSTEM entry:** `[Attention A1 (ask)] Need a decision` followed by the body as in FR.23.8.
- **TC.FV.10-5 Dedupe:** the same message again → no new item; ` Already pinned as A1.` appended; a SYSTEM entry `[Attention A1 (ask) updated] …`.
- **TC.FV.10-6 Worker:** a worker call is dropped as today; no item, no SYSTEM entry.

### TC.FV.11 — `AttentionServiceTests` (user close, closures) — FR.23.2, FR.23.8, FR.23.11, FR.23.15 (UC.FV.07)

- **TC.FV.11-1 Todo:** `CloseByUser` → `Dismissed` (not `Resolved`; `ResolutionNote` stays empty), `IsUserCloseUnreported = true`, `ClosedUtc` set; SYSTEM entry `[Attention A5 dismissed by the user]`. A Todo is closed exactly like any other kind.
- **TC.FV.11-2 Other kinds:** Ask, Critical, Alert, Decided → `Dismissed`, flag set; SYSTEM entry `[Attention A4 dismissed by the user]`. A theory over all five kinds asserts the same outcome for each.
- **TC.FV.11-3 No prompt:** closing sends nothing through the turn path (the service has no dependency on it).
- **TC.FV.11-4 Closed item:** closing an already closed item changes nothing and writes no entry.
- **TC.FV.11-5 Take once:** `TakeUnreportedUserClosures(nodeId)` returns the closed items; a second call returns none; the flag is cleared and persisted.
- **TC.FV.11-6 Session-closed items are not reported:** an item closed by `ResolveBySession` is never in the list.
- **TC.FV.11-7 Answered from Focus:** `MarkAnsweredByUser` sets `Answered`, `Answer` = text sent, **no** SYSTEM entry, and the item is not in the closures.
- **TC.FV.11-8 Mark answered batch:** several answers in one call all become `Answered`; an unknown or already closed item id is skipped without failing the rest.

### TC.FV.12 — `AttentionServiceTests` (summary) — FR.23.1, FR.23.13 (UC.FV.10)

- **TC.FV.12-1:** empty ledger → `OpenCount 0`, `TopKind null`.
- **TC.FV.12-2:** Alert + Todo + Ask + Critical open → `OpenCount 4`, `TopKind Critical`; closing the Critical → `3`, `Ask`; then Ask → `2`, `Todo`; then Todo → `1`, `Alert`.
- **TC.FV.12-3 Decided excluded:** only Decided open → `OpenCount 0`, `TopKind null`; Decided + Alert → `1`, `Alert`.
- **TC.FV.12-4 Closed excluded:** answered, resolved and dismissed items are not counted.

### TC.FV.13 — `AttentionServiceTests` — FR.23.16 (UC.FV.13)

- **TC.FV.13-1:** `AttentionChanged` is raised with the right `NodeId` after post (create and update), session resolve, user close and mark answered.
- **TC.FV.13-2:** not raised when nothing changed (already closed, rejected post).
- **TC.FV.13-3:** raised once per call, not once per item, for a multi-item post.

### TC.FV.14 — `InstructionBlockBuilderTests` (guidance gate) — FR.23.10 (UC.FV.14)

- **TC.FV.14-1:** `IsAgentToolsEnabled && IsNewSession && !IsWorkerSession` → the block contains `Constants.Instructions.AttentionGuidance`, whose text equals FR.23.10 with the prefix `mcp__cmx__` built from `Agent.McpServerName`.
- **TC.FV.14-2:** each of `IsAgentToolsEnabled = false`, `IsNewSession = false`, `IsWorkerSession = true` → guidance absent.
- **TC.FV.14-3:** same gate as `NoWorkflowTool` (both present or both absent across the combinations above).

### TC.FV.15 — `InstructionBlockBuilderTests` (Pinned list) — FR.23.11 (UC.FV.04, UC.FV.07)

- **TC.FV.15-1 Exact lines:** with `A12` critical and `A3` ask pinned, the block ends with
  ```
  - Pinned in the user's Focus view - they can see these, so don't restate them; refer to them by id, and call `mcp__cmx__resolve_attention` when one is answered in conversation or no longer applies:
    - A12 critical: <title>
    - A3 ask: <title>
  ```
  (two-space indent on item lines) after the existing lines.
- **TC.FV.15-2 Ordering:** severity (Critical, Ask, Todo, Alert, Decided) then number ascending, whatever the input order; Decided items are included.
- **TC.FV.15-3 Cap:** 25 open items → 20 item lines then `  - ...and 5 more (call mcp__cmx__list_attention)`; exactly 20 items → no "more" line.
- **TC.FV.15-4 Title cut:** a 130-char title → first 120 chars + `...`; a 120-char title is unchanged.
- **TC.FV.15-5 Closures line:** one Todo and one Ask in `UserClosedAttentionItems` → `- Closed by the user in the Focus view since your last turn: A5 todo "<t5>" dismissed without an answer; A4 ask "<t4>" dismissed without an answer.` Every kind reads `dismissed without an answer`; the text `marked done` never appears.
- **TC.FV.15-6 Independence of the two lines:** open items only → first line only; closures only → closures line only (no "Pinned" line); neither → neither, and the block is unchanged versus before FR.23.
- **TC.FV.15-7 Gate:** `IsAgentToolsEnabled = false` or `IsWorkerSession = true` → no Pinned list and no closures line even with items supplied.
- **TC.FV.15-8 Not gated on `IsNewSession`:** emitted with `IsNewSession = false` and `true`.
- **TC.FV.15-9 Plumbing:** `InstructionOptionsModel` exposes `PinnedAttentionItems` and `UserClosedAttentionItems`, both optional (default null → no lines).
- **TC.FV.15-10 Take-once through the call sites:** two consecutive `SessionPromptComposer` builds for a node with one unreported closure → the closure line appears in the first block only (the three call sites use `TakeUnreportedUserClosures`).

### TC.FV.16 — `InstructionBlockBuilderTests` (amended texts) — FR.23.10 (UC.FV.14, UC.FV.04)

- **TC.FV.16-1 `TelegramNotifyGuidance`:** gate unchanged (FR.21.3); text equals the new FR.23.10 wording; contains `critical`, `notify: true`, `post_attention`, `notify_user`, and `There is no blocking "ask and wait" tool`.
- **TC.FV.16-2 `ScheduledTurnNotifyReminder`:** gate unchanged (FR.21.24); text equals the new FR.23.10 wording; contains `mcp__cmx__post_attention` and `Don't restate items that are already pinned.`
- **TC.FV.16-3 Worker suppression:** worker block has none of: guidance, Telegram guidance, scheduled reminder, Pinned list, closures.
- **TC.FV.16-4 Skill text:** `Constants.Agent.OrchestrationSkillMarkdown` tells supervisors to post the ask once with `post_attention` (`notify: true` when they must act soon) and not restate it in later delivery turns, and tells workers not to call `notify_user` or `post_attention`.

### TC.FV.17 — `ClaudeSessionImportServiceTests` (`ExtractToolDescription`) — FR.23.8 (UC.FV.01)

- **TC.FV.17-1 `message`:** a `notify_user` tool use with `{"message":"Need a decision"}` → `[Tool: …notify_user] Need a decision` in the same shape as other fallback fields; a message over 120 chars is cut to 120.
- **TC.FV.17-2 `items`:** `{"items":[{"title":"A"},{"title":"B"}]}` → `2 item(s): A; B`; one item → `1 item(s): A`; long list cut to 120 chars; items without `title` are ignored.
- **TC.FV.17-3 `id` + `status`:** `{"id":"a12","status":"answered"}` → `a12 answered`.
- **TC.FV.17-4 Regression:** existing shapes (file path, command, pattern, …) are unchanged.

### TC.FV.18 — `AttentionPromptBuilderTests` — FR.23.15 (UC.FV.05)

- **TC.FV.18-1 Format:** two answers produce exactly
  ```
  Answers from the Focus view:
  - A3 (<t3>): <a3>
  - A5 (<t5>): <a5>
  ```
  in input order.
- **TC.FV.18-2 Multi-line answer:** `"line1\nline2\nline3"` → `- A3 (<t>): line1` then `  line2` then `  line3` (two-space indent on continuation lines); `\r\n` input gives the same result.
- **TC.FV.18-3 Single answer** and a title with parentheses are rendered verbatim.
- **TC.FV.18-4 Empty list:** returns an empty string (or per implementation; assert it is not a header-only prompt).
- **TC.FV.18-5 Reply uses the same builder:** the per-card Reply prompt is `BuildAnswersPrompt` with one answer; TC.FV.18-3 (single answer) already covers the exact output `Answers from the Focus view:` + `- A3 (<t>): <a>`. Add a one-answer multi-line case (continuation indented by two spaces) to that test.

### TC.FV.19 — `InProcDaemonClientTests` — FR.23.15, FR.23.16 (UC.FV.05, UC.FV.13)

- **TC.FV.19-1 Delegation:** `GetAttentionItems` returns the service's items in FR.23.2 order, any status; `GetAttentionSummary`, `MarkAttentionAnswered` and `CloseAttentionByUser` delegate to `IAttentionService`.
- **TC.FV.19-2 Relay:** raising the service's `AttentionChanged` raises `IDaemonClient.AttentionChanged` with the same `NodeId`, the way `EntryAppended` is relayed.
- **TC.FV.19-3 Unsubscribe:** after dispose, the relay no longer fires.
- **TC.FV.19-4 Singleton service shared:** an item posted through the MCP server is visible via `GetAttentionItems` of the client built over the same service instance.

---

## Manual UI tests (Avalonia)

Common set-up: a top-level session with agent tools enabled. To create items without waiting for a model, ask the session "post three test items to my Focus view with post_attention: a critical, an ask and a todo" or call the tools from an MCP test client. Try both the dark and the light theme where stated.

### TC.FV.20 — Switch placement and default — FR.23.12 (UC.FV.11)

1. Open a session. **Expected:** the header reads, left to right, `[Search output...] [Session][Focus] [MD] [JSONL]`; Session is checked, Focus unchecked; Padding and font look like MD/JSONL.
2. Click Focus. **Expected:** Focus checked, Session unchecked; exactly one is checked at all times.
3. Click Focus again. **Expected:** it stays checked.
4. Click Session. **Expected:** Session checked, output as before.

### TC.FV.21 — Focus replaces output; MD/JSONL hidden; busy indicator stays — FR.23.12 (UC.FV.11)

1. With Session selected, note the MD and JSONL toggles and the message list.
2. Select Focus. **Expected:** the message list and its search overlay are replaced by the Focus view; MD and JSONL are not visible.
3. Start a turn (send any prompt) and stay on Focus. **Expected:** the thinking/busy indicator is visible while the turn runs.
4. Select Session. **Expected:** output is back unchanged, MD and JSONL visible with their previous states.

### TC.FV.22 — Enter in search switches to Session — FR.23.12 (UC.FV.12)

1. Select Focus. Type a word that occurs in the output in the search box and press Enter. **Expected:** the view switches to Session and the search runs there (matches highlighted, as with Session selected).
2. Select Focus again, leave the box empty, press Enter. **Expected:** the view switches to Session.
3. With Session selected press Enter. **Expected:** search works as before; stays on Session.
4. Select Focus and type without pressing Enter. **Expected:** stays on Focus.

### TC.FV.23 — Badge colour per top kind, dark and light theme — FR.23.1, FR.23.13 (UC.FV.10)

Repeat each step in the dark theme and in the light theme. Session is selected.

| Step | Open items (excluding Decided) | Expected badge |
|---|---|---|
| TC.FV.23-1 | one Alert | transparent background, number in the normal foreground colour |
| TC.FV.23-2 | one Todo | background = theme foreground (light on dark, dark on light), number = theme background |
| TC.FV.23-3 | one Ask | background goldenrod `#DAA520`, black number |
| TC.FV.23-4 | one Critical | background dark red `#8B0000`, white number |
| TC.FV.23-5 | Alert + Todo + Ask + Critical | dark red (Critical), count 4 |

1. Create the items per row, keeping only those listed open (close the others from Focus).
2. Check the badge: a rounded rectangle (TC.FV.40), number legible, minimum width about 16.
3. Close the Critical, then Ask, then Todo. **Expected:** colour steps down to goldenrod, then the Todo colours, then transparent, and the count decreases by 1 each time.
4. Switch theme with the badge visible. **Expected:** the Todo badge re-colours; Critical/Ask colours stay.

### TC.FV.24 — Badge count and visibility — FR.23.13 (UC.FV.10)

1. Open one Ask, one Alert and two Decided items. **Expected:** the badge shows `2` (Decided not counted); colour goldenrod.
2. Close the Ask and the Alert. **Expected:** the badge disappears (only Decided open, count 0).
3. Post a new Alert. **Expected:** the badge appears with `1`.
4. Select Focus. **Expected:** the badge is hidden while Focus is selected.
5. Select Session. **Expected:** the badge is visible again with `1`.
6. Close all open items; **Expected:** the badge is hidden at count 0.

### TC.FV.25 — Focus layout and empty state — FR.23.14 (UC.FV.01)

1. Open a session with no items, select Focus. **Expected:** `Nothing needs your attention in this session.`; **Send answers (0)** is disabled at the top; no Decisions expander; no Closed expander. (The empty state also shows when only Decided items are open; the Decisions expander is then still present.)
2. Post a Critical, an Ask (with markdown body, 2 options, recommendation), a Todo, an Alert and a Decided. **Expected:** top to bottom: Send answers button; cards Critical, Ask, Todo, Alert in that order (equal kinds by number); a collapsed **Decisions (1)** expander holding the Decided card; Closed expander hidden.
3. Inspect the Ask card. **Expected:** header row with kind chip (Critical dark red, Ask goldenrod, Todo foreground-coloured, Alert neutral, Decided muted grey), id `A2`, SemiBold title, posted time (`HH:mm` today, `yyyy-MM-dd HH:mm` for an older item, local time); markdown rendered and the text selectable; option buttons; `Use recommendation: <text>`; a multi-line reply box; an action row under it with **Reply** on the left and the close button (**Dismiss**) on the far right.
4. Close the Critical and the Todo. **Expected:** a collapsed **Closed (2)** expander appears; expanded, newest closed first, each compact with its outcome (`Dismissed` for both). The collapse and compact-card behaviour is detailed in TC.FV.35.
5. Have the session close an Ask as answered. **Expected:** the closed entry shows `Answered: <answer>`.
6. Close every open item. **Expected:** empty-state text shows and the Closed expander is still present with its items (collapsed unless already expanded by the user).
7. An item with no options and no recommendation has neither option buttons nor a recommendation line.

### TC.FV.26 — Options and recommendation fill the reply box — FR.23.14 (UC.FV.05)

1. Type "draft" in an Ask card's reply box, then click an option. **Expected:** the box now holds exactly the option text (draft replaced); editable.
2. Click another option. **Expected:** replaced again.
3. Click `Use recommendation: <text>`. **Expected:** the box holds `Accepted your recommendation: <text>`.
4. Edit the text, **Expected:** the Send answers count stays 1.

### TC.FV.27 — Send answers — FR.23.15 (UC.FV.05)

1. Type a draft "keep this draft" in the main input box (do not send).
2. In Focus, fill reply boxes of two cards (one with a two-line answer via Shift+Enter or the box's newline key); leave a third empty. **Expected:** the button reads **Send answers (2)** and is enabled.
3. Click it. **Expected:** the items are marked `Answered` first (with `Answer` = the text sent) and the reply boxes cleared, and then the prompt is sent; the instruction block of that very turn therefore does not list these items as pinned. One USER message appears in the Session output:
   ```
   Answers from the Focus view:
   - A3 (<title>): <answer>
   - A5 (<title>): <line1>
     <line2>
   ```
   with cards in the on-screen order; the empty card is not in it.
4. **Expected:** the main input box still contains "keep this draft" (and the draft file is not deleted); the model, effort and profile used are those currently selected for the main input.
5. **Expected:** the two cards move to Closed with `Answered: <text sent>`, the reply boxes are cleared, the badge count drops by 2, no SYSTEM entry is added for the answers.
6. While a turn is running: **Expected:** Send answers follows the main Send button, which stays enabled because concurrent sends are allowed. Clicking it sends the answers the same way a second main-box send would; reply text is cleared only for the answers actually sent.
7. With all boxes empty: **Expected:** the button is disabled.
8. On the next turn the Session sees the answers (check the reply) and is not told in the closures line about the answered items.

### TC.FV.28 — Close buttons per kind — FR.23.14, FR.23.15 (UC.FV.07)

1. Create one item of each kind. **Expected:** the close buttons read **Got it** (Critical), **Dismiss** (Ask), **Dismiss** (Todo), **Got it** (Alert), **Got it** (Decided). No button reads **Done**.
2. Click **Dismiss** on the Todo. **Expected:** it moves to Closed as `Dismissed`; no prompt is sent; the session output gets the SYSTEM entry `[Attention A<n> dismissed by the user]`.
3. Click **Dismiss** on the Ask. **Expected:** Closed shows `Dismissed`; SYSTEM entry `[Attention A<n> dismissed by the user]`; no prompt sent.
4. Click **Got it** on the Critical, Alert and Decided. **Expected:** each shows `Dismissed` in Closed.
5. Send any prompt in the session. **Expected:** the reply shows the session knows (or inspect the instruction block in the log): `Closed by the user in the Focus view since your last turn: …` with every closed item `dismissed without an answer`. Send a second prompt: **Expected:** the line is gone.

### TC.FV.29 — Live refresh keeps unsent reply text — FR.23.16 (UC.FV.13)

1. Select Focus; type "unsent A" into the reply box of one Ask and "unsent B" into another.
2. From the session (or via an MCP client) post a new Alert and close a third item. **Expected:** the Focus view updates without switching or flicker; the new card appears; the reply boxes still hold "unsent A" and "unsent B"; **Send answers (2)** stays.
3. Have the session close the item with "unsent A" via `resolve_attention`. **Expected:** that card moves to Closed; "unsent B" remains.
4. Select Session. **Expected:** the badge shows the right count without re-opening the session.
5. Post an item in a **different** session. **Expected:** this session's view is unaffected.

### TC.FV.30 — Selection is per session and not persisted — FR.23.12 (UC.FV.11)

1. In session X select Focus; open session Y. **Expected:** Y is on Session.
2. Select Focus in Y; go back to X. **Expected:** X is still on Focus; Y keeps Focus when you return to it.
3. Restart the app. **Expected:** every session opens on Session.

### TC.FV.31 — End to end: a repeated ask in scheduled turns — FR.23.4, FR.23.10, FR.23.11 (UC.FV.01, UC.FV.03, UC.FV.04)

1. Start a fresh top-level session; give it a task that ends in an open decision, and schedule a wake every few minutes (`mcp__cmx__schedule_wake`).
2. **Expected (first turn):** the session posts the ask once (`A1 posted (ask).`); the badge shows 1 in goldenrod.
3. Let two scheduled turns run without answering. **Expected:** the session does not restate the ask (at most a reference to `A1`); `list_attention` shows one open item; the ledger file under `%AppData%\TessynDesktop\attention` holds one item.
4. If the session re-posts `A1` with the same key. **Expected:** `A1 already pinned (ask) - updated, not re-posted.`, no new item and no notification.
5. Answer from Focus (TC.FV.27). **Expected:** the next scheduled turn acts on the answer and does not raise `A1` again.
6. Restart the app. **Expected:** the closed `A1` is still in Closed, and a newly posted item is `A2`.

### TC.FV.32 — `AttentionServiceTests` / `InProcDaemonClientTests` (single-answer mark-first) — FR.23.11, FR.23.15 (UC.FV.16)

Core part of the per-card Reply. The prompt text is already covered by TC.FV.18-3 and TC.FV.18-5.

- **TC.FV.32-1 One answer:** `MarkAttentionAnswered(nodeId, [one answer])` marks only that item `Answered` with `Answer` = the text; other open items stay Open; no SYSTEM entry; the item is not in `TakeUnreportedUserClosures`.
- **TC.FV.32-2 Mark first, then build:** after the call, building the instruction block from the current open items (as `SessionPromptComposer` does) lists the other items in the Pinned list and does **not** list the answered one (FR.23.11 plumbing).
- **TC.FV.32-3 Raises `AttentionChanged`** once, and `GetAttentionSummary` reflects the lower count.
- **TC.FV.32-4 Already closed or unknown id:** skipped without error (see TC.FV.11-8); nothing else changes.

### TC.FV.33 — Per-card Reply — FR.23.14, FR.23.15 (UC.FV.16)

1. Type "keep this draft" in the main input box. Open Focus with three open Ask cards A3, A4, A5. **Expected:** every card has **Reply** (left) disabled, and the close button on the far right.
2. Type "reply 3" in A3's box. **Expected:** only A3's Reply is enabled; A4's and A5's stay disabled. Type "unsent 5" in A5's box (do not send).
3. Click A3's **Reply**. **Expected:** exactly one USER message is sent:
   ```
   Answers from the Focus view:
   - A3 (<title>): reply 3
   ```
   one answer line only, through the same send path as the main input box (same model, effort, profile).
4. **Expected:** A3 is marked `Answered` first (it appears in Closed as `Answered: reply 3`, its box cleared), then the prompt is sent; that turn's pinned list does not include A3 but does include A4 and A5.
5. **Expected:** A5's box still holds "unsent 5"; **Send answers (1)** now counts only A5; the main input box still holds "keep this draft" and its draft file is not deleted.
6. Type a two-line answer on A4 and click its Reply. **Expected:** the prompt line is `- A4 (<title>): <line1>` followed by `  <line2>` (two-space continuation).
7. Clear A5's box. **Expected:** A5's Reply becomes disabled again.

### TC.FV.34 — Dismiss placement, tooltip, discarded text — FR.23.14, FR.23.15 (UC.FV.07, UC.FV.16)

1. On an Ask and a Todo card, look at the action row. **Expected:** **Reply** on the left; the close button (**Dismiss**) on the far right, visibly apart from Reply.
2. Hover the close button on any card. **Expected:** a tooltip says it closes the item without sending anything (e.g. "Closes without sending").
3. Type "typed but not sent" in a card's box, then click its close button. **Expected:** no prompt is sent; the card moves to Closed as `Dismissed`; the typed text is discarded (not in any other box, not in the main input box); **Send answers (n)** drops by one.
4. Critical, Alert and Decided cards show **Got it** in the same far-right position with the same tooltip.
5. After the next turn starts, **Expected:** the instruction block closure line reads `dismissed without an answer` for each (TC.FV.15-5).

### TC.FV.35 — Closed history collapsed by default and compact — FR.23.14 (UC.FV.17)

1. Create and close three items (an Ask answered via Reply, a Todo dismissed, an Alert dismissed). Select Focus. **Expected:** **Closed (3)** is collapsed.
2. Expand Closed. **Expected:** three compact cards, newest closed first; each shows only the header row (chip, id, title, time) and the outcome line (`Answered: <answer>` / `Dismissed`); no body, options, recommendation, reply box or buttons.
3. Click a closed card's header row. **Expected:** its body shows; click again: hidden. Other closed cards are unaffected.
4. With Closed expanded, have the session post a new item or close another. **Expected:** the refresh keeps Closed expanded.
5. Switch to Session and back to Focus. **Expected:** Closed is collapsed again.
6. Open another session that has closed items, select Focus. **Expected:** its Closed is collapsed, regardless of how it was left in the first session.
7. Restart the app and open Focus. **Expected:** collapsed.

### TC.FV.36 — `AttentionServiceTests` (all-open summary) — FR.23.17 (UC.FV.18)

- **TC.FV.36-1:** empty ledger → `AllOpenCount 0`, `AllOpenTopKind null` (and `OpenCount 0`, `TopKind null`).
- **TC.FV.36-2 Decided counted:** Decided + Alert open → `AllOpenCount 2`, `OpenCount 1`.
- **TC.FV.36-3 Decided only:** two Decided open → `AllOpenCount 2`, `AllOpenTopKind Decided`; `OpenCount 0`, `TopKind null`.
- **TC.FV.36-4 Mixed kinds:** Decided + Alert + Todo open → `AllOpenTopKind Todo`, equal to `TopKind`.
- **TC.FV.36-5 Steps down as items close:** Decided + Alert + Critical → `3`, `Critical`; close the Critical → `2`, `Alert`; close the Alert → `1`, `Decided`.
- **TC.FV.36-6 Closed excluded:** answered, resolved and dismissed items, Decided included, are in neither count.

### TC.FV.37 — `RecentSessionVisibilityServiceTests` — FR.9.8 (UC.FV.18)

- **TC.FV.37-1 Each pin alone:** a favorite, a session with open Focus items, and a session with schedules are each shown while stale AND hidden.
- **TC.FV.37-2 Not pinned:** recent and not hidden → shown; recent and hidden → not shown; stale, hidden or not → not shown.
- **TC.FV.37-3 `IsPinnedInRecentDropdown`:** true for each reason alone and for all three; false when none applies.

### TC.FV.38 — Session-row badge in the tree — FR.23.16, FR.23.17 (UC.FV.18)

1. In session S, post one Ask, one Alert and two Decided items. **Expected:** S's tree row shows a goldenrod badge `4` immediately left of its name, after the status dots; S's Focus badge shows `2`.
2. Dismiss the Ask and the Alert from Focus. **Expected:** the row badge turns grey (`#707070`, white number) with `2`; the Focus badge disappears.
3. Dismiss both Decided items. **Expected:** the row badge disappears.
4. With S selected, have another session T post an Alert. **Expected:** T's row badge appears without selecting T, a bare number in the normal foreground colour.
5. Restart the app. **Expected:** every badge comes back with the same count and colour.
6. Switch theme with a Todo-only row badge visible. **Expected:** it re-colours like the Focus badge (TC.FV.23-2).

### TC.FV.39 — Recent dropdown: Pinned Sessions and row badge — FR.9.6, FR.9.7, FR.9.8, FR.23.17 (UC.FV.18)

Preconditions: session A has had no activity for more than 24 hours and has one open Ask; session B has had no activity for more than 24 hours, has a schedule and no open items; session C was active within the last 24 hours, is not a favorite, and has no open items or schedules; session D is a favorite with one open Alert. None is a Worker Session.

1. Open the dropdown. **Expected:** A, B, C and D are all listed, most recently active first. A shows a goldenrod `1` badge between the status dots and the name; D shows a `1` Alert badge; B and C show no badge; B shows the black alarm clock.
2. **Expected icons:** C shows the hide icon ⦸; D shows the star; A and B show neither, and their folder and profile column lines up with C's and D's.
3. Click ⦸ on C. **Expected:** C disappears at once (FR.9.6).
4. Click the star on D. **Expected:** the star goes and no ⦸ appears, because D is still pinned by its open Alert.
5. Select A. **Expected:** the tree reveals and selects A; the closed box shows A's name only, without a badge.
6. Dismiss A's Ask from Focus, then reopen the dropdown. **Expected:** A is no longer listed (not pinned, no activity in the last 24 hours).
7. Cancel B's schedule, then reopen the dropdown. **Expected:** B is no longer listed.

### TC.FV.40 — Badge shape — FR.23.13, FR.23.17 (UC.FV.10, UC.FV.18)

1. Show the Focus badge with a single-digit count. **Expected:** a rounded rectangle with a small corner radius, not a circle; number legible; minimum width about 16.
2. Compare it with a tree-row badge and a dropdown-row badge. **Expected:** the same shape and size.
3. Select Focus. **Expected:** the kind chips on the cards keep their pill shape.
