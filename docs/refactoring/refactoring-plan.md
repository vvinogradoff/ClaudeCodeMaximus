# ClaudeMaximus — General Refactoring Plan (UI / Daemon Split)

> **Sunset note (2026-11-17).** Historical plan. The former desktop UI named below is retired; WPF is the only desktop client and `code/TessynDesktop` remains only as the daemon host (`TessynDesktop.exe --background`). See `wpf-programme-plan.md`.

> Working document (not authoritative, not committed). Author: Developer/Claude.
> Companion to `structure-notes.md`. Snapshot: 2026-07-18.

## 0. Decisions this plan assumes

| # | Decision | Value assumed | Set by |
|---|---|---|---|
| D1 | Tessyn dual-path resolution | **Split app into UI + .NET daemon** | Architect ✔ |
| D2 | `SessionNodeModel` ownership | **Extract `ISessionRegistry` service** | Architect ✔ |
| D3 | UI direction | **Split first; keep Avalonia thin; WPF is a later, separate project (not WinForms)** | Architect ✔ (2026-07-18) |
| D4 | UI↔daemon control API | **TDCP is its own protocol — JSON-RPC 2.0 over Streamable HTTP — separate from the claude-callback MCP server; both hosted in one daemon process** | Architect ✔ (2026-07-19) — see §0.2 |
| D5 | Stage 5 sequencing | **The actual process split (separate exe) is deferred and may not be needed at all** ("one exe can serve UI and daemon, I keep it running anyway"). Priority is migrating `SessionTreeViewModel`/`SessionViewModel` fully onto `IDaemonClient`, still in one process (`InProcDaemonClient`). Revisit the exe split only after that migration is complete and verified. | Architect ✔ (2026-07-24) |

