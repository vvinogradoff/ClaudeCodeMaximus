# ClaudeMaximus — TODO

## Phase 1 — Shell & Tree ✓ DONE

### P1.1 Solution scaffold ✓
- [DONE] `ClaudeMaximus.sln` + project structure under `code/`
- [DONE] Avalonia MVVM app project (net9.0, ReactiveUI)
- [DONE] xUnit test project
- [DONE] NuGet packages + `NuGet.config` scoped to nuget.org

### P1.2 Configuration & persistence (FR.4) ✓
- [DONE] `AppSettingsModel`, tree node models, `WindowStateModel`
- [DONE] `IAppSettingsService` / `AppSettingsService` — atomic JSON load/save
- [DONE] Unit tests: load/save round-trip, atomic write

### P1.3 Session tree UI (FR.1) ✓
- [DONE] `SessionTreeViewModel` — Add/Rename/Delete for all node types
- [DONE] `DirectoryNodeViewModel`, `GroupNodeViewModel`, `SessionNodeViewModel`
- [DONE] Tree panel with search box bound to `SearchText`
- [DONE] Add Directory (folder picker), Add Group, Add Session (via context menus + InputDialog)
- [DONE] Rename Group / Session
- [DONE] Delete rules enforced (session: file must be gone; node: must be empty)
- [DONE] Running session indicator (`IsRunning` on `SessionNodeViewModel`)
- [DONE] git-root label resolution for Directory nodes (trailing separator bug fixed)
- [DONE] `x:CompileBindings="False"` on tree DataTemplates (Avalonia compiled binding gotcha)
- [DONE] Unit tests: git-root label logic incl. trailing separator case

### P1.4 Application shell (FR.6) ✓
- [DONE] Two-panel `MainWindow` with resizable splitter
- [DONE] Window state persisted/restored via `AppSettingsService`
- [DONE] Settings window (session root dir + claude path)
- [DONE] Empty right-panel placeholder

---

## Phase 2 — Session Storage & Display ✓ DONE

### P2.1 Session file service ✓
- [DONE] `ISessionFileService` / `SessionFileService`
- [DONE] File naming: `YYYY-MM-dd-HHmm-xxxxxx.txt`
- [DONE] Append USER / ASSISTANT / SYSTEM entries (immediate flush)
- [DONE] Append `[COMPACTION]` separator
- [DONE] Read and parse all entries
- [DONE] Unit tests: create, append, compaction, round-trip, multi-line

### P2.2 Session view ✓
- [DONE] `SessionViewModel` — loads file, observable `Messages` list
- [DONE] `MessageEntryViewModel` with role booleans
- [DONE] `SessionView` — per-role visual styles (user/assistant/system/compaction)
- [DONE] Compaction separator displayed inline; full history preserved above
- [DONE] Multi-line input, Ctrl+Enter send, Send button
- [DONE] Busy state disables input; "Claude is thinking…" indicator
- [DONE] Auto-scroll to bottom on new messages
- [DONE] Session name header

### P2.3 Search (FR.1.9) ✓
- [DONE] `ISessionSearchService` + `SessionSearchService` — linear scan over session `.txt` files
- [DONE] Wire `SessionTreeViewModel.SearchText` changes to filter tree (300ms debounce, async file scan)
- [DONE] Sessions not matching query hidden; ancestor nodes of matches expanded
- [DONE] `IsVisible` property on all node ViewModels, bound via `ReflectionBinding` on `TreeViewItem` style
- [ ] Unit tests: match / no-match / ancestor expansion

### P2.4 UI polish
- [DONE] Date shown above time on user and assistant message bubbles (`FormattedDate` property)
- [DONE] Last user prompt date/time shown on session tree nodes (small grey text, bottom-right)

---

## Phase 3 — Claude Code Process Integration ✓ DONE

### P3.1 Process management ✓
- [DONE] `IClaudeProcessManager` / `ClaudeProcessManager`
- [DONE] Spawn `claude --output-format stream-json [--resume <id>]` per message
- [DONE] Pipe user input via stdin (EOF triggers processing)
- [DONE] Parse `assistant` / `system` / `result` stream-json events
- [DONE] Capture `session_id` from result → store in `appsettings.json` → used for `--resume`
- [DONE] Process launch errors surface as SYSTEM messages

