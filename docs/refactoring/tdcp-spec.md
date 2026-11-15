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
3. **S3 (future):** the daemon→web-server link reuses this same TDCP JSON-RPC schema over an
   outbound HTTPS connection with an SSE downstream (design implication only; not built during
   the split) — see refactoring-plan.md §0.2.

---

## 9. Local client additions (2026-11)

Added for the WPF client (`wpf-programme-plan.md`, decision P13: no "TDCP v2"). All additions are additive: the 21 remote methods of the web contract and their result shapes are unchanged, `protocolVersion` stays 1, and the new fields are optional on the wire. Features are announced as capabilities in `client.hello`. Reports with the full wire formats are in `.wpf-work/` (cited per item).

**9.1 Capabilities and hello** (P1.C, report 60). `client.hello` takes optional `{ clientName, clientVersion, protocolVersion, capabilities }` and returns, additively, `capabilities[]`, `minProtocolVersion` and `eventSeq`. Capabilities today: `events.local`, `events.resume`, `entries.ids` (the last only when the daemon has a content store). A client enables a feature only when it is listed.

**9.2 Local event stream, ids and resume** (P1.A and P1.B, report 60). `GET /tdcp` is an SSE stream of the same notifications the relay gets, each with `id: <seq>`. `seq` is monotonic per daemon run, starting at 1, stamped by the Event Log (ring of 5,000 events or 10 minutes). The legacy `tree.changed` and `schedule.changed` frames come only with `?legacy=1`. A client reconnects with `Last-Event-ID` (and `?runId=<hello.runId>`): the daemon replays from the ring, or sends `resync { reason, eventSeq }` with reason `gap`, `unknown-seq` or `overflow`, and the client refetches its state. The Client library adds the reason `run-changed` when a hello after a reconnect shows another run (report 130).

**9.3 Discovery and auth** (P1.C, report 60). `%AppData%\TessynDesktop\daemon.json` (`endpoint`, `pid`, `protocolVersion`, `runId`, `tokenFile`) is written when the server starts and removed on clean exit. The token lives in a DPAPI-protected file (`tdcp-token.dat`), not in `appsettings.json`. `X-CMX-Token` is compared in constant time; a bad or missing token answers HTTP 401 with a JSON-RPC error body; any request that carries an `Origin` header answers HTTP 403.

**9.4 Entry ids** (P1.E to P1.G, reports 90 and 170). `SessionEntryDto` gains `entryId` = `{fileId}:{ordinal}`; `session.get` and `session.entries` results gain `fileId` and `seq`. `session.entries` accepts `afterSeq` (resume from the N entries a client already holds) and `beforeEntryId`, with an optional `fileId` check; a stale id fails with `-32000 "Session history changed"`. `session.entry` accepts an `entryId` and an optional character `range`. `session.reset` carries the new `fileId`. `session.get` reads its header from the content store's sidecar index and no longer parses the transcript.

**9.5 `entries.appended`** (P1.F, report 170). Published beside `session.appended` with the same 250 ms coalescing: `{ nodeId, fileId, fromSeq, toSeq, entries? }`. Entries are inline only for at most 50 entries within 65,536 bytes; otherwise the notification carries ids only and the client fetches with `afterSeq`. **Local-only**: the relay connector drops it; phones keep using `session.appended`.

**9.6 `session.headers`** (P1.G, report 170). Loopback only (`remoteCallable: false`, listed in `RemoteProtocol.LoopbackMethods`, not in `Methods`; answers `-32601` on the relay). `{ nodeIds[] }` (at most 500) returns per-session `{ fileId, epoch, entryCount, seq, firstTimestamp, lastTimestamp, lastUsed, contentVersion, fileLength }` and a `missing` list.

**9.7 Focus references** (report 230, FR.23.18). `attention.list` items gain `refs[]` (entry, file and url refs, resolved by the daemon when the item is posted). `session.entries { aroundEntryId }` returns the page centred on an entry (not combinable with the other paging parameters). New `file.read { handle, offset?, length? }` (UTF-8 text or base64, chunked) and `file.preview { nodeId, entryId, path }` (returns a handle for a path that occurs in that entry's text). Handles are opaque; a raw path is never accepted. The `AppSettings.FileAccess` policy applies: denied paths on every transport; type allowlist, size and chunk caps, audit log and the `ReferencedOnly` / `WorkingDirectory` mode (decision A11) on the relay only. Opening these three on the relay for Tessyn Web needs an FR.25 amendment; until then they are loopback-only in practice.

Test coverage: the contract suite (`code/TessynDesktop.Tests/Contract`) runs against the in-process module and loopback HTTP, with golden result shapes; the web-compatibility proofs in `SessionHeadersAndCompatibilityTests` show that the existing 21 methods answer as before.
