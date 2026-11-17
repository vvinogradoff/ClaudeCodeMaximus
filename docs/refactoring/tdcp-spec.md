# Tessyn Daemon Control Protocol (TDCP) — Draft Spec

> Working document (not authoritative, not committed). Author: Developer/Claude.
> Companion to `refactoring-plan.md` (Stage 4) and `structure-notes.md`. Snapshot: 2026-07-18.
> Purpose: define the UI↔daemon contract so (a) the Avalonia UI can swap in-process → wire
> transport with no ViewModel changes, and (b) the NodeJS `tessyn` project has a spec to port to.

## 1. What TDCP is

TDCP is the wire form of the **`IDaemonClient`** interface
(`ClaudeMaximus.Core/Services/Daemon/IDaemonClient.cs`). Everything the UI needs from the
daemon-owned domain is expressed as `IDaemonClient` members; TDCP is those members serialized over
a loopback transport. Two adapters implement the same interface:

| Adapter | Where | Transport | Stage |
|---|---|---|---|
| `InProcDaemonClient` | Core | direct method calls to Core services | 4 (done) |
| `WireDaemonClient` | UI/client | TDCP: JSON-RPC 2.0 over Streamable HTTP (loopback) | 5 |

**Design invariant:** ViewModels depend only on `IDaemonClient`. The transport swap (Stage 5)
touches no ViewModel. Any NodeJS `tessyn` backend that implements the TDCP methods below is a
drop-in alternative daemon (D1).

## 2. Placement decision (refinement over the plan)

The plan (Stage 4) placed `IDaemonClient` "in the UI." **Refinement:** both `IDaemonClient` and
`InProcDaemonClient` are Avalonia-free (they touch only Core services + Models), so they live in
**`ClaudeMaximus.Core`**. This:
- makes the boundary adapter unit-testable from `ClaudeMaximus.Tests` (which references Core only);
- lets a future `ClaudeMaximus.Wpf` client (D3) share the exact same contract;
- keeps the Avalonia-free gate intact.

Only the transport-specific `WireDaemonClient` and the UI's choice of which adapter to inject are
client-side concerns.

## 3. Transport (D4 ✔: TDCP = JSON-RPC 2.0 over Streamable HTTP, co-hosted with MCP)

> **Confirmed 2026-07-19 (Architect).** TDCP is **its own protocol**, *not* an extension of the
> MCP tool surface, carried over **MCP-style Streamable HTTP**. This unifies with MCP's own modern
> transport and "fits every modern tech stack." Supersedes the earlier long-poll and raw-WebSocket
> notes. Full rationale + S1/S2/S3 topology: refactoring-plan.md §0.2.

- **Own protocol, shared host & transport family:** TDCP messages are **JSON-RPC 2.0** (requests &
  responses correlated by `id`; events as `notifications`). They are **not** folded into the MCP
  tool-call surface, but ride the **same transport** as MCP. Both live in one daemon process / one
  Kestrel host, on different paths: `/mcp` (claude callbacks, unchanged) and `/tdcp` (UI ↔ daemon).
  "Separate protocol" ≠ "separate infrastructure."
- **Streamable HTTP semantics:** a POST to `/tdcp` carrying a JSON-RPC request is answered with
  **either** a single `application/json` body **or** a `text/event-stream` (SSE) stream when the
  result streams (e.g. a turn emitting incremental `turn.event`s). Server→client push that is not
  tied to a request (`TreeChanged`/`ScheduleChanged`) rides an **SSE stream opened by a GET** to
  `/tdcp`. This is exactly MCP's Streamable HTTP model.
- **Auth:** the existing `X-CMX-Token` loopback token mechanism, sent as a header on every POST/GET.
- **Bind:** `127.0.0.1` only, ephemeral port discovered by the UI via the existing daemon-discovery
  handshake (reuse `EnsureDaemonRunningAsync`/`IsDaemonListening` patterns from `App.axaml.cs`).
- **Serving the future S3 link (daemon → web server) with the same schema:** the daemon **dials out**
  over HTTPS, issues a GET that returns an SSE stream, the web server pushes JSON-RPC **requests**
  (commands) down that stream, and the daemon POSTs JSON-RPC **responses/events** back up — so the
  daemon stays the JSON-RPC *server* though it opened the connection (standard reverse-tunnel-over-
  HTTPS). Only the connection-establishing side differs from S2; the message schema is identical.
  (S3 is a future stage, not built during the split — but the transport choice makes it free.)
  **Realised 2026-11-12** for the web client: see §8 (Remote API v1 and the S3 relay) and FR.25.

## 4. Method map (IDaemonClient → TDCP)

Request/response methods (JSON-RPC `method` + `params` → `result`). NodeId/token are strings.