### P3.2 Wired to session view & storage ✓
- [DONE] Send → file append → `Messages` list update → auto-scroll
- [DONE] Compaction event → file separator + inline UI separator
- [DONE] `IsRunning` flag on `SessionNodeViewModel` set while process is active
- [ ] Integration tests with mock process (fake stdout stream) — deferred

---

## Phase 4 — Code Reference Autocomplete (FR.7)

### P4.1 Background Code Indexer (FR.7.1, FR.7.9, FR.7.10, FR.7.11) ✓
- [DONE] `CodeSymbolKind` enum, `CodeSymbolModel`, `IndexedFileModel` models
- [DONE] `ICodeIndexService` / `CodeIndexService` — singleton, per-directory indexes, reference-counted
- [DONE] `CodeIndex` — background scan, Roslyn syntax-only parsing, FileSystemWatcher, debounced reindex
- [DONE] Constants for debounce, max suggestions, triggers, extensions, excluded dirs
- [DONE] DI registration + Roslyn NuGet package

### P4.2 Trigger Detection (FR.7.2, FR.7.3, FR.7.4) ✓
- [DONE] `AutocompleteMode` enum, `AutocompleteTriggerModel`
- [DONE] `AutocompleteTriggerParser` — scans backward from caret for `#` / `##` triggers

### P4.3 Autocomplete ViewModel (FR.7.4, FR.7.5, FR.7.6, FR.7.7) ✓
- [DONE] `AutocompleteSuggestionModel`
- [DONE] `AutocompleteViewModel` — suggestions collection, selection, accept/dismiss
- [DONE] 4-tier search: case-sensitive starts-with, case-insensitive starts-with, case-sensitive contains, case-insensitive contains
- [DONE] Wired into `SessionViewModel` + `MainWindowViewModel`

### P4.4 Autocomplete UI (FR.7.5, FR.7.6, FR.7.8) ✓
- [DONE] `AutocompletePopup.axaml` + `.cs` — ListBox with symbol icon, display text, secondary text
- [DONE] `SymbolKindConverter` + `SymbolKindColorConverter` (VS Code-style colors)
- [DONE] Popup in `SessionView.axaml` positioned above InputBox
- [DONE] Keyboard handling in `SessionView.axaml.cs` — Up/Down/Tab/Enter/Escape
- [DONE] Trigger detection on text/caret change
- [DONE] Insertion removes `#`/`##` trigger and inserts FQN (symbols) or relative path (files)
- [ ] Unit tests for `AutocompleteTriggerParser`
- [ ] Unit tests for `CodeIndexService` tiered search

