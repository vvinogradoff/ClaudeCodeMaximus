# ClaudeMaximus — Structure Notes for Refactoring

> Working notes (not authoritative documentation). Author: Developer/Claude.
> Purpose: give enough structural understanding to plan a general refactoring.
> Scope: `code/ClaudeMaximus` (~20.2k LOC C#, net9.0, Avalonia + ReactiveUI, MVVM).
> Snapshot date: 2026-07-17.

---

## 1. Layer Map (as intended by NR.4)

```
Views (AXAML + code-behind)        ← Avalonia controls, converters, custom render
      │  binds to
ViewModels (ReactiveUI)            ← MainWindow, SessionTree, Session, + node VMs
      │  calls
Services (interfaces + impls)      ← process, files, settings, orchestration, daemon
      │  operates on
Models (POCOs / records)           ← settings, tree nodes, agent DTOs, Tessyn DTOs
```

**Intended rule (NR.4/NR.5):** Models and Services have no UI dependencies; everything sits behind interfaces for testability.

**Reality:** the rule is broken at the Service↔ViewModel boundary — see §3.2. `SessionTreeViewModel` is the de-facto in-memory store of tree state, so three headless services reach up into it.

---

## 2. Component Inventory & Size Hotspots

| File | LOC | Layer | Verdict |
|---|---:|---|---|
| `ViewModels/SessionViewModel.cs` | **2545** | VM | **God object** — top refactor target |
| `ViewModels/SessionTreeViewModel.cs` | **1469** | VM | God object + doubles as data store |
| `Services/AgentMcpServer.cs` | **1242** | Svc | Large but cohesive; split transport vs. tools |
| `Views/SessionTreeView.axaml.cs` | **959** | View | **Too much logic in code-behind** (DnD, move mode) |
| `ViewModels/ImportPickerViewModel.cs` | 833 | VM | Large; self-contained |
| `Services/TessynDaemonService.cs` | 719 | Svc | Entire parallel subsystem (see §4) |
| `ViewModels/MainWindowViewModel.cs` | 688 | VM | Mostly forwarding/shell orchestration |
| `Views/MainWindow.axaml.cs` | 666 | View | Heavy code-behind |
| `Services/ClaudeSessionImportService.cs` | 552 | Svc | Cohesive |
| `Services/ClaudeAssistService.cs` | 511 | Svc | Cohesive |
| `Services/ClaudeProcessManager.cs` | 507 | Svc | Core; cohesive |
| `Views/MarkdownView.cs` | 457 | View | Custom renderer; cohesive |
| `Views/SessionView.axaml.cs` | 442 | View | Heavy code-behind |
| `Constants.cs` | 323 | — | Central; nested config classes |

Everything else is < 355 LOC. Full breakdown available via `wc -l`.

---

## 3. Dependency Graph

### 3.1 DI registration (App.axaml.cs `ConfigureServices`)
All singletons except `SettingsViewModel` (transient). Verified edges (constructor injection):

```
AppSettingsService ──────────── (root config; loaded once at startup)

ClaudeModelService ── OllamaModelService, AppSettings
ClaudeAssistService ── ClaudeProcessManager, ClaudeModelService
SessionTreeViewModel ── AppSettings, DirectoryLabel, SessionFile,
                         ClaudeSessionStatus, SessionSearch, GitOrigin,
                         Draft, TessynDaemon?(nullable)

SessionTurnService ── ProcessManager, SessionFile, AppSettings, ClaudeProfile,
                       Lazy<AgentMcpServer>, SessionTreeViewModel,
                       ClaudeSessionStatus, ClaudeModel
AgentMcpServer ── AppSettings, Lazy<SessionTurnService>, Lazy<SchedulerService>,
                   SessionTreeViewModel, SessionFile, ClaudeModel
SchedulerService ── AppSettings, SessionTurnService, SessionTreeViewModel, Notification

MainWindowViewModel ── 18 deps (see §3.3)
SessionViewModel ── 15 deps (constructed per-session, not via DI root)
```

### 3.2 ⚠ Circular dependencies & layering violations

1. **`SessionTurnService` ↔ `AgentMcpServer`** — genuine cycle, broken with `Lazy<T>`
   registrations (documented inline in App.axaml.cs:251). `SchedulerService` also
   Lazy-wrapped. This is a smell: the turn runner, the MCP tool host, and the
   scheduler are one tangled cluster ("the agent engine").

2. **Services depend on a concrete ViewModel.** `SessionTurnService`,
   `AgentMcpServer`, and `SchedulerService` all take `SessionTreeViewModel`
   (concrete, not an interface) as a constructor dependency. This violates NR.4
   ("services have no UI dependencies") and NR.5 (testability) — you cannot unit
   test the agent engine without instantiating a ReactiveUI VM that itself pulls in
   8 services. **Root cause:** the tree of `SessionNodeModel`s (the durable domain
   state) lives inside a ViewModel rather than in a service.

3. **`SessionViewModel` is constructed manually** (not from the DI container root) and
   takes 15 dependencies incl. two nullable Tessyn services. `MainWindowViewModel`
   holds a `_sessionCache` (`Dictionary<string, SessionViewModel>`) and builds them.

### 3.3 `MainWindowViewModel` (18 ctor deps)
`AppSettings, SessionFile, ProcessManager, Draft, CodeIndex, ClaudeProfile,
ClaudeSessionImport, ClaudeModel, ClaudeUsage, SelfUpdate, DirectoryLabel,
SessionTreeViewModel, SessionTurnService, AgentMcpServer, SchedulerService,
TessynRun?(nullable), TessynDaemon?(nullable)` — most are only forwarded to
`SessionViewModel` at construction time. This is a construction-injection funnel,
not real usage. A `SessionViewModelFactory` would remove ~10 of these.

---

## 4. The Tessyn dual-path problem (biggest structural risk)

**201 references across 33 files.** There are effectively **two execution engines**
for a turn, chosen at runtime by the `UseDaemon` flag
(`Settings.UseTessynDaemon && _runService != null && _daemonService != null`):

| Concern | Direct path | Tessyn (daemon) path |
|---|---|---|
| Send a turn | `SessionViewModel.SendAsyncCore` | `SessionViewModel.SendViaDaemonAsync` |
| Stream events | `HandleStreamEvent(ClaudeStreamEvent)` | `HandleRunEvent(TessynRunEvent)` |
| Load history | `LoadFromFile` | `LoadFromDaemonAsync` |
| Completion | inline in SendAsyncCore | `FinishDaemonRun` |
| Transport | `ClaudeProcessManager` (child process, stream-json) | `TessynDaemonService` (JSON-RPC to external `npm` daemon) |

Supporting cast: `TessynRunService`, `TessynDaemonService`, `TessynRpcException`,
and ~15 `Tessyn*` model DTOs. Startup logic in `App.axaml.cs`
(`StartTessynConnectionAsync`, `EnsureDaemonRunningAsync`, `IsTessynInstalled`).

**Observations for refactoring:**
- The daemon path is **not covered by the requirements docs** (FR.1–18 describe only
  the direct `claude` CLI path). It appears to be an experimental/parallel feature.
- It roughly doubles the branching in `SessionViewModel` — a large share of that
  file's 2545 lines is the second code path plus its file/JSONL watchers.
- Log lines already call the app "Tessyn Desktop" (App.axaml.cs:105) — naming drift.
- **Decision needed from the Architect:** is Tessyn a first-class supported feature,
  a deprecated experiment, or in-progress? The answer sharply changes the refactor
  (unify behind one `ITurnEngine` abstraction vs. remove entirely vs. isolate).

---

## 5. Responsibility Matrix (key components)

### SessionViewModel (2545 LOC) — needs decomposition into ~6 collaborators
Currently owns ALL of:
- **Message display**: `LoadFromFile`, `RefreshFromFile`, `EntryToViewModel`, `SwapMessageSource`, `Messages` collection, markdown toggle.
- **Turn sending (direct)**: `SendAsync`, `SendAsyncCore`, `HandleStreamEvent`, thinking timer.
- **Turn sending (daemon)**: `SendViaDaemonAsync`, `HandleRunEvent`, `FinishDaemonRun`, `LoadFromDaemonAsync`.
- **File/JSONL watching**: `StartFileWatcher`, `StartJsonlWatcher`, `OnSessionFileChanged`, `OnJsonlFileChanged`, `DetectExternalActivity`, `RefreshFromJsonl`.
- **Instruction building**: `BuildInstructionBlock`, `BuildContextPreamble`, `BuildCompactionPrompt`, `BuildModelLabel`.
- **Compaction & detach**: `SendCompactionPromptAsync`, `DetachSession`.
- **Mid-run corrections**: `SendMidRunToggleCorrection` (×2 overloads), `SendMidRunAutoCommitCorrection`.
- **Model/effort/profile UI state**: `RebuildModelList`, `RebuildProfileList`, `RestoreLastUsedSettings`, `OnModelsUpdated`, and the entire profile lifecycle (`HandleNewProfileAsync`, `HandleReauthenticateAsync`, `HandleRemoveProfile`, `RenameProfile`, `DeleteProfile`, `ResolveDefaultProfileEmailAsync`).
- **Scheduling UI**: `RefreshScheduleState`, `FormatScheduleTooltip`, `CancelAllSchedules`.
- **JSONL path resolution**: `GetJsonlPath`, `GetAllJsonlPaths`, `BuildJsonlPathForSessionId`.
- Owns sub-VMs (good): `AutocompleteVm`, `OutputSearchVm`.

**Suggested seams**: `MessageLog`/session-content VM · `TurnController` (engine-agnostic) · `ProfileController` · `ModelSelectionController` · `SessionFileWatcher` · `InstructionComposer` (partly already in `InstructionBlockBuilder`).

### SessionTreeViewModel (1469 LOC) — split "tree UI" from "node store"
- **Tree CRUD**: Add/Rename/Delete for Directory/Group/Session (+ group-in-group).
- **Move / drag-drop model**: move mode, `MoveSessionTo`, git-origin restrictions, insert/remove.
- **Persistence load**: `LoadFromSettings`, `BackfillAgentIdentity`, `ReorganizeSubSessions`.
- **Search filter**: `ApplySearchFilter` + by-ExternalId variant.
- **Node lookup (used by services)**: `FindNodeVmByNodeId`, `FindModelByNodeId`, `FindModelByAgentToken`, `CreateSessionForAgent`, `BuildAgentSessionSummaries`.
- **Recency**: `RefreshLastPromptTimes`, `LoadLastPromptTime`.

**Key seam**: extract the "find/create/enumerate `SessionNodeModel`" surface into an
`ISessionRegistry` service. That single move fixes layering violation §3.2 (services
depend on the registry interface, not the VM) and unlocks testability of the agent engine.

### AgentMcpServer (1242 LOC) — cohesive but mixes 3 things
- **HTTP/JSON-RPC transport**: `AcceptLoopAsync`, `HandleRequestAsync`, send/error helpers, `FindFreePort`.
- **Tool dispatch + schemas**: `HandleToolsList`, `HandleToolCallAsync`, ~14 `*Schema()` methods.
- **Tool logic**: schedule_wake, list/cancel, list/spawn/send/read/stop_session, set_session_model, orchestrate_parallel/pipeline, workflow_phase/log, get_budget, tier enforcement, budget accrual.

**Suggested split**: `McpTransport` (JSON-RPC plumbing) · `McpToolRegistry` (schemas +
routing) · `OrchestrationEngine` (spawn/fan-out/budget/tier). Schemas (~180 lines of
inline JSON) could move to embedded resources.

### The "agent engine" cluster (SessionTurnService + AgentMcpServer + SchedulerService)
Tightly coupled trio (two `Lazy<>` cycles). Together they implement FR.14–17.
Good candidate to formalize as a bounded module with a small public surface
(`ITurnEngine`, `IScheduler`, `IOrchestrator`) sharing an `ISessionRegistry`.

### Leaf services (small, cohesive — low refactor priority)
`AppSettingsService`, `SessionFileService`, `DraftService`, `DirectoryLabelService`,
`GitOriginService`, `SessionSearchService`, `ClaudeSessionStatusService`,
`KeyBindingService`, `OllamaModelService`, `ClaudeModelService`, `ClaudeUsageService`,
`ModelTierService`, `SelfUpdateService`, `CodeIndexService`/`CodeIndex`,
`InstructionBlockBuilder`, `ThemeApplicator`, `WindowsNotificationService`.

---

## 6. Test Coverage Map (⚠ major gap)

Test project `ClaudeMaximus.Tests` contains **only 5 test files**:
`AppSettingsServiceTests`, `ClaudeAssistServiceTests`, `ClaudeSessionImportServiceTests`,
`DirectoryLabelServiceTests`, `SessionFileServiceTests`.

**Untested (and highest-complexity / newest):**
- All ViewModels (SessionViewModel, SessionTreeViewModel, MainWindowViewModel, ImportPicker…).
- The entire agent engine: `AgentMcpServer`, `SessionTurnService`, `SchedulerService`, `ModelTierService`.
- `ClaudeProcessManager` (TODO explicitly defers "integration tests with mock process").
- All of Tessyn.
- Autocomplete (`AutocompleteTriggerParser`, `CodeIndexService` tiered search — TODO marks these `[ ]`).

CLAUDE.md mandates integration + E2E coverage per requirement; the TODO lists many
`[ ]` test items. **Implication:** any refactor of the agent engine is currently
unguarded. Establishing an `ISessionRegistry` seam (§5) is a prerequisite to writing
those tests, so it should come *first* in the refactor sequence.

---

## 7. Cruft / Housekeeping (safe to clean, pending confirmation)

- `ViewModels/SessionViewModel.cs.bak` — committed-adjacent backup file (untracked). Remove.
- `Tempcmx-build/` — explicitly "deprecated, do not use" per `code/CLAUDE.md`. Untracked.
- Root scripts `run.ps1`, `write_axaml.ps1`, `write_codebehind.ps1` — untracked build/scratch helpers; confirm intent.
- Naming drift: product is "ClaudeMaximus" but logs/startup say "Tessyn Desktop".
- `obj/`/`bin/` artifacts appear in some listings — ensure `.gitignore` coverage.

*(Per project rules: cleanup and any doc changes are the Architect's call; nothing here committed.)*

---

## 8. Prioritized Refactoring Plan (proposed sequence)

**Ordering rationale:** decouple → cover with tests → decompose. Do not decompose the
god objects before the seams and tests exist, or the refactor is unguarded.

1. **Resolve the Tessyn question (Architect decision).** Unify / isolate / remove.
   This gates how much of `SessionViewModel` and `App.axaml.cs` changes. *(blocking)*
2. **Extract `ISessionRegistry`** from `SessionTreeViewModel` (find/create/enumerate
   `SessionNodeModel` + backfill). Repoint `SessionTurnService`, `AgentMcpServer`,
   `SchedulerService` at the interface. Fixes §3.2 layering violation and both `Lazy<>`
   cycles' worst coupling.
3. **Add a test harness** for the agent engine against the new registry interface
   (tier governance, depth/concurrency guardrails, budget accrual, schedule firing).
4. **Introduce `SessionViewModelFactory`** to remove the 15-arg manual construction
   and shrink `MainWindowViewModel`'s 18-dep funnel.
5. **Decompose `SessionViewModel`** along the seams in §5 (message log, turn
   controller, profile controller, model selection, file watcher, instruction composer).
6. **Split `AgentMcpServer`** into transport / tool-registry / orchestration.
7. **Move logic out of `SessionTreeView.axaml.cs` (959 LOC)** — drag/drop + move-mode
   belong in the VM or a behavior, not code-behind.
8. **Backfill unit tests** for autocomplete + code index (already-listed TODO items).

---

## 9. Open Questions for the Architect

1. **Tessyn**: supported feature, deprecated experiment, or WIP? (drives §4, §8.1)
2. Is moving `SessionNodeModel` ownership into an `ISessionRegistry` service
   acceptable architecturally (it changes where "the tree" lives)? *(§8.2)*
3. Target test strategy for the agent engine — pure unit vs. loopback-HTTP integration
   against the real `AgentMcpServer`?
4. Product/log naming: standardize on "ClaudeMaximus"?