| IDaemonClient member | TDCP method | params | result |
|---|---|---|---|
| `Directories` (get) | `tree.list` | — | `DirectoryNode[]` (full tree) |
| `SelectedNodeId` (get/set) | `tree.getSelected` / `tree.setSelected` | `{nodeId?}` | `{nodeId?}` / ok |
| `FindSessionByNodeId` | `tree.findByNodeId` | `{nodeId}` | `SessionNode?` |
| `FindSessionByAgentToken` | `tree.findByToken` | `{token}` | `SessionNode?` |
| `GetSessionLocation` | `tree.location` | `{nodeId}` | `{directoryLabel, treePath}` |
| `IsSessionRunning` | `turn.isRunning` | `{nodeId}` | `{running}` |
| `SetSessionRunning` | `turn.setRunning` | `{nodeId, running}` | ok |
| `GetTurnLock` | *(local only — see §5)* | — | — |
| `RunTurnAsync` | `turn.run` | `{nodeId, prompt, source}` | `TurnResult` (stream via events) |
| `CancelTurn` | `turn.cancel` | `{nodeId}` | `{cancelled}` |
| `ResolveProfileName` | `turn.resolveProfile` | `{nodeId}` | `{profileName?}` |
| `GetSchedules` | `schedule.list` | `{targetNodeId?}` | `Schedule[]` |
| `AddSchedule` | `schedule.add` | `Schedule` | ok |
| `RemoveSchedule` | `schedule.remove` | `{scheduleId}` | `{removed}` |
| `ReadEntries` | `content.read` | `{fileName}` | `SessionEntry[]` |
| `SessionFileExists` | `content.exists` | `{fileName}` | `{exists}` |
| `GetSessionFilePath` | `content.path` | `{fileName}` | `{path}` |
| `TreeChanged` (event) | push `tree.changed` | — | `TreeChangedEvent` |
| `ScheduleChanged` (event) | push `schedule.changed` | — | — |
| `LoadFromSettings` | *(local only — see §5)* | — | — |
| `AddDirectory` | `tree.addDirectory` | `{path}` | `DirectoryNode` |
| `TryDeleteDirectory` | `tree.deleteDirectory` | `{directoryNodeId}` | `{deleted}` |
| `AddGroup` | `tree.addGroup` | `{parentDirectoryNodeId, name}` | `GroupNode` |
| `AddGroupToGroup` | `tree.addGroupToGroup` | `{parentGroupNodeId, name}` | `GroupNode` |
| `TryDeleteGroup` | `tree.deleteGroup` | `{parentDirectoryNodeId, groupNodeId}` | `{deleted}` |
| `TryDeleteGroupFromGroup` | `tree.deleteGroupFromGroup` | `{parentGroupNodeId, groupNodeId}` | `{deleted}` |
| `RenameGroup` | `tree.renameGroup` | `{groupNodeId, newName}` | ok |
| `AddSession` | `tree.addSession` | `{parentDirectoryNodeId, name}` | `SessionNode` |
| `AddSessionToGroup` | `tree.addSessionToGroup` | `{parentGroupNodeId, name}` | `SessionNode` |
| `ImportSession` | `tree.importSession` | `{parentDirectoryNodeId, name, fileName, claudeSessionId}` | `SessionNode` |
| `ImportSessionToGroup` | `tree.importSessionToGroup` | `{parentGroupNodeId, name, fileName, claudeSessionId}` | `SessionNode` |
| `TryDeleteSession` | `tree.deleteSession` | `{parentDirectoryNodeId, sessionNodeId, forceDelete?}` | `{deleted}` |
| `TryDeleteSessionFromGroup` | `tree.deleteSessionFromGroup` | `{parentGroupNodeId, sessionNodeId, forceDelete?}` | `{deleted}` |
| `RenameSession` | `tree.renameSession` | `{sessionNodeId, newName}` | ok |
| `RemoveSessionFromModel` | `tree.removeFromModel` | `{sessionNodeId}` | `DirectoryNode?` |
| `InsertSessionIntoDirectory` | `tree.insertIntoDirectory` | `{directoryNodeId, sessionNodeId, index}` | ok |
| `InsertSessionIntoGroup` | `tree.insertIntoGroup` | `{groupNodeId, sessionNodeId, index}` | ok |
| `BuildAgentSessionSummaries` | `tree.agentSummaries` | — | `SessionSummary[]` |
| `CollectAllClaudeSessionIds` | `tree.allClaudeSessionIds` | — | `string[]` |