### P4.5 Filesystem Path Autocomplete (FR.7.12) ✓
- [DONE] `AutocompleteMode.Path` enum value
- [DONE] `AutocompleteTriggerParser` detects drive letter patterns (C:\, D:\, etc.)
- [DONE] `AutocompleteViewModel.PopulatePathSuggestions` — lists dirs first, then files, filtered by partial name
- [DONE] Accepting a directory appends `\` for continued drilling; accepting a file inserts full path

---

## Phase 5 — State Persistence & UI Polish

### P5.1 Session State Persistence
- [DONE] Persist active session selection (`ActiveSessionFileName` in appsettings.json)
- [DONE] Persist tree expand/collapse state (`IsExpanded` on DirectoryNodeModel/GroupNodeModel)
- [DONE] Persist scroll position per session (`ScrollOffset` on SessionNodeModel)

### P5.2 Title Bar Redesign
- [DONE] Replace File menu with hamburger icon (settings flyout menu)
- [DONE] Add chevron left/right to toggle tree panel auto-hide
- [DONE] Add day/night theme toggle icon (sun/moon)
- [DONE] Tree panel collapse state persisted in `IsTreePanelCollapsed`

### P5.3 Theme & Color Customization
- [DONE] Wire `ThemeApplicator` on startup (App.axaml.cs)
- [DONE] Theme toggle from title bar updates Avalonia ThemeVariant + custom colors
- [DONE] Settings window: theme selection (Dark/Light radio buttons)
- [DONE] Settings window: color customization for selected theme (input box bg/fg, user bubble bg/fg, code block bg/fg, inline code bg/fg, system bubble bg)
- [DONE] SessionView uses DynamicResource for user bubble, system bubble, input box colors
- [DONE] MarkdownView uses ThemeApplicator resource keys for code block/inline code colors

---

## Phase 6 — Output Search (FR.10)

### P6.1 Output Search
- [DONE] `OutputSearchViewModel` — case-insensitive search, match navigation, dismiss (FR.10.1–10.5)
- [DONE] Search TextBox in SessionView header (left of MD toggle)
- [DONE] Floating overlay in top-right of output area (yellow stroke/fill, match count, prev/next/close)
- [DONE] Keyboard: Enter=search/next, Ctrl+Enter=prev, Escape=dismiss (preserves search text)
- [DONE] Scroll-to-match via `BringIntoView` on matched message container
- [DONE] Yellow text highlighting: `HighlightTextBlock` for plain text, `MarkdownView.HighlightTerm` for markdown (FR.10.4)
- [DONE] Orange highlight for current match message to distinguish from other matches (FR.10.4)
- [DONE] Precise scroll positioning: match text positioned at 25% from viewport top (FR.10.5)
- [DONE] Re-search on text change: Enter with modified text performs new search (FR.10.6)

---

## Phase 7 — Session Recency Bars (FR.9.5)

### P7.1 Recency Bars
- [DONE] 3 recency color properties in `ThemeColorsModel` with dark/light defaults
- [DONE] `CmxRecency15Min`/`CmxRecency30Min`/`CmxRecency60Min` resource keys in `ThemeApplicator`
- [DONE] `LastPromptTimestamp` + `RecencyBrush` on `SessionNodeViewModel`
- [DONE] Tree session node background bound to `RecencyBrush`
- [DONE] 60-second refresh timer in `SessionTreeView` to keep recency current
- [DONE] Recency color settings in `SettingsViewModel` + `SettingsWindow.axaml`

---

## Phase 8 — Session Instruction Toolbar (FR.11) ✓ DONE

### P8.1 Models & Persistence ✓
- [DONE] Add `IsAutoCommit` (bool) and `IsAutoDocument` (bool) to `SessionNodeModel` — per-session sticky toggles
- [DONE] Persist in `appsettings.json` via existing tree serialization

### P8.2 SessionViewModel — Toggle Properties & Instruction Building ✓
- [DONE] Add properties: `IsAutoCommit`, `IsNewBranch`, `IsAutoDocument`, `IsAutoCompact` (bool, reactive)
- [DONE] `IsAutoCommit`/`IsAutoDocument` two-way synced with `SessionNodeModel`
- [DONE] `IsNewBranch`/`IsAutoCompact` runtime-only, default false
- [DONE] `BuildInstructionBlock()` — reads toggle states, returns `---` delimited instruction text
- [DONE] Always includes auto-commit instruction (ON or OFF)

### P8.3 SessionViewModel — SendAsync Modifications ✓
- [DONE] Separate clean message (stored in file + shown in UI) from augmented message (sent to claude stdin)
- [DONE] After send: auto-reset `IsNewBranch` to false
- [DONE] After response: if `IsAutoCompact` was set, fire compaction follow-up (P8.5)
- [DONE] After response: if Clear was triggered, nullify `ClaudeSessionId`
- [DONE] Proactive context reload (FR.11.10): if session file has history but `ClaudeSessionId` is null, wrap message with `BuildContextPreamble`

### P8.4 Session File Rewrite ✓
- [DONE] Add `RewriteSessionFile(string fileName, string content)` to `ISessionFileService`
- [DONE] Atomic write (write .tmp then rename) matching existing pattern

### P8.5 Auto-Compact Follow-up ✓
- [DONE] `SendCompactionPromptAsync()` — sends compaction prompt, captures response, rewrites file, reloads UI

### P8.6 Clear Session Command ✓
- [DONE] `ClearCommand` sets `_pendingClear` flag consumed by `SendAsync` post-response
- [DONE] Post-response: nullify `ClaudeSessionId`, save settings, raise `CanClear` changed

### P8.7 Title Bar UI — Instruction Buttons ✓
- [DONE] 4 ToggleButtons + 1 Button in `MainWindow.axaml` title bar, right of theme toggle with separator
- [DONE] Bound to `MainWindowViewModel` forwarding properties
- [DONE] Disabled when no session selected (`IsEnabled="{Binding HasActiveSession}"`)
- [DONE] Tooltips on each button

### P8.8 MainWindowViewModel — Forwarding ✓
- [DONE] Forwarding properties delegate to `ActiveSession`
- [DONE] `RaiseInstructionToolbarChanged()` on session switch
- [DONE] `ClearSessionCommand` delegates to `ActiveSession.ClearCommand`

---

## Bug Fixes

### Self-Update on Exit (FR.8)
- [DONE] Fix `FindBuildOutputDir` — was filtering for `bin\Debug` in path, missing builds in `Tempcmx-build/`
- [DONE] Replace with `FindNewestBuildOutputDir` — finds newest DLL anywhere under solution root
- [DONE] Fix PS script retry logic — use `$delays[$i]` directly, `exit 0` on success
- [DONE] Update FR.8.1/FR.8.2 in requirements.md

---

## Phase 9 — Session Drag-and-Drop & Move

### P9.1 Git Origin Detection
- [DONE] `IGitOriginService` / `GitOriginService` — reads `.git/config` remote origin URL, handles worktrees
- [DONE] `DirectoryNodeViewModel.GitOrigin` property resolved on startup
- [DONE] Refresh button (↻) left of "Search sessions…" textbox to re-read git origins

### P9.2 Drag-and-Drop Sessions
- [DONE] Mouse drag initiation from session nodes
- [DONE] Git origin restriction: sessions from git repos can only drop on same-origin directories/groups
- [DONE] Non-git sessions can be dragged anywhere
- [DONE] DragOver visual feedback (cursor changes)

### P9.3 F2 Inline Rename
- [DONE] F2 starts inline rename for sessions and groups (Enter = confirm, Esc = cancel)

### P9.4 F6 / Right-Click Move Mode
- [DONE] F6 or context menu "Move" starts move mode
- [DONE] Session being moved: semi-transparent background, grey text
- [DONE] Keyboard navigation: session follows selection (below sessions, first in folders)
- [DONE] Git origin restrictions: invalid targets restore session to original position
- [DONE] Enter confirms, Esc cancels
- [DONE] "Move" added to session context menu

---

## Phase 10 — Input Command Bar & Model Selection (FR.12)

### P10.1 Model Selection & Command Bar ✓
- [DONE] Settings toggle button (gear icon) below Send button in input area (FR.12.1)
- [DONE] Collapsible command bar beneath text input with model ComboBox (FR.12.2, FR.12.3)
- [DONE] `SelectedModelIndex` persisted per directory in `DirectoryNodeModel` (FR.12.4)
- [DONE] `--model` flag passed to all `SendMessageAsync` calls when non-default model selected (FR.12.5)
- [DONE] `IClaudeProcessManager.SendMessageAsync` accepts optional `model` parameter
- [DONE] `ClaudeProcessManager.BuildArguments` appends `--model` when provided

### P10.2 Profile Selection (FR.12.6–12.10) ✓
- [DONE] `ClaudeProfileModel` — ProfileId + DisplayName (FR.12.8)
- [DONE] `IClaudeProfileService` / `ClaudeProfileService` — auth status queries + interactive auth login (FR.12.7)
- [DONE] Profile list in `AppSettingsModel`, `SelectedProfileIndex` persisted per directory in `DirectoryNodeModel` (FR.12.8)
- [DONE] Profile ComboBox in command bar (right of model selector) with Default + stored profiles + "New..." (FR.12.6)
- [DONE] "New..." triggers visible-terminal auth login, resolves email, adds profile (FR.12.7)
- [DONE] `--profile` flag passed to all `SendMessageAsync` calls when non-default profile selected (FR.12.9)
- [DONE] Default profile email resolved on first session load (FR.12.10)

### P10.3 Effort Level Selection (FR.12.12) ✓
- [DONE] Effort ComboBox in command bar (Default/Max/High/Medium/Low)
- [DONE] `SelectedEffortIndex` persisted per directory in `DirectoryNodeModel`
- [DONE] `--effort` flag passed to all `SendMessageAsync` calls when non-default effort selected
- [DONE] `IClaudeProcessManager.SendMessageAsync` + `BuildArguments` accept optional `effort` parameter

### P10.4 Per-Directory Command Bar Persistence (FR.12.11) ✓
- [DONE] `IsCommandBarVisible`, `SelectedModelIndex`, `SelectedProfileIndex` added to `DirectoryNodeModel`
- [DONE] `SessionViewModel` resolves parent `DirectoryNodeModel` by matching `WorkingDirectory` path
- [DONE] Command bar visibility, model, and profile selections restored from directory model on session switch
- [DONE] Changes persisted to directory model on user interaction

### P10.5 Profile Re-authenticate & Remove (FR.12.16–12.18)
- [DONE] Detect `"Invalid authentication credentials"` in result/system error events, set `HasAuthError` (FR.12.16)
- [DONE] `HasAuthError` cleared on request success, profile selection change, or completed reauth/remove
- [DONE] `IClaudeProfileService.LaunchAuthLoginAsync` accepts nullable configDir (Default profile reauth) (FR.12.17)
- [DONE] "Re-authenticate" button in command bar, right-docked, visible when `HasAuthError` (FR.12.17)
- [DONE] "Remove" (✕) button in command bar, right-docked, visible when `HasAuthError` and non-Default profile selected (FR.12.18)
- [DONE] Remove: strips profile from settings list, renames config dir to `<ProfileId>_rem` (collision-safe), resets selection to Default (FR.12.18)

---

## Phase 11 — Session Import (FR.13)

### P11.1 JSONL Discovery & Parsing Service (FR.13.2, FR.13.3, FR.13.7) ✓
- [DONE] `ClaudeSessionSummaryModel` model (SessionId, JsonlPath, Created, LastUsed, MessageCount, FirstUserPrompt, GeneratedTitle)
- [DONE] `IClaudeSessionImportService` / `ClaudeSessionImportService`
- [DONE] `DiscoverSessions(workingDirectory)` — derive slug, scan `~/.claude/projects/<slug>/` for `.jsonl` files, extract metadata
- [DONE] `ParseJsonlSession(jsonlPath)` — line-by-line streaming parse, per-line try/catch, FileShare.ReadWrite
- [DONE] Handle user events (string content), assistant events (text blocks + tool_use summaries), skip all other types
- [DONE] Extract `BuildProjectSlug` to `Constants.ClaudeSessions` (shared with `ClaudeSessionStatusService`)
- [DONE] Unit tests: parse happy path, corrupt lines, thinking blocks, non-conversation events, empty file, timestamps, tool_use with/without description, user content as array, slug building (12 tests)

### P11.2 Claude Assist Service (FR.13.8, FR.13.9, FR.13.14) ✓
- [DONE] `IClaudeAssistService` / `ClaudeAssistService`
- [DONE] Add `RunPrintModeAsync` to `IClaudeProcessManager` — shared infrastructure for non-interactive `claude -p` calls with timeout and stderr handling
- [DONE] `GenerateTitlesAsync(summaries)` — batch up to 20 per call, explicit ID→title JSON mapping in prompt
- [DONE] `SearchSessionsAsync(summaries, query)` — semantic search, returns ranked session IDs
- [DONE] Model fallback order: haiku → user's selected model → CLI default (FR.13.14)
- [DONE] Fallback: local substring match when CLI unavailable (handled in ImportPickerViewModel)
- [DONE] Unit tests: title response parsing, search response parsing, model fallback logic (15 tests)

### P11.3 Import Picker UI (FR.13.4, FR.13.5, FR.13.6) ✓
- [DONE] Import picker dialog (ImportPickerWindow) — scrollable session list with checkboxes
- [DONE] Display: title (progressive via Claude), date range, message count, already-imported indicator
- [DONE] Search box with Enter-to-search, spinner during async operations
- [DONE] Multi-select with "Import Selected" button
- [DONE] Empty state when no sessions found (FR.13.13)
- [DONE] Already-imported sessions greyed out and not selectable
- [DONE] Empty JSONL sessions greyed out and not selectable (FR.13.13)
- [DONE] Safe DateTimeOffset handling for empty sessions (codex review fix)
- [DONE] Search result deduplication and validation (codex review fix)

### P11.4 Tree Integration & Import Execution (FR.13.1, FR.13.10, FR.13.11, FR.13.12) ✓
- [DONE] Context menu "Import Claude Session" on Directory and Group nodes
- [DONE] `SessionTreeViewModel.ImportSession()` / `ImportSessionToGroup()` — accepts pre-populated file + ClaudeSessionId
- [DONE] `ISessionFileService.WriteSessionFile(fileName, entries)` — write complete session from parsed entries
- [DONE] Duplicate detection: `CollectAllClaudeSessionIds()` collects all values from tree before showing picker
- [DONE] Import creates session file, session node, saves appsettings.json
- [DONE] Error handling: try/catch around I/O in ExecuteImport (codex review fix)
- [DONE] Resumability preserved: original ClaudeSessionId stored on imported session node (FR.13.12)

---

---

## Phase 12 — Scheduled Turns & Agent Orchestration (FR.14, FR.15)

### P12.1 Turn execution core (FR.14.9)
- [ ] Add `NodeId` (GUID) and `AgentToken` (secret) to `SessionNodeModel` — backfill on load
- [ ] `Models/TurnSource.cs` — enum: User, Scheduled, Orchestrated
- [ ] `Models/TurnResultModel.cs` — record: AssistantText, SessionId
- [ ] `Models/InstructionOptionsModel.cs` — record: IsAutoCommit, IsNewBranch, IsAutoDocument
- [ ] `Services/InstructionBlockBuilder.cs` — shared static builder extracted from SessionViewModel
- [ ] `Services/ISessionTurnService.cs` + `SessionTurnService.cs` — headless turn runner with per-node SemaphoreSlim
- [ ] Wire turn lock into `SessionViewModel.SendAsync` to prevent concurrent turns per node

### P12.2 In-process MCP server (FR.14.1–3)
- [ ] Add `AgentMcpPort` (int) and `AgentToolsEnabled` (bool) to `AppSettingsModel`
- [ ] `Services/IAgentMcpServer.cs` + `AgentMcpServer.cs` — HttpListener on loopback, JSON-RPC 2.0
- [ ] Thread `mcpConfigPath` through `IClaudeProcessManager.SendMessageAsync` and `BuildArguments`
- [ ] `AgentMcpServer.EnsureConfigFileAsync(nodeId, token)` — writes per-node `--mcp-config` JSON file
- [ ] `SessionTreeViewModel`: backfill NodeId/AgentToken on load; add `FindNodeByNodeId` + `FindModelByNodeId`
- [ ] Register `IAgentMcpServer` singleton; start on app startup in `App.axaml.cs`
- [ ] `Constants.Agent` nested class — MaxOrchestrationDepth, MaxConcurrentWorkers, MaxTurnsPerLoop

### P12.3 Scheduling tools (FR.14.4–8)
- [ ] `Models/ScheduleModel.cs`, `Models/ScheduleKind.cs`, `Models/MissedFirePolicy.cs`
- [ ] `Models/Agent/ScheduleWakeArgs.cs`, `ListSchedulesArgs.cs`, `CancelScheduleArgs.cs`
- [ ] Add `List<ScheduleModel> Schedules` to `AppSettingsModel`
- [ ] `Services/ISchedulerService.cs` + `SchedulerService.cs` — timer-based scheduler, cron support, missed-fire handling
- [ ] Register `ISchedulerService` singleton; start on app startup; arm timers on load
- [ ] Register `schedule_wake`, `list_schedules`, `cancel_schedule` tools on `AgentMcpServer`

### P12.4 Orchestration tools (FR.15.1–4)
- [ ] `Models/Agent/SessionSummaryModel.cs`, `SpawnResultModel.cs`, `SendToSessionArgs.cs`, `SpawnSessionArgs.cs`, `ReadSessionArgs.cs`, `StopSessionArgs.cs`
- [ ] Register `list_sessions`, `spawn_session`, `send_to_session`, `read_session`, `stop_session` tools on `AgentMcpServer`
- [ ] Async mailbox: on worker turn complete → delay-0 schedule back to supervisor

### P12.5 Native scheduling redirect (FR.14.11)
- [DONE] Add `Constants.Instructions.NativeSchedulingRedirect` referencing `Agent.McpServerName`
- [DONE] Add `IsAgentToolsEnabled` to `InstructionOptionsModel`
- [DONE] Emit the redirect line from `InstructionBlockBuilder.Build` and `SessionViewModel.BuildInstructionBlock` when `AgentToolsEnabled`
- [DONE] Pass `AppSettings.AgentToolsEnabled` from `SessionTurnService` to the options record

---

## Phase 13 — Local Model Support via Ollama (FR.12.13, FR.12.14)

### P13.1 Model infrastructure
- [DONE] `Models/ModelProvider.cs` — `enum ModelProvider { Anthropic, Ollama }`
- [DONE] `Models/ClaudeModelInfo.cs` — add `Provider` parameter (default `Anthropic`)
- [DONE] `Models/AppSettingsModel.cs` — add `OllamaBaseUrl` (default `http://localhost:11434`); replace `SelectedModelIndex: int` with `SelectedModelId: string`
- [DONE] `Models/DirectoryNodeModel.cs` — replace `SelectedModelIndex: int` with `SelectedModelId: string`
- [DONE] `Constants.cs` — add `Ollama` nested class with `AuthToken`, `TagsPath`, `DiscoveryTimeoutMs`