> **D3 is settled (Architect, 2026-07-18):** WPF (not WinForms), built *later* as a
> separate thin client once the daemon boundary exists. Avalonia stays the sole client
> through the split and is kept thin. **NR.1 consequence:** WPF is Windows-only, and the
> build TFM is already `net9.0-windows10.0.17763.0` (see `code/CLAUDE.md`) — the app is
> **already Windows-only in practice**. Choosing WPF makes that explicit rather than
> introducing a new restriction; NR.1 should be reconciled to Windows-only (Architect
> owns that requirements edit — see Open confirmation #4).
>
> **D4 is proposed, NOT yet confirmed — see §0.2.** The Architect's three-connection
> topology (a local UI plus a daemon that dials *out* to a web server which then operates
> it) moved the recommendation away from "extend the MCP server" toward a **separate TDCP
> protocol over WebSocket**, co-hosted with the MCP server in one daemon process. **Stage 5
> stays blocked until the Architect confirms D4.**

## 0.1 Naming (resolve the three-way "Tessyn" collision)

Three distinct things currently blur together:
- **(a)** the abandoned *in-app* daemon code path (`SendViaDaemonAsync`, `TessynRunService`, `TessynDaemonService`, ~15 `Tessyn*` models);
- **(b)** the external **NodeJS** project `C:\Projects\Personal\TessynDaemon` (github.com/sapph1re/tessyn);
- **(c)** the **new .NET daemon** this plan creates.

Proposed names, used throughout: new .NET daemon = **`TessynNetDaemon`** (host exe "tessynnetd"); its wire contract = **Tessyn Daemon Control Protocol (TDCP)**; the NodeJS project = **tessyn** (an *alternative* TDCP backend, if kept). The in-app path (a) is **removed** — the UI always talks to a daemon over TDCP; tessyn (b) becomes one TDCP implementation.

## 0.2 D4 analysis — the three wire surfaces (topology from Architect, 2026-07-18)

The Architect's target topology has **three** distinct connections, not one. Separating
them is what drives the D4 recommendation:

| # | Link | Who dials whom | Who commands whom | Protocol |
|---|------|----------------|-------------------|----------|
| **S1** | `claude` → daemon | claude dials daemon | claude calls tools | **MCP** (the 8 agent tools) |
| **S2** | local UI → daemon | UI dials daemon (loopback) | UI commands daemon | **TDCP** |
| **S3** | daemon ↔ web server | **daemon dials out** | **web server commands daemon** | **TDCP** |

- **S1 is a genuinely different surface** — LLM tool-calls (MCP spec, dictated by what
  `claude` expects). It stays exactly as-is: the daemon is the MCP *server*, `claude` is the
  client that dials it. Non-negotiable.
- **S2 and S3 are the same logical protocol (TDCP)** — read the tree, subscribe to changes,
  run turns, manage schedules — merely carried over two different topologies.

**Why this pushes D4 to a separate protocol (not "extend MCP"):** S3 is the decider. The
daemon sits behind NAT on the Architect's machine, so it must **initiate** the connection
(outbound `wss://`), yet the web server **commands** it once the pipe is up — a reverse-tunnel
/ device-agent role inversion (cf. CI runners, VS Code tunnels, TeamViewer). MCP's transport
model is strictly client-dials-server, so folding TDCP into MCP would force a bespoke tunnel
to invert roles for S3 anyway. If instead **TDCP is a plain symmetric JSON message protocol
(request/response + event envelopes) over a full-duplex socket**, then S2 (UI dials daemon)
and S3 (daemon dials web server) share one identical message schema — only the
socket-establishment side differs.

**"Separate protocol" ≠ "separate infrastructure":** both surfaces live in one daemon
process, one Kestrel host, even one loopback port, on different paths — `/mcp` (S1) and
`/tdcp` (S2, WebSocket). That preserves the "least new infrastructure" benefit that originally
favored extend-MCP.

**Transport — DECIDED (Architect, 2026-07-19): JSON-RPC 2.0 over Streamable HTTP.** TDCP messages
are JSON-RPC 2.0 (requests/responses correlated by `id`; events as notifications), carried over
**MCP-style Streamable HTTP** — a single endpoint that answers a POST with either a single
`application/json` body or a `text/event-stream` (SSE) stream, and serves server→client push as an
SSE stream on a GET. Chosen because it (a) **unifies with MCP's own modern transport** — the daemon
already speaks JSON-RPC, so `/mcp` and `/tdcp` share one transport family, differing only in method
schema; (b) is **maximally portable** — every stack has an HTTP client + SSE, so the NodeJS `tessyn`
port and third-party clients are trivial; (c) traverses NAT/firewall/proxy as ordinary HTTPS.

**How Streamable HTTP serves each topology:**
- **S2 (UI → daemon):** UI is the HTTP client. It POSTs JSON-RPC requests to `/tdcp` (single-JSON or
  SSE response for streaming turns) and holds a GET SSE stream for unsolicited push
  (`TreeChanged`/`ScheduleChanged` as JSON-RPC notifications). Auth: `X-CMX-Token` header.
- **S3 (daemon → web server), the inversion:** the daemon **dials out** over HTTPS, issues a GET that
  returns an SSE stream, the web server pushes JSON-RPC **requests** (commands) down that stream, and
  the daemon POSTs JSON-RPC **responses/events** back up. So the daemon stays the JSON-RPC *server*
  even though it opened the TCP/TLS connection — the standard reverse-tunnel-over-HTTPS pattern, fully
  expressible in Streamable HTTP without a bespoke protocol.

This **supersedes** both the earlier "long-poll" note (S2-only) and last revision's raw-WebSocket
recommendation. (WebSocket would also work, but Streamable HTTP is the better fit for the
"fit every modern tech stack" goal and reuses MCP's transport rather than introducing a second one.)

**Scope note:** the S3 web-server connector is a *future* stage, not built during the split.
But choosing WebSocket now means S3 later reuses the S2 message schema instead of inventing a
second protocol.

---

## 1. Target Architecture

```
┌──────────────────────────────────────────┐
│ ClaudeMaximus.App  (Avalonia UI, exe)     │  thin client
│   Views + ViewModels + IDaemonClient      │
└───────────────────┬──────────────────────┘
                    │  Tessyn Daemon Control Protocol (JSON-RPC over Streamable HTTP, loopback, token-auth)
┌───────────────────▼──────────────────────┐
│ TessynNetDaemon  (host exe, "tessynnetd")  │  lifecycle, MCP/TDCP server,
│   owns process lifetime, self-update,     │  scheduler, missed-fire, claude spawn
│   single-instance, loopback auth          │
├───────────────────────────────────────────┤
│ ClaudeMaximus.Core  (class library)        │  NO Avalonia dependency
│   ISessionRegistry  ← owns SessionNodeModel tree (source of truth)
│   turn engine · scheduler · orchestration/MCP tools · process mgr
│   session files · model/profile/usage · code index · import · assist
│   IUiDispatcher (abstraction, replaces Dispatcher.UIThread in services)
└───────────────────────────────────────────┘
                    ▲
                    │ TDCP (same protocol)
        (optional) tessyn NodeJS  — alternative backend
        (later)    ClaudeMaximus.Wpf — additional thin client (D3, Windows-only)
```

**Invariant that makes porting to tessyn possible:** the daemon's entire behavior is
expressed through **TDCP + the MCP tool schemas**. Keep `docs/` describing that contract
and any NodeJS re-implementation has a spec to hit.

**The Avalonia-free test:** `ClaudeMaximus.Core` must compile and unit-test with **no
Avalonia package reference**. That single constraint enforces the whole separation.

---

## 2. Strangler Sequence (each stage keeps the app shippable)

Ordering rationale: **decouple → cover with tests → relocate → thin the UI**. We never
decompose a god object before its seam and tests exist.

Legend: 🟢 Developer can do · 🟡 **Architect approval/action** (project files, output
type, cross-project refs, requirements docs, Program.cs — all "limited responsibility"
per CLAUDE.md).

---

### Stage 1 — `ISessionRegistry` extraction (in-place, no new projects) 🟢
**Goal:** move the *authoritative* `SessionNodeModel` tree out of `SessionTreeViewModel`
into a service. This is the keystone: it fixes the layering violation, dissolves the
worst of the `Lazy<>` cycles, and makes the tree daemon-ownable.

**New:** `Services/ISessionRegistry.cs` + `SessionRegistry.cs`. Owns the tree of
`DirectoryNodeModel`/`GroupNodeModel`/`SessionNodeModel`, persists via
`IAppSettingsService`, raises a `TreeChanged` event.

**Move from `SessionTreeViewModel` (model-level ops) → `SessionRegistry`:**
- `LoadFromSettings`, `BackfillAgentIdentity(+InGroups)`, `ReorganizeSubSessions`
- model side of Add/Rename/Delete (`AddDirectory/Group/Session…`, `RenameGroup/Session`, `TryDelete*`)
- model-level move (`RemoveSessionFromTree`, `InsertSessionAt`, `MoveSessionTo` state changes)
- `CollectAllClaudeSessionIds`, `FindModelByNodeId`, `FindModelByAgentToken`, `CreateSessionForAgent`, `BuildAgentSessionSummaries`, `GetSessionLocation`

**Stays in `SessionTreeViewModel` (view projection):**
- `ObservableCollection` of node VMs, selection, `IsExpanded`/recency/git-origin display
- move-mode UX (`IsMoveModeActive`, `StartMoveMode/Cancel/Confirm/UpdateMovePosition`)
- search filter (`ApplySearchFilter`, `ShowAll*`), `FindNodeVmByNodeId`
- becomes an **observer** of `SessionRegistry.TreeChanged`, projecting models→VMs on the UI thread

**Repoint to the interface (removes Service→VM dependency):**
- `SessionTurnService(... SessionTreeViewModel → ISessionRegistry ...)`
- `AgentMcpServer(... SessionTreeViewModel → ISessionRegistry ...)`
- `SchedulerService(... SessionTreeViewModel → ISessionRegistry ...)`
- DI: register `ISessionRegistry` singleton; `SessionTreeViewModel` depends on it.

**Bonus fix:** `AgentMcpServer.CreateSessionForAgent` currently does
`Dispatcher.UIThread.InvokeAsync` to mutate the tree (FR.15.4). After this stage the
service mutates the **registry** (thread-safe, UI-free); the VM re-projects on
`TreeChanged`. Removes UI-thread coupling from a "service."

**Exit criteria:** app behaves identically; `SessionTurnService`/`AgentMcpServer`/
`SchedulerService` no longer reference any type in `ViewModels`.

**Risk:** highest-touch stage. Do it behind the running app, verify each tool
(spawn/send/schedule) manually after repoint.

---

### Stage 2 — Test the engine against the new seam 🟢
Now testable without Avalonia. Write unit tests (`ClaudeMaximus.Tests`):
- `ModelTierService.GetTier` table (FR.16.1)
- tier governance rejection in spawn/set_model/orchestrate (FR.16.2)
- guardrails: `MaxOrchestrationDepth`, `MaxConcurrentWorkers`, `MaxTurnsPerLoop` (FR.15.5)
- `OrchestrationBudget` accrual + exhaustion (FR.15.6, FR.17.3)
- schema retry/`[schema-invalid]` path (FR.15.7)
- `SchedulerService` fire / cron re-arm / missed-fire FireOnce (FR.14.6–14.8)
- `SessionRegistry` CRUD + backfill + find-by-token round-trips
- `ClaudeProcessManager` with a mock process stream (long-standing deferred TODO)

Use a `FakeSessionRegistry` and a fake process/transport. **This is the safety net for
everything after.**

---

### Stage 3 — Carve out `ClaudeMaximus.Core` 🟡 (new project + refs)
Pure reorganization, no behavior change.
- 🟡 Create `ClaudeMaximus.Core` class library; add to `.sln`; UI references it.
- 🟢 Move `Models/*` and `Services/*` (incl. `ISessionRegistry`) into Core.
- 🟢 Introduce `IUiDispatcher` in Core; provide an Avalonia impl in the UI project.
  Replace remaining `Dispatcher.UIThread` usages *inside services* with `IUiDispatcher`
  (VMs keep using Avalonia directly).
- 🟢 **Enforce the Avalonia-free test:** Core must build with zero Avalonia reference.
  Anything that fails this (stray `using Avalonia`, converters, `ThemeApplicator`) stays
  in the UI project.

**Exit criteria:** `dotnet build ClaudeMaximus.Core` succeeds with no Avalonia package;
Stage-2 tests still green.

---

### Stage 4 — Define the Tessyn Daemon Control Protocol (still in-process) 🟢🟡
Introduce the boundary the UI will eventually cross, *without moving processes yet*.
- 🟢 `IDaemonClient`: every operation the UI needs — enumerate tree +
  subscribe to changes, send user turn, stop, read session content, model/effort/profile
  selection, schedules list/cancel, usage. (This is the union of what VMs call on
  services today.)
- 🟢 `InProcDaemonClient`: adapter implementing `IDaemonClient` by calling Core
  directly. VMs are refactored to depend **only on `IDaemonClient`**, not on the
  individual services.
- 🟢 Specify TDCP as its **own** protocol (D4 ✔ — see §0.2): **JSON-RPC 2.0 over Streamable
  HTTP** (requests/responses by `id`, events as notifications; POST answered by single-JSON or
  SSE, GET SSE for push), co-hosted with the claude-callback MCP server in one daemon process
  (`/tdcp` vs `/mcp`). Token-auth via the existing `X-CMX-Token` mechanism.

**Exit criteria:** the entire UI talks to Core through one `IDaemonClient` interface;
switching transport later touches no ViewModel.

> **Stage 4 progress (2026-07-18 session) — infrastructure + safety net landed, consumer migration deferred:**
> - ✅ `IDaemonClient` + `InProcDaemonClient` created and DI-registered. Build + tests green.
> - ✅ **Boundary test landed:** `InProcDaemonClientTests.cs` (27 tests) pins delegation of every
>   `IDaemonClient` member to its Core service + the `TreeChanged`/`ScheduleChanged` event relay,
>   using recording fakes. **Suite now 128 tests, all green.** This is the regression guard for the
>   Stage-5 wire adapter (WireDaemonClient must satisfy the same contract).
> - 🔧 **Placement refinement:** both types are Avalonia-free, so they live in
>   **`ClaudeMaximus.Core`** (`Services/Daemon/`), not the UI project as the bullet above
>   said. This makes the boundary adapter unit-testable from `ClaudeMaximus.Tests` (Core-only
>   ref) and lets a future WPF client (D3) share the contract. Avalonia-free gate intact.
> - ✅ TDCP spec drafted: `docs/refactoring/tdcp-spec.md` (method map, change channel options,
>   the `GetTurnLock` "does-not-survive-the-wire" caveat, streaming-turn `turn.event` design).
> - 🔧 **First cut scope:** covers the daemon-owned core (turn / tree-read+notify / schedule /
>   content-read). Tree CRUD, model/profile/usage, and search/titles fold in **as their
>   consumers migrate** — see sequencing note below.
> - ⏭ **Consumer migration deferred (rationale):** the primary consumer of daemon-owned ops is
>   `SessionViewModel` (the 2545-LOC god object). Migrating it wholesale to `IDaemonClient` now
>   would be undone by Stage 7's decomposition (double work). Instead, the Stage-7 extracted
>   controllers (`TurnController`, `ProfileController`, …) will be **born depending on
>   `IDaemonClient`**. `SessionTreeViewModel`/`MainWindowViewModel` can migrate independently.
>   → **Exit criterion "entire UI talks through IDaemonClient" is now met incrementally across
>   Stages 4→7, not all in Stage 4.**

> **Stage 6 sequencing — VERIFIED must stay after Stage 5 (do NOT pull early):** the in-app
> Tessyn path is not fully dead. `ITessynDaemonService` still powers **working** cross-project
> global search (FTS5) and daemon-persisted title generation in `ImportPickerViewModel` (17 refs)
> and `SessionTreeViewModel` (7 refs), gracefully degrading when the NodeJS daemon is absent. The
> Architect's "never worked" refers to the *turn-execution* path (`SendViaDaemonAsync`). Removing
> Tessyn before TessynNetDaemon (Stage 5) re-provides search/titles over TDCP would be a feature
> regression. Confirmed 2026-07-18.

---

### Stage 5 — TDCP wire + VM migration onto `IDaemonClient` 🟢 (exe split deferred, see D5)
- 🟢 (5a, done) TDCP wire transport, in-process.
- 🟢 (5b, redefined per D5) Migrate `SessionTreeViewModel`/`SessionViewModel` onto `IDaemonClient`,
  still in-process (`InProcDaemonClient`).
- 🟡 *Deferred:* `TessynNetDaemon` host exe + `Program.cs` for both host and UI — only if/when the
  exe split actually happens (D5: "might not even be so necessary").
- 🟢 `WireDaemonClient` already implements `IDaemonClient` over TDCP (5a) — ready whenever the split
  is revisited; UI would swap `InProcDaemonClient` → `WireDaemonClient` in DI at that point.

**Original exit criteria (now conditional on the exe split actually happening):** UI and daemon run
as separate processes; closing the UI leaves background/scheduled sessions running in the daemon
(*would finally deliver* FR.5.5 + FR.14.7 properly). **5b's actual exit criterion:** `SessionTreeViewModel`
and `SessionViewModel` depend on `IDaemonClient` (not raw Core services) for tree/turn-lock/schedule
operations, verified by a clean build + manual app run — this is what makes the exe split *safe*
whenever it's revisited, without being required now.

> **Stage 5 breakdown — build the wire in-process first (5a), then split the process (5b):**
>
> **5a — TDCP wire transport, still one process (additive, test-guarded) 🟢:**
> - **Hosting decision:** the existing `AgentMcpServer` `HttpListener` binds the *root* prefix
>   `http://127.0.0.1:{port}/`, so it already receives every path. Route by
>   `ctx.Request.Url.AbsolutePath`: `/mcp` → existing MCP flow (unchanged); `/tdcp` → new
>   `TdcpServer`. This realizes the documented "one process, one loopback port, `/mcp` vs `/tdcp`"
>   design with a single low-risk routing branch — the working MCP path is untouched.
> - **`TdcpServer`** (Core `Services/Daemon/`): JSON-RPC 2.0 over Streamable HTTP for the §4 method
>   map, delegating to an injected `IDaemonClient` (the in-proc one). POST→single-JSON for
>   request/response methods; a GET SSE stream that pushes `tree.changed`/`schedule.changed`
>   notifications by subscribing to the daemon's `TreeChanged`/`ScheduleChanged` events.
>   Auth: existing `X-CMX-Token`.
> - **`WireDaemonClient`** (Core `Services/Daemon/`): implements `IDaemonClient` via `HttpClient`
>   against `/tdcp`; a background SSE-GET consumer re-raises `TreeChanged`/`ScheduleChanged`;
>   `RunTurnAsync` forwards `node.NodeId`. Per §5, **`GetTurnLock` throws `NotSupportedException`**
>   (the per-node lock lives daemon-side; the UI stops calling it once turns route through
>   `turn.run`).
> - **Scope of 5a:** request/response for every §4 method + SSE for the two push events. **Deferred:**
>   live `turn.event` token streaming (not part of the current `IDaemonClient` contract — tdcp-spec
>   §5/§6) and daemon-side turn-lock enforcement subtleties → 5b/later.
> - **Guard:** a round-trip test starting a real-loopback `TdcpServer` over a recording
>   `IDaemonClient`, with a `WireDaemonClient` pointed at it — mirrors `InProcDaemonClientTests`
>   through the wire. WireDaemonClient must satisfy the same contract (minus the `GetTurnLock`
>   caveat). This is the regression net for 5b.
>
> **5b — extract the separate process 🟡:** the `TessynNetDaemon` host exe + `Program.cs`, relocate
> lifecycle from `App.axaml.cs`, UI swaps `InProcDaemonClient` → `WireDaemonClient` in DI, daemon
> discovery/spawn/single-instance. Higher risk; done only after 5a is green.

> **Stage 5a — DONE, verified.** `TdcpServer` (Core `Services/Daemon/`) and `WireDaemonClient`
> (Core `Services/Daemon/`) implemented; `/tdcp` routed off `AgentMcpServer`'s existing root-prefix
> `HttpListener` with the `/mcp` flow byte-for-byte unchanged. DI registration completed in
> `App.axaml.cs`: `TdcpServer` is constructed from `IDaemonClient` + a `TdcpToken` (new
> `AppSettingsModel.TdcpToken` field — generated once via `Guid.NewGuid()` on first run, persisted,
> reused across restarts, sent as `X-CMX-Token`). 32 new round-trip tests
> (`WireDaemonClientTests.cs`) cover every `IDaemonClient` member over a real loopback socket, SSE
> propagation of `TreeChanged`/`ScheduleChanged`, the `GetTurnLock` → `NotSupportedException` wire
> caveat, and wrong-token rejection. Suite: **160/160 passing**, build clean (0 warnings). One process
> throughout.

> **Stage 5b — REDEFINED (D5, Architect 2026-07-24): migrate the VMs onto `IDaemonClient`, still
> in-process; the actual exe split is deferred and may not be needed at all.**
>
> **Why the original 5b couldn't proceed as written:** verification showed `SessionTreeViewModel`
> and `SessionViewModel` still inject the daemon-owned services **directly**
> (`ISessionRegistry`, `IAppSettingsService`, `ISessionFileService`, `IDirectoryLabelService`,
> `IClaudeProcessManager`, `ISessionTurnService`, `IAgentMcpServer`, `ISchedulerService`) — Stage 4
> built `IDaemonClient`/`InProcDaemonClient` as a parallel path and *deliberately deferred* wiring
> the VMs onto it (to avoid double-handling before Stage 7's decomposition). Standing up a real
> `TessynNetDaemon.exe` today and just swapping DI registration would either fail to build (missing
> services in the UI) or require registering the daemon-owned services in **both** processes —
> two independent `SchedulerService`s double-firing the same cron jobs, two independent
> `SessionTurnService`s potentially double-spawning `claude` for the same node. Actively harmful,
> not cosmetic.
>
> **Scope of the migration (verified against actual call sites, not assumed):**
> - `SessionTreeViewModel` uses **~26 call sites across nearly the full `ISessionRegistry` surface**
>   (tree CRUD: add/rename/delete directory/group/session, import, move/insert, lookups, agent
>   summaries) — `IDaemonClient` only covered tree *read* + turn/schedule/content. **`IDaemonClient`
>   is extended with the missing ~19 tree-CRUD members**, mirroring `ISessionRegistry` 1:1;
>   `InProcDaemonClient` implements them as thin delegation to the wrapped registry. `SessionTreeViewModel`
>   is migrated to depend on `IDaemonClient` only.
> - `SessionViewModel`'s direct usage is smaller (12 call sites: `_turnService.CancelTurn`,
>   `_schedulerService.{GetSchedules,RemoveSchedule,ScheduleChanged}`, `_mcpServer.EnsureConfigFileAsync`,
>   `_turnService.GetTurnLock`, `_processManager.SendMessageAsync` ×4). Per `ISessionTurnService`'s own
>   doc comment, `RunTurnAsync` is the **headless** (scheduler/orchestration) path; the interactive
>   send path deliberately drives `IClaudeProcessManager` directly for **live token streaming**,
>   coordinating with headless turns only via the shared `GetTurnLock`. `RunTurnAsync` is a single
>   blocking call — migrating the interactive path onto it would silently drop live streaming, which
>   is explicitly a separate, not-yet-designed problem (tdcp-spec §5/§6, "deferred: live `turn.event`
>   token streaming"). **Decision: migrate `CancelTurn`/`GetTurnLock`/schedule.\* calls onto
>   `IDaemonClient` (already on the interface, safe today since `InProcDaemonClient` wraps the same
>   singleton `ISessionTurnService`/`ISchedulerService`); keep `IClaudeProcessManager` and
>   `IAgentMcpServer.EnsureConfigFileAsync` as direct dependencies** — this is an intentional,
>   documented boundary, not an oversight. Solving turn streaming is future work (Stage 7 territory
>   or later), not bundled into this pass.
> - `LoadFromSettings()` (currently called once by `SessionTreeViewModel`'s constructor) moves to
>   `IDaemonClient` too, called explicitly once in `App.axaml.cs` right after `appSettings.Load()` —
>   removes another VM→Core-service coupling. `WireDaemonClient.LoadFromSettings()` throws
>   `NotSupportedException` (startup-only concern, matching the `GetTurnLock` wire caveat pattern).
> - **No safety net for this step** — Avalonia ViewModels aren't in the headless `Tests` project.
>   Verification is: `dotnet build` clean + manually running the app and exercising tree CRUD,
>   drag/drop, session send, schedules. The existing 27+32 `IDaemonClient` boundary tests extend to
>   cover the new tree-CRUD members (that remains the regression net for the *interface*, just not
>   for the VMs consuming it).
>
> **Exe split status:** deferred per D5. `TessynNetDaemon.exe` / `Program.cs` changes are **not**
> part of this pass. Revisit only if/when actually needed — a single process serving both UI and
> daemon roles may be the permanent answer.
>
> **Stage 5b — DONE.** `IDaemonClient` extended with the ~19 missing tree-CRUD members (worker
> `Stage5b-IDaemonClient-TreeCRUD`; suite grew to **202/202 passing**, build clean). Then, by the
> supervisor directly (small, mechanical, precision-sensitive):
> - `SessionTreeViewModel`: the `ISessionRegistry _registry` field/ctor-param replaced by
>   `IDaemonClient _daemon`; all ~26 call sites repointed 1:1 (two renamed:
>   `FindModelByNodeId`/`FindModelByAgentToken` → `IDaemonClient.FindSessionByNodeId`/
>   `FindSessionByAgentToken`). No other constructor deps touched.
> - `SessionViewModel`: `ISessionTurnService _turnService` + `ISchedulerService _schedulerService`
>   removed, replaced by a single `IDaemonClient _daemonClient`. Repointed: `CancelTurn`,
>   `GetTurnLock`, `ScheduleChanged` (sub/unsub), `GetSchedules`, `RemoveSchedule`. Kept as direct
>   deps, as designed: `IClaudeProcessManager` (interactive streaming) and
>   `IAgentMcpServer.EnsureConfigFileAsync`.
> - `MainWindowViewModel`: same `_turnService`/`_schedulerService` → `_daemonClient` swap, since it
>   manually constructs `SessionViewModel` (not DI-resolved, needs the per-instance `node`) and had
>   its own direct `_schedulerService.GetSchedules` check for `HasSchedules` — migrated too, for the
>   same reason (one source of truth for schedule state, not two).
> - `LoadFromSettings()` call **stays** inside `SessionTreeViewModel`'s constructor (via
>   `_daemon.LoadFromSettings()`), rather than moving to `App.axaml.cs` as originally sketched — the
>   VM already depends only on `IDaemonClient`, so the dependency-direction goal is met; relocating
>   the call site is a separate, optional nicety, not pursued to avoid scope creep.
> - Verified: `dotnet build` clean (0 warnings), **202/202 tests pass**. **Manual app run was
>   deliberately skipped** — the app has no single-instance guard or isolated-profile flag, and a
>   second process would share the same live `AppSettings`/session files as the instance currently
>   hosting this supervising session; the risk of a write conflict against real in-progress work
>   outweighed the marginal confidence gain from a mechanical, call-site-verified type swap over the
>   same singleton services. Real-world verification happens the next time the Architect restarts
>   the app.
>
> **Stage 5b exit criterion met:** both VMs now depend on `IDaemonClient` for tree/turn-lock/schedule
> operations — the exe split (if/when revisited) is safe without touching either VM again.

---

### Stage 6 — Remove the in-app Tessyn dual path 🟢 DONE
With the daemon in place, the old in-UI daemon path is redundant.
- 🟢 (done, Stage 7 prerequisite) Delete `SessionViewModel.SendViaDaemonAsync/HandleRunEvent/
  FinishDaemonRun/LoadFromDaemonAsync` and the `UseDaemon` branch; the UI always uses `IDaemonClient`.
- 🟢 (done) Retire `TessynRunService` (Stage 7), `TessynDaemonService`, all 15 `Tessyn*` models,
  `TessynRpcException`, and the `App.axaml.cs` tessyn-connection startup (`StartTessynConnectionAsync`/
  `IsTessynInstalled`/`EnsureDaemonRunningAsync`/`IsDaemonListening`) — 18 files deleted outright.
  `UseTessynDaemon`/`TessynPath`/`AutoStartDaemon`/`DaemonPermissionMode` settings removed from
  `AppSettingsModel`; the orphaned `Constants.Tessyn` block (ports, RPC timeouts, error codes) removed.
- 🟢 (done) Fix naming drift ("Tessyn Desktop" → "ClaudeMaximus") in `App.axaml.cs` startup log and
  `MainWindow.axaml` window title.

> **The blocker this stage was waiting on — TessynDaemonService's search + title generation —
> is now solved, not deferred.** Both were reimplemented in-process (no NodeJS dependency at
> all) rather than routed around:
> - **New Core service `IClaudeSessionIndexService`/`ClaudeSessionIndexService`**, backed by
>   SQLite FTS5 (new dependency: `Microsoft.Data.Sqlite`, Architect-approved). Indexes every
>   `~/.claude/projects/*/*.jsonl` file (all projects, machine-wide) into a `messages` FTS5
>   table (message-level, `bm25`-ranked) + a `sessions` metadata/title-cache table, both keyed
>   by the stable Claude CLI session UUID. Incremental: skips files whose mtime hasn't changed
>   since last index. `EnsureTitlesAsync` wraps the *already-working* CLI-based
>   `IClaudeAssistService.GenerateTitlesAsync` (no daemon needed for this part — it never was)
>   and adds the one thing that was missing: **persistence**, so a title is never regenerated
>   once cached.
> - **`IDaemonClient` extended** with `ReindexSessionsAsync`, `SearchSessionsAsync`,
>   `EnsureTitlesAsync`, `GetSessionSummariesAsync` — implemented for real in
>   `InProcDaemonClient`; `WireDaemonClient` throws `NotSupportedException` (not yet wired over
>   TDCP), matching the established pattern for Stage 5b members.
> - **`SessionTreeViewModel`'s tree search bar and `ImportPickerViewModel`'s global search +
>   title generation** rewired off `ITessynDaemonService` onto `IDaemonClient`. The rewrite is a
>   genuine simplification, not just a swap: search results now carry the stable external ID
>   directly, eliminating the old numeric-daemon-ID → external-ID resolution round trip
>   (`SessionsListAsync(limit: 10000)` cached dictionary) entirely.
> - Reindexing runs once in the background at app startup (`App.axaml.cs`), replacing the old
>   daemon-connect kickoff.
> - **Verified with 15 new real tests** (temp-directory JSONL fixtures + a fake
>   `IClaudeAssistService`, no CLI/process dependency) — reindex incrementality, cross-project
>   search + project/role filtering, FTS5 query-escaping, and title cache/regeneration
>   semantics. This is a genuine Core service, unlike the ViewModels elsewhere in this refactor,
>   so it has a real test net, not just call-site auditing. Suite: **221/221 passing**, build
>   clean (0 warnings).
>
> **Scope boundary respected:** cross-project *import* of a search hit (writing a real local
> `.txt` session file from a resolved JSONL path) was already working code in
> `SessionTreeView.axaml.cs.ExecuteImport` and needed no changes — it already branched correctly
> whenever `ClaudeSessionSummaryModel.JsonlPath` is populated, which the new search path always
> does. The one dead branch (a `daemon-*.pending` placeholder filename for sessions with no
> resolvable JSONL, whose content-loading counterpart `LoadFromDaemonAsync` Stage 7 already
> deleted) was removed from both `SessionRegistry.CreateSessionModel` and `ExecuteImport`.

**Payoff:** the entire external NodeJS `tessyn` dependency is gone. Cross-project search and
title generation are now permanent, in-process ClaudeMaximus capabilities, reachable from any
future `IDaemonClient` consumer (Avalonia today; WPF or a web client over TDCP later) — not
tied to whether a separate daemon binary happens to be installed and running.

---

### Stage 7 — Decompose the god objects 🟢
Now safe (VMs are thin over `IDaemonClient`, engine is tested).
- 🟢 (done) `SessionViewModel` → split along seams from `structure-notes.md §5`:
  `SessionContentViewModel` (messages/markdown), `TurnController`, `ProfileController`,
  `ModelSelectionController`, `SessionPromptComposer` (folds in the existing
  `InstructionBlockBuilder`). Keep `AutocompleteVm`/`OutputSearchVm`.
- 🟡 *Not done this pass:* `SessionTreeView.axaml.cs` (959 LOC) drag/drop + move-mode
  extraction, and a `SessionViewModelFactory` to kill the manual multi-arg construction in
  `MainWindowViewModel`.

> **`SessionViewModel` decomposition — DONE, verified.** 2551 LOC → **520 LOC** coordinator
> plus 5 focused collaborators (`SessionPromptComposer` 108, `ModelSelectionController` 198,
> `ProfileController` 341, `SessionContentViewModel` 533, `TurnController` 706 — 2406 LOC
> total, the delta being genuine separation-of-concerns overhead: per-file usings, XML docs,
> constructors).
>
> **Prerequisite folded in: the dead in-app-daemon turn-execution path was deleted first**
> (`SendViaDaemonAsync`/`HandleRunEvent`/`FinishDaemonRun`/`LoadFromDaemonAsync`/`UseDaemon`,
> plus `ITessynRunService`/`TessynRunService` — a thin, now-orphaned facade over
> `ITessynDaemonService`, entirely deleted). This is exactly Stage 6's turn-execution bullet,
> pulled forward because it was dead weight sitting inside precisely the code being
> decomposed — doing the decomposition first would have meant faithfully preserving a
> deletion candidate. **Scope note:** only the turn-execution path was removed here — the
> still-working `ITessynDaemonService` search/title-generation features
> ([[tessyn-inapp-path-not-dead]]) were **not** touched; the rest of Stage 6 (retiring
> `TessynDaemonService`, the `App.axaml.cs` tessyn-connection startup) remains open.
>
> **Design: facade-preserving decomposition.** `SessionViewModel` keeps every original public
> property/method/event signature unchanged, each now a thin passthrough delegating to one of
> the 5 collaborators, with each collaborator's `PropertyChanged` forwarded so bound
> properties keep notifying. This was a deliberate choice over binding AXAML directly to
> nested VMs (the codebase's own established pattern for `AutocompleteVm`/`OutputSearchVm`,
> e.g. `{Binding AutocompleteVm.IsOpen}`) — that alternative would have touched every `.axaml`
> file referencing session state, which cannot be safely verified: this app has no
> single-instance guard, so a second instance for manual UI testing would share the same live
> `AppSettings`/session files as whatever instance is running the split. Facade-preservation
> means **zero `.axaml` changes**, so the entire decomposition is verifiable by build + the
> 202-test suite plus systematic call-site auditing, without needing a live UI check.
>
> **A real bug avoided:** the natural design (cache `fileName`/`workingDirectory` as strings in
> `SessionPromptComposer`'s constructor) would have gone stale after a session is moved to a
> different directory (`WorkingDirectory` is a mutable `{ get; set; }`, unlike the immutable
> `{ get; init; }` `FileName`) — fixed by holding a `SessionNodeViewModel` reference and reading
> both live instead.
>
> **Verified:** build clean (0 warnings), **202/202 tests pass** (one `SchedulerServiceTests`
> failure was observed and confirmed pre-existing/flaky — passes in isolation, untouched by
> this change). Every public member was cross-checked against actual call sites in
> `MainWindowViewModel.cs` and `Views/SessionView.axaml`/`.axaml.cs` (not assumed) before being
> preserved on the coordinator's facade. One genuinely dead private method,
> `GetJsonlPath()` (zero callers anywhere in the codebase), was dropped rather than carried
> forward.
>
> **Not verified:** a live manual run of the app — same shared-state constraint as Stage 5b.
> Real-world verification happens on the Architect's next app restart.

---

### Stage 8 — Additional WPF client (D3) ⛔ SUPERSEDED
> **Superseded 2026-11-15 by `wpf-programme-plan.md`.** The Architect decided that WPF *replaces* Avalonia (P1, A9) rather than sitting beside it, and that the WPF client talks to the daemon through the remote API (P2, P13). The text below is kept as history; do not follow it.

D3 is settled: Avalonia stays the sole client through the split; the **WPF** client is a
*later, separate, additive* project once the daemon boundary exists — not a rewrite.
- 🟡 Reconcile NR.1 (Windows-only) in requirements **first** (Architect owns the doc edit).
- 🟡 Build `ClaudeMaximus.Wpf` against `IDaemonClient`/TDCP, with a **WebView2** message pane
  to avoid re-implementing markdown/selection/highlighting. Because both Avalonia and WPF are
  thin TDCP clients sharing the Core `IDaemonClient` contract, this is additive.
- Avalonia is *not* frozen by this — it remains a first-class thin client; WPF is added
  alongside it for those who want it.

---

### Stage 9 — Docs & porting contract 🟡 (Architect owns docs; Developer drafts)
- Draft `docs/architecture/daemon.md`: process model, TDCP methods, MCP tool schemas,
  auth, lifecycle, state ownership — the **porting spec** for tessyn-NodeJS.
- Update `requirements.md`/`glossary.md` for the split, `IDaemonClient`, TDCP, and (if D3)
  the Windows-only note.
- Housekeeping: remove `SessionViewModel.cs.bak`, deprecated `Tempcmx-build/`, confirm
  root scripts (`run.ps1`, `write_*.ps1`), verify `.gitignore`.

---

## 3. Cross-cutting concerns to watch

- **UI-thread marshaling** — the biggest hidden Avalonia coupling inside "services"
  (FR.15.4). `IUiDispatcher` (Stage 3) is the fix; audit every `Dispatcher.UIThread`
  in `Services/`.
- **`appsettings.json` ownership** — becomes the **daemon's**; the UI must never write it
  directly after Stage 5 (all mutations go through TDCP). Watch for VMs that call
  `_appSettings.Save()` today.
- **Per-node turn serialization / locks** (`SemaphoreSlim` in `SessionTurnService`,
  FR.14.9) — must live daemon-side; the UI can no longer assume in-proc locking.
- **File watchers** — `SessionViewModel` watches session `.txt`/JSONL for live updates.
  Post-split, prefer daemon push over TDCP to the UI rather than the UI watching files
  the daemon writes (avoids double sources of truth). Decide in Stage 4.
- **Profiles/auth interactive flows** (FR.12.7/12.17) spawn visible consoles — must run
  daemon-side (it owns `CLAUDE_CONFIG_DIR`); the UI triggers via TDCP.

## 4. Risk register

| Risk | Stage | Mitigation |
|---|---|---|
| Registry extraction breaks tree/orchestration | 1 | Manual verify each MCP tool; keep VM projection dumb |
| Hidden Avalonia deps block Core carve-out | 3 | Avalonia-free build gate; `IUiDispatcher` |
| TDCP under-specifies UI needs → churn | 4 | Build `IDaemonClient` from the *actual* current VM→service calls |
| State double-write (UI + daemon) | 5 | Single writer rule: daemon owns `appsettings.json` |
| Scope creep into WPF client mid-split | 8 | Gate D3; keep WPF additive after boundary exists |
| TDCP transport wrong for future web-server link (S3) | 4/5 | JSON-RPC over Streamable HTTP serves S2+S3 via SSE inversion (see §0.2); D4 confirmed |

## 5. Sequencing summary

Stage 1 (registry) and Stage 2 (tests) are **prerequisites and pure wins** — worth doing
even if D3/D4 change. Stages 3–5 realize the split. Stage 6 collapses the old Tessyn path.
Stage 7 pays down the god objects. Stage 8 is the only D3-dependent stage. Stage 9 keeps
the porting contract alive.

## 6. Open confirmations before Stage 5

1. ✅ **D3 confirmed** (2026-07-18): split-first, Avalonia thin, WPF as a later separate client.
2. ✅ **D4 confirmed** (2026-07-19): TDCP as its own protocol — **JSON-RPC 2.0 over Streamable
   HTTP** — co-hosted with the MCP server (not "extend MCP"), so one transport family serves S1
   (MCP), S2 (UI↔daemon) and the future S3 (daemon→web-server) link. See §0.2. **Stage 5 unblocked.**
3. ✅ Approval to create `ClaudeMaximus.Core` / `TessynNetDaemon` projects, edit `.sln`, and add
   `Program.cs` hosts — granted (2026-07-18); Core already created in Stage 3.
4. ⏳ Confirm the daemon owns `appsettings.json` post-split (single-writer rule).
5. ⏳ **NR.1 reconciliation** — WPF (D3) is Windows-only and the TFM is already
   `net9.0-windows10.0.17763.0`, so the app is Windows-only in practice. Record NR.1 as
   Windows-only (Architect owns the requirements edit).