> **Stage 5b addition (2026-07-24):** the tree-CRUD rows above were pulled forward from §6 — they
> were originally slated for Stage 6/7 but turned out to be a hard prerequisite for
> `SessionTreeViewModel` to depend on `IDaemonClient` at all (it used ~26 call sites across nearly
> the full `ISessionRegistry` surface). `InProcDaemonClient` implements these as **real** delegation
> to the wrapped registry (this is what's actually used — one process, no split yet, per D5).
> `WireDaemonClient` throws `NotSupportedException` for all of them for now, same as `GetTurnLock` —
> the wire methods above are specified so `TdcpServer`/`WireDaemonClient` can catch up *if/when* the
> exe split is revisited, but implementing that wire path now would be speculative work against a
> transport nothing exercises yet (D5: the split "might not even be necessary").

> Methods pass `SessionNodeModel` today (in-proc). Over the wire, callers pass a **NodeId** and the
> daemon resolves the node from its registry (the UI must not hold authoritative node objects
> post-split). `WireDaemonClient` bridges this: its `RunTurnAsync(SessionNodeModel node, …)`
> forwards `node.NodeId`.

## 5. Cross-cutting notes (carried from plan §3)

- **`GetTurnLock` is process-local.** The per-node `SemaphoreSlim` (turn serialization, FR.14.9)
  must live **daemon-side**. Post-split the UI cannot hold the lock; `turn.run` on the daemon
  acquires it internally. `WireDaemonClient.GetTurnLock` throws `NotSupportedException` — the UI
  stops calling it once turns route through `turn.run`.
- **`LoadFromSettings` and the Stage 5b tree-CRUD members (§4) also throw `NotSupportedException`
  on `WireDaemonClient`** — not yet wired over TDCP (only `InProcDaemonClient` implements them for
  real today, per D5). `LoadFromSettings` is startup-only in any case (called once in `App.axaml.cs`
  after `appSettings.Load()`, not by the UI at large) and wouldn't make sense for a remote client to
  call regardless.
- **`SessionViewModel`'s interactive send path is intentionally NOT migrated onto `RunTurnAsync`.**
  `ISessionTurnService.RunTurnAsync` is the headless (scheduler/orchestration) path; the interactive
  path drives `IClaudeProcessManager` directly for live token streaming, coordinating with headless
  turns only via the shared `GetTurnLock`. Migrating it onto `RunTurnAsync` would silently drop
  streaming — see the next bullet, this needs its own design.
- **`appsettings.json` single-writer:** the daemon owns it post-split (plan §3). Tree CRUD and
  schedule mutations mutate daemon state; the UI reflects via the change channel.
- **File watchers → push:** the UI currently watches session `.txt`/JSONL. Post-split, prefer the
  daemon pushing `content.appended`/`tree.changed` over the change channel to avoid two sources of
  truth. `content.read` remains for full reloads.
- **Streaming turns:** `RunTurnAsync` returns a final `TurnResultModel`, but the UI also needs live
  stream events during the turn. In-proc this is done via file watching + `SessionTurnService`
  writing the session file. Over the wire, add a `turn.event` push (assistant deltas, thinking,
  tokens) keyed by nodeId. Design alongside the change channel in Stage 5.

## 6. Not yet in IDaemonClient (fold in as consumers migrate — Stages 6–7)

The first cut covers turn/tree/schedule/content — the daemon-owned core. Tree CRUD moved into §4 in
Stage 5b (2026-07-24) since it was a hard prerequisite, not an optional fold-in. Remaining gaps
migrate when their consumers move off direct service calls (primarily during the `SessionViewModel`
decomposition, Stage 7, and the still-undesigned turn-streaming work):
- **Interactive turn streaming** — `IClaudeProcessManager.SendMessageAsync`,
  `IAgentMcpServer.EnsureConfigFileAsync`. `SessionViewModel` keeps these as direct dependencies
  (§5) until a `turn.event` streaming design exists.
- **Model/effort/profile selection** — model catalog, profile lifecycle (`IClaudeModelService`,
  `IClaudeProfileService`).
- **Usage** — `IClaudeUsageService`.
- **Global search + title generation** — currently the in-app `ITessynDaemonService` path; these are
  the features that must be re-provided over TDCP before Stage 6 removes that path
  (see memory: "Tessyn in-app path not fully dead").

## 7. Open confirmations

1. ✅ **D4 confirmed (2026-07-19):** TDCP as its **own** protocol — JSON-RPC 2.0 over Streamable
   HTTP — co-hosted with the MCP server. Settled; §3 updated.
2. **Streaming:** `turn.event` push model for live assistant output — §5 (rides SSE per §3).
3. **S3 (realised 2026-11-12, §8):** the daemon→web-server link reuses this same TDCP JSON-RPC schema over an
   outbound HTTPS connection with an SSE downstream (design implication only; not built during
   the split) — see refactoring-plan.md §0.2.

## 8. Remote API v1 and the S3 relay (2026-11-12)

> Realises the S3 topology of §3 for the web client (FR.25 in `requirements.md`). This section is the reference for clients. The loopback `/tdcp` endpoint is unchanged.

### 8.1 Transports and the remote-callable flag

| Transport | Carrier | Who calls | Methods accepted |
|---|---|---|---|
| `Loopback` | `POST /tdcp` on `127.0.0.1`, `X-CMX-Token` | the Avalonia UI (Wire client), local tools | every registered method |
| `Relay` | the Daemon Link (§8.5): requests arrive on `GET /link/stream` of the web server, replies go to `POST /link/reply` | Tessyn Web through the Web Relay | only methods flagged **remote-callable** (the 21 below) |

One dispatcher (`TdcpDispatcher`) serves both. On `Relay` any other method gets `-32601`. The Web Relay also keeps its own allowlist of the same names. Feature modules register methods through `IRemoteRpcModule`.

### 8.2 Conventions

- JSON-RPC 2.0. camelCase properties, enums as camelCase strings, nulls omitted, times UTC ISO-8601.
- Every tree node has a 32-hex-digit `id` (`Guid.ToString("N")`); sessions use their `NodeId`, directories and groups their new persisted `NodeId`. No path, token, file name or profile config directory is sent.
- Errors: `-32700` parse, `-32600` invalid request, `-32601` method not found or not remote-callable, `-32602` invalid params, `-32000` daemon error (user-facing message), `-32603` internal. Server-made: `-32001` daemon offline, `-32002` daemon timeout.

Shared types:

```
SessionStateDto     { id, running, streamingAlive, attention?: RowAttentionDto, favorite, lastActivityUtc?, lastPromptUtc?, hasDraft, profileName? }
RowAttentionDto     { count, topKind }                    // all open items, Decided included; omitted when 0
AttentionSummaryDto { openCount, topKind?, allOpenCount, allOpenTopKind? }   // openCount excludes Decided
AttentionKindWire   "critical" | "ask" | "todo" | "alert" | "decided"
TreeSessionDto      { id, name, workerCount, supervisorId?, state }
SessionRowDto       { id, name, state }
SessionGroupDto     { directoryId, directoryLabel, sessions: SessionRowDto[] }
```

### 8.3 Methods

| Method | Params | Result |
|---|---|---|
| `client.hello` | — | `{ protocolVersion: 1, daemonVersion, machineName, runId, serverTimeUtc }` |
| `tree.snapshot` | `{ sinceVersion?: long }` | `{ version, unchanged?: true, directories?: TreeDirectoryDto[] }`; `TreeDirectoryDto { id, label, isExpanded, groups, sessions }`, `TreeGroupDto { id, name, isExpanded, groups, sessions }`; top-level sessions only |
| `tree.workers` | `{ supervisorId }` | `{ supervisorId, workers: TreeSessionDto[] }` |
| `tree.search` | `{ query, exact?: bool or null, scopeNodeId?: string or null }` | `{ query, effectiveExact, autoExact, usedFallback, matches: [{ id, strict, loose, matchedSelf, workerIds }] }` ranked best first |
| `tree.createSession` | `{ anchorId, name }` | `{ session: TreeSessionDto, directoryId, groupId? }` — anchor session: right after it; group or directory: first |
| `recent.list` | — | `{ serverTimeUtc, sessions: [{ id, name, directoryLabel, state, hasSchedules, pinned, canHide }] }` (Recent dropdown rules) |
| `recent.hide` | `{ nodeId }` | `{ hidden }` (refuses pinned sessions) |
| `tags.search` | `{ text, scopeNodeId?, selected: TagRefDto[] }` | `{ emptyReason?, chips: TagChipDto[], sessionsEmptyReason?, groups: SessionGroupDto[] }`; `TagRefDto { key, label, url, kind }`, `TagChipDto { …, sessionCount, selected }`, `kind` = `linearIssue`, `gitHubPullRequest`, `gitHubRepository` |
| `session.get` | `{ nodeId }` | `{ id, name, directoryId, groupId?, location: { directoryLabel, treePath }, supervisorId?, state, tags: SessionTagDto[], attention: AttentionSummaryDto, lastUsed: { modelId, effort, profileId, profileName }, isStreamingMode, entryCount, epoch }` |
| `session.entries` | `{ nodeId, beforeIndex?, afterIndex?, limit? }` | `{ nodeId, epoch, total, entries: SessionEntryDto[] }`; `SessionEntryDto { index, timestamp, role, content, truncated?, fullLength?, profileName?, modelId?, effort? }`; limit default 50, max 200; content cap 32,768 |
| `session.entry` | `{ nodeId, index, epoch }` | `SessionEntryDto` (full); `-32000 "Session history changed"` on epoch mismatch |
| `session.send` | `{ nodeId, text, modelId?, effort?, profileId? }` | `{ accepted: true, queued }`; returns at once, the turn runs headless with `TurnSource.Web` |
| `session.stop` | `{ nodeId }` | `{ stopped }` (kills a live Persistent Session Process, cancels a headless turn; cannot cancel a desktop-interactive one-shot turn nor a compaction) |
| `session.compact` | `{ nodeId, modelId?, effort?, profileId? }` | `{ accepted: true, queued }` |
| `session.setFavorite` | `{ nodeId, value }` | `{ value }` |
| `session.setStreaming` | `{ nodeId, enabled, force? }` | `{ enabled, needsConfirm }` |
| `attention.list` | `{ nodeId }` | `{ nodeId, summary, items: AttentionItemDto[] }`; `AttentionItemDto { id, number, kind, title, body?, options, recommendation?, status, answer?, resolutionNote?, createdUtc, updatedUtc, closedUtc? }` |
| `attention.reply` | `{ nodeId, answers: [{ itemId, text }], modelId?, effort?, profileId? }` | `{ answeredIds, skippedIds, turnStarted }` |
| `attention.dismiss` | `{ nodeId, itemId }` | `{ dismissed }` |
| `catalog.get` | — | `{ models: [{ id, displayName, provider }], efforts: ["default","max","high","medium","low"], profiles: [{ id, displayName }] }`; `default` entries first |
| `usage.get` | `{ profileId? }` | `{ profileId, profileName, state: "ok", "pending", "unauthorized" or "unavailable", fiveHour?, sevenDay?, fetchedAtUtc? }`; window `{ utilization, resetsAtUtc, isStale }` |

Notes: `lastUsed.modelId` and `profileId` are `"default"` when unset (the Default profile's `profileName` is `[Sys] <email>`, or `[Sys] Default` until the email is known); `effort` is `default`, `max`, `high`, `medium` or `low`. Entry `index` is the position in the parsed list (all entries, empty ones included). `epoch` increases when the session file is rewritten.

### 8.3a As built: where the implementation refines the contract (2026-11-13)

- **Parameters.** A missing or mistyped param is `-32602` (also for the 17 legacy loopback methods, which used to answer `-32000`). `params` must be a JSON object or absent. A user-facing failure is `TdcpException` → `-32000` with its message; a parameter problem is `-32602` (`TdcpInvalidParamsException`); any other exception, including a stray `FormatException`/`JsonException`/`InvalidCastException` from a handler, is `-32603 "Internal error"` (logged, message not leaked). `DateTimeOffset` values serialise as `…+00:00` (valid ISO-8601).
- **`client.hello`.** `daemonVersion` is the entry assembly's informational version without the `+commit` suffix. `serverTimeUtc` lets the client correct its clock (the web uses the offset for recency).
- **`tree.snapshot`.** `version` is read **before** the snapshot is built; `sinceVersion == version` gives `{ version, unchanged: true }`. The web always sends a full snapshot request (no `sinceVersion`) after a gap. Workers and sessions with a resolvable supervisor are excluded from the top-level lists; supervisor cycles stop at the first repeated session.
- **`tree.workers`** returns **direct** workers only, in the desktop display order (groups first, then sessions per container).
- **`tree.search`.** The file-scan fallback is not strict-filtered (it yields loose hits only), as on the desktop; if the index and the fallback both fail, `-32000 "Search is unavailable"`. An unknown `scopeNodeId` (here and in `tags.search`) gives `-32000 "Session not found"`. The web always sends `exact` explicitly.
- **`tree.createSession`.** One registry call, one `Save()`; the event carries the index and the preceding session. An unknown anchor creates nothing (`-32000`). Empty name: `-32602`.
- **`recent.hide`.** A web hide is persisted and visible in `recent.list`, and raises `SessionSettingsChanged` so the desktop's node refreshes its own `RecentIgnoredAt` at once (the first build needed a restart). Pinned: `{ hidden: false }`.
- **`tags.search`.** Built from the same tree snapshot as the scope resolution (`SessionTagIndexBuilder`); chips whose URL is outside the allowed policy carry `url: ""`; `TagChipDto` has no `source`; selected tags are de-duplicated case-insensitively.
- **`session.send`, `attention.reply`, `session.compact`.** `queued` = the session's running flag is set **or** another web turn was accepted and has not finished. Per-turn `modelId` / `effort` / `profileId` are validated (`-32602`); `"default"` = none. When the turn is redirected into a live streaming process the options are ignored (logged). Every headless USER entry is stamped `profile="<display name>"`.
- **`session.compact` options.** Resolved by the shared `ITurnOptionsResolver`; an unknown `modelId` is `-32602` like in `session.send`.
- **`session.compact` outcomes.** `Nothing to compact` and `Failed` append a SYSTEM line (remote path only) and publish `turn.completed`; success rewrites the file (`session.reset`) and also publishes `turn.completed`.
- **`session.setStreaming`.** `needsConfirm` answers `enabled` = the current value and raises no event; a change publishes `session.changed`.
- **`attention.reply`.** Open Decided items count as answerable (as the desktop's reply cards do); blank, repeated, unknown and closed answers go to `skippedIds`; invalid options are rejected before anything is marked.
- **`usage.get`.** No `profileId` means `"default"`. A fetch longer than 8 s answers `state: "pending"` and continues (then `usage.changed`); failures retry after 1 min, 401 after 5 min; `unauthorized` with an older reading returns that reading as `ok`. The default profile and the stored profile ids behave as in `catalog.get`.
- **`session.entries` / `session.get`.** Parsed lists are cached per file (validated by length and write time, 16 files). Activity is cached per file stamp: an append-only change is parsed from the cached length and merged; a cold entry reads the 64 KB tail, widened once to 4 MB when it holds no USER entry (then the prompt is null); the draft file is read only when its stamp changes. `lastActivityUtc` = max(last USER timestamp, last ASSISTANT timestamp, non-blank draft timestamp) from real entry timestamps; `lastResponseUtc`/`lastPromptUtc` come from the 64 KB tail and can read as null when older than the tail.
- **`session.get.lastUsed`** is built by one resolver that mirrors the three desktop `RestoreFromEntry` methods.

### 8.4 Notifications (daemon → relay → browsers)

Sent as JSON-RPC notifications (`method`, `params`, no `id`); the Relay Connector posts them in batches to `/link/events`; the server fans them out on `GET /api/events`.

| Method | Params | Trigger |
|---|---|---|
| `tree.changed` | `{ version }` | structural tree change (debounce 300 ms) |
| `session.changed` | `{ state: SessionStateDto }` | running flag, attention, favourite/streaming, entry appended (debounce 250 ms per session) |
| `session.appended` | `{ nodeId, epoch }` | entry appended (debounce 250 ms per session) |
| `session.reset` | `{ nodeId, epoch }` | session file rewritten |
| `turn.completed` | `{ nodeId, cancelled, error? }` | turn ended |
| `attention.changed` | `{ nodeId, summary }` | Focus items changed |
| `tags.changed` | `{ nodeId? }` | tags changed |
| `usage.changed` | `{ profileId }` | usage fetched with new data |
| `index.changed` | `{}` | search index updated (debounce 2 s) |

Server-made, never sent by the daemon: `link.status { online, since }` (first frame of every browser stream) and `resync {}`.

**Ordering and coalescing.** `turn.completed` and `session.reset` use a zero window (published at once, off the calling thread); `session.appended` / `session.changed` use 250 ms, but the daemon **flushes** the session's pending `session.appended`/`session.changed` before it sends `turn.completed` (`IRemoteEventBus.FlushPending`), so they normally arrive first; a timer already firing at that moment can still land just after it. A client refreshes on `turn.completed` and still handles a late `session.appended`. `session.reset` reads the epoch after the bump. Windows: tree 300 ms, session 250 ms, attention 250 ms, tags 250 ms, index 2 s. `usage.changed` is published by the usage service.

### 8.5 Link protocol (Web Relay ⇄ Relay Connector)

- `GET /link/stream` — headers `Authorization: Bearer <link token>`, `X-Tessyn-Daemon: <machine name>`, `X-Tessyn-Protocol: 1`. `text/event-stream` with `X-Accel-Buffering: no`; each frame `data: <JSON-RPC request>`; a `: ping` comment every 15 s. One link at a time: a new connection supersedes the old one (the old stream ends, its pending requests fail `-32001`). 401 for a wrong token.
- `POST /link/reply` — bearer token; body = JSON array of JSON-RPC responses; 204.
- `POST /link/events` — bearer token; body `{ "events": [ { "method", "params" } ] }`; 204.
- Request ids on the link are server-generated (`r-<n>`) and mapped back to the browser's id. Server timeout 30 s (60 s for `session.entries`, `tree.snapshot`, `tags.search`, `tree.search`).
- Connector: backoff 1, 2, 5, 10, then 30 s (cap), reset after 60 s connected; 45 s without any frame = dead link; at most 8 requests at once; notifications coalesced for 250 ms per POST; notifications made while offline are dropped (browsers resync on `link.status`).
- Frames as built: requests are `data: {"jsonrpc":"2.0","id":"r-N","method":…,"params":…}` (no `event:` line, `params` omitted when absent); `resync` is a normal data frame `{"method":"resync","params":{}}` carrying `id:` = the latest sequence number; the first `link.status` frame has no `id:`. `/link/events` ignores daemon-posted `link.status`/`resync` and any `seq`. The connector replies with a one-element array per request; frames without an `id` get no reply. Events are posted after 250 ms as one `{ "events": [...] }`; a failed POST is logged and dropped.
- The token is compared as SHA-256 (of the UTF-8 bytes of the token text exactly as sent in the `Bearer` header, lowercase hex) against the configured hash, in constant time. On the PC the config is `%AppData%\TessynDesktop\web-relay.dat` (DPAPI, current user): `{ "enabled", "url", "token" }`.

### 8.6 Browser API (Tessyn.Web)

`POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`; `POST /api/rpc` (cookie plus header `X-Tessyn-Client: web`); `GET /api/events` (cookie; `id:` = server sequence, ring buffer of 1,000 events or 10 minutes, `Last-Event-ID` replay or `resync`); `GET /healthz` (no auth: `{ ok }`; `linkOnline` only for a loopback caller). Every `POST /api/*`, login and logout included, needs `X-Tessyn-Client: web` (else 400). Cookies are `__Host-Tessyn.Auth` and `__Host-Tessyn.Device`; logout logs out every device; body limits 4 KB login, 1 MB rpc, 4 MB link. Details: FR.25.03–FR.25.06, FR.25.28.

---

## 9. Local client additions (2026-11)

Added for the WPF client (`wpf-programme-plan.md`, decision P13: no "TDCP v2"). All additions are additive: the 21 remote methods of the web contract and their result shapes are unchanged (the contract has since grown by three methods, see 9.8), `protocolVersion` stays 1, and the new fields are optional on the wire. Features are announced as capabilities in `client.hello`. Reports with the full wire formats are in `.wpf-work/` (cited per item).

**9.1 Capabilities and hello** (P1.C, report 60). `client.hello` takes optional `{ clientName, clientVersion, protocolVersion, capabilities }` and returns, additively, `capabilities[]`, `minProtocolVersion` and `eventSeq`. Capabilities today: `events.local`, `events.resume`, `entries.ids` (the last only when the daemon has a content store). A client enables a feature only when it is listed.

**9.2 Local event stream, ids and resume** (P1.A and P1.B, report 60). `GET /tdcp` is an SSE stream of the same notifications the relay gets, each with `id: <seq>`. `seq` is monotonic per daemon run, starting at 1, stamped by the Event Log (ring of 5,000 events or 10 minutes). The legacy `tree.changed` and `schedule.changed` frames come only with `?legacy=1`. A client reconnects with `Last-Event-ID` (and `?runId=<hello.runId>`): the daemon replays from the ring, or sends `resync { reason, eventSeq }` with reason `gap`, `unknown-seq` or `overflow`, and the client refetches its state. The Client library adds the reason `run-changed` when a hello after a reconnect shows another run (report 130).

**9.3 Discovery and auth** (P1.C, report 60). `%AppData%\TessynDesktop\daemon.json` (`endpoint`, `pid`, `protocolVersion`, `runId`, `tokenFile`) is written when the server starts and removed on clean exit. The token lives in a DPAPI-protected file (`tdcp-token.dat`), not in `appsettings.json`. `X-CMX-Token` is compared in constant time; a bad or missing token answers HTTP 401 with a JSON-RPC error body; any request that carries an `Origin` header answers HTTP 403.

**9.4 Entry ids** (P1.E to P1.G, reports 90 and 170). `SessionEntryDto` gains `entryId` = `{fileId}:{ordinal}`; `session.get` and `session.entries` results gain `fileId` and `seq`. `session.entries` accepts `afterSeq` (resume from the N entries a client already holds) and `beforeEntryId`, with an optional `fileId` check; a stale id fails with `-32000 "Session history changed"`. `session.entry` accepts an `entryId` and an optional character `range`. `session.reset` carries the new `fileId`. `session.get` reads its header from the content store's sidecar index and no longer parses the transcript.

**9.5 `entries.appended`** (P1.F, report 170). Published beside `session.appended` with the same 250 ms coalescing: `{ nodeId, fileId, fromSeq, toSeq, entries? }`. Entries are inline only for at most 50 entries within 65,536 bytes; otherwise the notification carries ids only and the client fetches with `afterSeq`. **Local-only**: the relay connector drops it; phones keep using `session.appended`.

**9.6 `session.headers`** (P1.G, report 170). Loopback only (`remoteCallable: false`, listed in `RemoteProtocol.LoopbackMethods`, not in `Methods`; answers `-32601` on the relay). `{ nodeIds[] }` (at most 500) returns per-session `{ fileId, epoch, entryCount, seq, firstTimestamp, lastTimestamp, lastUsed, contentVersion, fileLength }` and a `missing` list.

**9.7 Focus references** (report 230, FR.23.18). `attention.list` items gain `refs[]` (entry, file and url refs, resolved by the daemon when the item is posted). `session.entries { aroundEntryId }` returns the page centred on an entry (not combinable with the other paging parameters). New `file.read { handle, offset?, length? }` (UTF-8 text or base64, chunked) and `file.preview { nodeId, entryId, path }` (returns a handle for a path that occurs in that entry's text). Handles are opaque; a raw path is never accepted. The `AppSettings.FileAccess` policy applies: denied paths on every transport; type allowlist, size and chunk caps, audit log and the `ReferencedOnly` / `WorkingDirectory` mode (decision A11) on the relay only. These three are open on the relay for Tessyn Web since 2026-11-15 (section 9.8).

**9.8 Attachments, profile switch, media hosts and the web contract** (2026-11-15; reports `.wpf-work/300-attachments-daemon.md`, `300b-attachments-fixes.md`, `320-issue013-daemon.md`, `320b-issue013-fixes.md`, `340-wpf-focus-previews.md`; requirements FR.31, FR.12.27, FR.12.28, FR.26.4).

- **`attachment.import { nodeId, sourcePath }`** (loopback only: `remoteCallable: false`, listed in `RemoteProtocol.LoopbackMethods.AttachmentImport`; the relay answers `-32601`). A relative `sourcePath` resolves against the session's working directory. Result: the attachment object `{ attachmentId, path, name, size, mime, createdUtc }`. It refuses a missing file, a non-local (UNC) path, a file the relay deny rules would refuse, another session's attachment, a file with more than one hard link and a file over 200 MB.
- **`attachment.list { nodeId }`** (loopback and relay; on the browser allowlist). Result: `{ nodeId, attachments: [ { attachmentId, path, name, size, mime, createdUtc } ] }`, newest first, across the session's profile folders.
- **`attachment.fetch { blobId, nodeId, name, size, sha256 }`** (relay only, called by the web server; not on the browser allowlist, so a browser gets `-32601`). The daemon downloads `GET {server}/link/blob/{blobId}` with its link token, stops at `size` bytes, checks size and SHA-256 and stores the file. Result: the attachment object. Failures are JSON-RPC errors that the server turns into HTTP `422`: `Session not found`, `The file is larger than the 200 MB limit`, `The download is incomplete or the wrong size (n of m bytes)`, `The download is larger than announced`, `The downloaded file does not match its SHA-256`, `The upload is no longer available on the web server`, `The web link is not connected`. After 240 s the daemon answers `-32002` (`TdcpTimeoutException`); the server maps it to HTTP `504`.
- **`file.read { handle }`** also accepts an `attachmentId` as the handle. The relay policy applies (type allowlist, size caps, chunk cap, audit log, denied list), and the file must be directly in `attachments\<profile>\<node>\` with one hard link. The `name` in the result is the on-disk name with the timestamp prefix.
- **`profile.switch { nodeId, profileId, includeDescendants? }`** (loopback only: `remoteCallable: false`, `RemoteProtocol.LoopbackMethods.ProfileSwitch`; the relay answers `-32601`; not part of the web contract). `profileId` is `"default"` or a stored `ProfileId`; `includeDescendants` defaults to `false`. Result `ProfileSwitchResultDto`: `{ profileId, profileName, nodeMoved, moved[], queued[], failed[], oldConfigPaths[] }`. `moved` and `queued` items are `{ nodeId, name, fromProfileId, fromProfileName }`; `failed` items are `{ nodeId, name, paths[] }`. `moved` are the descendants moved now; `queued` are running sessions (descendants, or the node itself) whose switch applies when their next turn starts; `nodeMoved` is `false` when the node itself is queued or was already on the profile. Errors, all `-32602`: missing or mistyped parameters, `Session not found`, `Profile not found: <id>`.
- **`catalog.get`** gains the optional `mediaPreviewHosts: string[]`: the hosts whose https images load without a click (`host` matches exactly, `*.host` matches subdomains only; lower-case and trimmed). The default is `["annielartens.online", "*.githubusercontent.com"]` from `AppSettings.MediaPreviewHosts`. A client that gets no list treats every host as click-to-load.
- **`lastUsed.profileId` and `lastUsed.profileName`** in `session.get` and `session.headers` (9.6) follow the node's `ProfileId` pin, and a queued target wins; they no longer come from the stale history name. For the Default pin `profileName` is `[Sys] Default` unless the history already says Default. A client sends a per-turn `profile` only when the user changed it.
- **`turn.run` results** gain the optional `note` (a visible remark, for example that a legacy profile label ran on Default) and `refused` (the turn was refused before it ran). Both are additive.
- **Web contract.** The browser allowlist is now **24 methods**: the 21 of the earlier contract plus `file.read`, `file.preview` and `attachment.list` (`session.entries` with `aroundEntryId` is a new parameter of an existing method). Two HTTP endpoints sit beside it: `POST /api/attachments` (authenticated browser upload) and `GET /link/blob/{blobId}` (daemon only, link token). Request and response formats of the upload endpoint are in requirements FR.31.3 and FR.31.7. `protocolVersion` stays 1.

Test coverage: the contract suite (`code/TessynDesktop.Tests/Contract`) runs against the in-process module and loopback HTTP, with golden result shapes; the web-compatibility proofs in `SessionHeadersAndCompatibilityTests` show that the existing 21 methods answer as before.