### P13.2 Ollama discovery service (FR.12.13)
- [DONE] `Services/IOllamaModelService.cs` — interface
- [DONE] `Services/OllamaModelService.cs` — `GET {baseUrl}/api/tags`, 2.5s timeout, silent fail on any error
- [DONE] `Services/ClaudeModelService.cs` — inject `IOllamaModelService` + `IAppSettingsService`; fetch Anthropic + Ollama in parallel on startup; merge (Anthropic first)
- [DONE] `App.axaml.cs` — register `IOllamaModelService → OllamaModelService`

### P13.3 Local model routing (FR.12.14)
- [DONE] `Services/IClaudeProcessManager.cs` — add `ollamaBaseUrl` param to `SendMessageAsync` and `RunPrintModeAsync`
- [DONE] `Services/ClaudeProcessManager.cs` — when `ollamaBaseUrl != null`: set `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_API_KEY`; skip profile/proxy

### P13.4 ViewModel & assist updates
- [DONE] `Services/ClaudeAssistService.cs` — skip Ollama models in fallback; update to use `SelectedModelId` string
- [DONE] `ViewModels/SessionViewModel.cs` — string ID persistence; `RebuildModelList` shows model IDs; `SelectedLocalBaseUrl`; pass `ollamaBaseUrl` at all 4 `SendMessageAsync` sites; re-select by ID in `OnModelsUpdated`
- [DONE] Tests updated for new `GetModelFallbackOrderFromId` method signature

---

## Phase 16 — Orchestration Enhancements (FR.15.5–FR.15.10, FR.16, FR.17) ✓ DONE

### [DONE] P16.1 Per-session model plumbing & bug fix (FR.16.3, FR.16.5)
- [DONE] Add `ModelId` (string?), `OrchestrationDepth` (int), `SupervisorNodeId` (string?) to `SessionNodeModel`
- [DONE] `SessionTurnService`: inject `IClaudeModelService`; resolve effective model + ollamaBaseUrl + disableTools from `node.ModelId` → dir setting → app setting; pass to `SendMessageAsync` (bug fix: scheduled turns were running on CLI default model instead of session model)
- [DONE] `SessionTreeViewModel.CreateSessionForAgent`: accept `depth` and `supervisorNodeId` params; set them on the new node

### [DONE] P16.2 Model Tier Governance (FR.16)
- [DONE] `Services/ModelTierService.cs` — static `GetTier(string? modelId)` (case-insensitive substring: fable=4, opus=3, sonnet=2, haiku=1, else=0)
- [DONE] `Constants.Agent.DefaultModelTier = 2` (Sonnet) and `MaxSchemaRetries = 3`
- [DONE] `AgentMcpServer`: enforce tier in `spawn_session`, `set_session_model`, `orchestrate_parallel`, `orchestrate_pipeline`
- [DONE] `AgentMcpServer`: add `set_session_model` tool
- [DONE] `AgentMcpServer`: enforce depth in `spawn_session`, `orchestrate_parallel`, `orchestrate_pipeline`
- [DONE] `AgentMcpServer`: enforce `MaxConcurrentWorkers` via global `SemaphoreSlim`

### [DONE] P16.3 Token capture (FR.17)
- [DONE] `Models/ClaudeStreamEvent.cs` — add `InputTokens`, `OutputTokens`, `CostUsd`
- [DONE] `Models/TurnResultModel.cs` — add `InputTokens`, `OutputTokens`, `CostUsd`
- [DONE] `ClaudeProcessManager.ParseResultEvent` — extract `usage.input_tokens`, `usage.output_tokens`, `total_cost_usd`
- [DONE] `SessionTurnService.HandleEvent` — capture usage from result event into `TurnResultModel`

### [DONE] P16.4 Orchestration Budget (FR.15.6)
- [DONE] In-memory `ConcurrentDictionary<string, BudgetEntry>` in `AgentMcpServer` for active budgets
- [DONE] `get_budget` MCP tool
- [DONE] Accrue tokens per worker turn in `orchestrate_parallel` / `orchestrate_pipeline`
- [DONE] Block new worker spawns when budget exhausted

### [DONE] P16.5 Schema-enforced output (FR.15.7)
- [DONE] Add `JsonSchema.Net` NuGet to `ClaudeMaximus.csproj`
- [DONE] Schema validation helper in `AgentMcpServer`: validate response, build re-prompt instruction, retry up to `MaxSchemaRetries`
- [DONE] Wire into `spawn_session`, `send_to_session`, fan-out tools

### [DONE] P16.6 Fan-out primitives (FR.15.8)
- [DONE] `orchestrate_parallel` MCP tool + schema + tool registration
- [DONE] `orchestrate_pipeline` MCP tool + schema + tool registration

### [DONE] P16.7 Progress tracking + misc (FR.15.9, FR.15.10)
- [DONE] `workflow_phase` MCP tool
- [DONE] `workflow_log` MCP tool
- [DONE] Wire all new tools into `HandleToolsList` and `HandleToolCallAsync`
- [DONE] `Constants.Instructions.NoWorkflowTool` — updated to mention all new CMX fan-out tools

---

## Phase 18 — Status Bar: Usage & Model Pricing (FR.18)

### P18.1 Model catalog — curated static list (FR.18.7)
- [DONE] `Models/ClaudeModelInfo.cs` — add `InputPricePerMillion` and `OutputPricePerMillion` decimal fields
- [DONE] `Services/IClaudeModelService.cs` — simplify `EnsureModelsLoadedAsync` (no CLI params needed)
- [DONE] `Services/ClaudeModelService.cs` — replace dynamic CLI fetch with curated static catalog; keep live Ollama discovery
- [DONE] `Constants.cs` — add `Usage` nested class

### P18.2 Usage service (FR.18.4–18.6)
- [DONE] `Models/ClaudeUsageData.cs` — record: FiveHourUtilization, FiveHourResetsAt, FiveHourSeverity, SevenDay…
- [DONE] `Services/IClaudeUsageService.cs` — interface with CachedUsage, UsageUpdated event, SetActiveProfile
- [DONE] `Services/ClaudeUsageService.cs` — HTTP GET /api/oauth/usage; reads token from .credentials.json; 5-min polling
- [DONE] `App.axaml.cs` — register IOllamaUsageService (sic IClaudeUsageService) singleton

### P18.3 Status bar UI (FR.18.1–18.3)
- [DONE] `ViewModels/MainWindowViewModel.cs` — StatusBarModelText, FiveHourUtilization, SevenDayUtilization, labels, HasUsageData; subscribe to ModelsUpdated + UsageUpdated; call SetActiveProfile on session switch
- [DONE] `Views/MainWindow.axaml` — DockPanel.Dock="Bottom" status bar with model label + 2 stacked ProgressBars

---

## Phase 19 — Session View Toolbar: Sub-Session Stop + Schedule Indicator (FR.14, FR.15)

### P19.1 Stop button for sub-sessions (FR.15) ✓ DONE
- [DONE] `ViewModels/SessionViewModel.cs` — `IsNodeRunning` mirrors `SessionNodeViewModel.IsRunning`; `CanStop` combines `IsBusy || IsNodeRunning`
- [DONE] `StopCommand` — cancels `_sendCts` for user sends, or calls `ISessionTurnService.CancelTurn(nodeId)` for orchestrated turns
- [DONE] `Views/SessionView.axaml` — Stop button `IsVisible` bound to `CanStop` instead of `IsBusy`

### P19.2 Schedule indicator button (FR.14) ✓ DONE
- [DONE] `Services/ISchedulerService.cs` / `SchedulerService.cs` — add `ScheduleChanged` event, raised on add/remove/fire
- [DONE] `ViewModels/SessionViewModel.cs` — inject `ISchedulerService`; `HasActiveSchedule`, `ScheduleTooltip`, `CancelScheduleCommand`, `RefreshScheduleState()`, `CancelAllSchedules()`
- [DONE] `ViewModels/MainWindowViewModel.cs` — thread `ISchedulerService` through to `SessionViewModel`
- [DONE] `Views/SessionView.axaml` — clock button (row between Stop and Settings), visible only when `HasActiveSchedule`, tooltip shows schedule details
- [DONE] `Views/SessionView.axaml.cs` — `OnCancelScheduleRequested` shows `ShowConfirmOverlayAsync` confirmation, then cancels all schedules for the node

---

## Backlog / Future

- [ ] **P2.3 Search unit tests** — match / no-match / ancestor expansion
- [ ] Session file watcher (detect disk deletion so tree delete button becomes available)
- [ ] Integration tests for `ClaudeProcessManager` with a mock process
- [ ] **Touchscreen UX review** — Review all UI interactions for touchscreen compatibility, plan touch-friendly gestures (long-press for context menu, swipe for move, tap-and-hold for drag), review hit target sizes, consider touch-specific affordances
