# TessynDesktop — TODO

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

### P8.6 Auto-Commit tri-state rework (FR.11.2, FR.11.3, FR.11.9, FR.11.10, FR.11.12)
- [ ] `SessionNodeModel.IsAutoCommit`: `bool` → `bool?` (`true`=Commit, `false`=Do-not-commit, `null`=No preference; new sessions default `false`)
- [ ] `InstructionOptionsModel.IsAutoCommit` → `bool?`; `InstructionBlockBuilder.Build` emits Commit/Do-not-commit/nothing accordingly, returns `""` when the block would otherwise be empty
- [ ] Reword `Constants.Instructions.Delimiter` to an honest preamble (no "do not acknowledge"); keep matching the old delimiter for stripping back-compat on existing JSONL sessions
- [ ] `SessionViewModel`/`MainWindowViewModel` `IsAutoCommit` proxies → `bool?`; mid-run correction covers the silent transition
- [ ] `MainWindow.axaml` Auto-Commit `ToggleButton` → `IsThreeState="True"` with three distinct visuals/tooltips
- [ ] Verify txt/MD render still strips the block while raw JSONL view keeps it

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

### P8.8 Cross-profile session adoption (FR.11.13) ✓
- [DONE] `IClaudeSessionStatusService.TryAdoptSessionFromOtherProfile` — locates a session's JSONL under any other profile (Default + every profile dir, including removed ones) and moves (not copies) it into the active profile's store
- [DONE] Wired into interactive send (`TurnController.SendAsyncCore`) and headless/orchestrated turns (`SessionTurnService.RunTurnAsync`, replacing the old Default-only resumability preflight). No longer used by Auto-Compact as of P8.10 below — its compaction call is stateless and needs no `--resume` lookup at all.

### P8.9 Auto-Compact failure safety (FR.11.6) ✓
- [DONE] `SendCompactionPromptAsync` now tracks `evt.IsError` across the compaction turn's stream events; any error (auth/reauth failure, non-zero exit code, etc.) or a wholly empty result aborts applying the result — no file rewrite, no JSONL detach — even if partial assistant text was captured first
- [DONE] Fixes bug: a reauth request mid-compaction was previously treated as a successful (empty) compaction, clearing the session
- [DONE] Failure surfaces a system message in the Output Panel; an invalid-credentials failure also sets `HasAuthError`

### P8.10 Embed transcript directly into compaction, drop --resume (FR.11.6) ✓
- [DONE] `SessionPromptComposer.BuildCompactionTranscript()` — reads the text Session File, takes USER/ASSISTANT entries since the last `[COMPACTION]` marker (or the whole file if none), formats each as `[timestamp] ROLE` + content
- [DONE] `Constants.Instructions.CompactionPromptTemplate` extended with a `SESSION TRANSCRIPT TO COMPACT:` section (`{1}` placeholder)
- [DONE] `TurnController.SendCompactionPromptAsync` now sends the compaction call statelessly (`sessionId: null`); removed the now-pointless cross-profile-adoption pre-call (P8.8) since nothing is resumed anymore
- [DONE] `BuildCompactionPrompt()` returns `null` when there's nothing to compact yet; caller shows `[Nothing to compact]` and skips the CLI call entirely instead of making a wasted request
- [DONE] Fixes bug: compaction previously relied on Claude's own resumed JSONL memory, which is incomplete when a session has been resumed under different profiles (Cross-Profile Session Adoption only relocates whichever single JSONL currently exists) or when Anthropic and local Ollama turns are mixed within one session — both silently compacted only part of what the user saw in the Output Panel

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

### P10.6 Profile Rename/Delete (FR.12.19)
- [DONE] `ProfileListItemModel` (DisplayName + ProfileId) backs `AvailableProfiles` so rename/delete target a specific profile, not by display-name matching
- [DONE] ~~ComboBox ItemTemplate context menu~~ — implemented, then found never reliably testable/used (nested `ContextMenu`-inside-`ComboBox`-`Popup` is a known-fragile Avalonia pattern); replaced with plain "Rename"/"Delete" command-bar buttons acting on the current selection
- [DONE] `ProfileController.CanManageSelectedProfile` — true when a stored (non-Default) profile is selected; drives the new buttons' visibility, independent of `HasAuthError`
- [DONE] `SessionViewModel.RenameProfile` — updates stored `DisplayName` via input dialog
- [DONE] `SessionViewModel.DeleteProfile` — same removal as FR.12.18 Remove button, usable without an active auth error; `HandleRemoveProfile` (✕ button) still uses it for the auth-error-gated path, now alongside the always-visible Delete button
- [DONE] `ProfileListItemModel.IsSystem` flag; Default entry prefixed `"[Sys] "` and rendered dark grey (`BoolToSystemProfileForegroundConverter`), stored profiles stay black
- [DONE] Re-authenticate/Rename/Delete buttons switched from text labels to icons (🔑/✏/🗑), with `ToolTip.Tip` retained for discoverability; ✕ Remove kept its existing icon
- [DONE] ✕ Remove button now confirms before deleting (previously fired immediately) — both it and Delete route through a shared `ConfirmAndDeleteSelectedProfileAsync` in `SessionView.axaml.cs`
- [DONE] Re-authenticate button visibility has gone back and forth (always-visible ↔ `IsVisible="{Binding HasAuthError}"`) while the ISSUE.004 fix awaited a real deployment to test against; currently **always-visible** per explicit request — see ISSUE.004 for why the running app couldn't yet reflect the fix (stale `[Publish]` build, unrelated to this binding)
- [DONE] Fixed root cause of `HasAuthError` never firing: `ParseResultEvent` only read `"errors"`/`"error"` JSON fields; auth-failure text lives in `"result"` (ISSUE.004)
- [DONE] Confirmed after restart onto a fresh build: right-click on a Profile dropdown row indeed does not open a context menu — validates the ISSUE.005 nested-popup hypothesis. Not urgent (buttons cover the functionality); no fix attempted this round.
- [DONE] Buttons moved out of the always-visible command bar into a flyout `Popup`, opened by clicking a "Profile: ▾" toggle label (`ProfileActionsToggle`) — was crowding the bar. Buttons shrunk (`Padding="4,2"`). Popup uses `IsLightDismissEnabled="False"` + one-way `IsOpen` binding to `ProfileActionsToggle.IsChecked` (mirrors the existing `AutocompletePopup` pattern in this file) rather than light-dismiss, to avoid the toggle visually desyncing from the popup's open state.
- [DONE] Fixed: a removed (`_rem`) profile's tree sessions kept their name/compact log (never depended on the profile), but every feature that reads the *live Claude CLI JSONL* directly — resumability dot, raw "Claude Session View" toggle + its file watcher, and the Import picker's cross-project discovery — hardcoded `~/.claude/projects/<slug>/`, so they went blind for any session run under a non-Default profile once that profile could no longer be found there (worse after removal, but really any non-Default profile, active or `_rem`). Added `Constants.ClaudeSessions.GetAllProjectsDirs()`/`FindExistingJsonlPath(...)` (Default + every directory under `profiles/`, active or `_rem`, mirroring the ISSUE.003 index fix) and switched `ClaudeSessionStatusService.IsSessionResumable`, `SessionContentViewModel.BuildJsonlPathForSessionId`/`StartJsonlWatcher`, and `ClaudeSessionImportService.DiscoverSessions` (now takes an optional `projectsDirsOverride` for testing) to use it.

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

## Phase 20 — Instruction-Block Injection Timing + /deep-research Ban (FR.14, FR.15)

### P20.1 Fix scheduled-turn profile mismatch (ISSUE.001) — DONE
- [DONE] `Services/SessionTurnService.cs` — `ResolveProfileConfigDir` now scans the session's own history for the last recorded `ProfileName` (same as `SessionViewModel.RestoreLastUsedSettings`), instead of reading the shared per-directory `SelectedProfileIndex` — fixes scheduled/orchestrated turns authenticating as the wrong account and getting 401s

### P20.2 Ban /deep-research skill alongside Workflow/Agent — DONE
- [DONE] `Constants.cs` — `NoWorkflowTool` text extended to also ban the `/deep-research` skill, redirecting to ClaudeMaximus MCP orchestration tools

### P20.3 Inject tool-usage bans once per fresh session instead of every turn — DONE
- [DONE] `Models/InstructionOptionsModel.cs` — add `IsNewSession` flag
- [DONE] `Services/InstructionBlockBuilder.cs` — gate `NativeSchedulingRedirect`/`NoWorkflowTool` on `IsAgentToolsEnabled && IsNewSession` (auto-commit/branch/document toggles remain per-turn, unaffected)
- [DONE] `Services/SessionTurnService.cs` — compute `IsNewSession = node.ClaudeSessionId == null`, evaluated after the resumability check so a context reset re-injects the bans
- [DONE] `ViewModels/SessionViewModel.cs` — `BuildInstructionBlock` now delegates to the shared `InstructionBlockBuilder` (previously a separate, drifted copy missing `NoWorkflowTool` entirely) with `IsNewSession` from `ClaudeSessionId`/`ExternalId == null`
- Confirmed no change needed for txt-view/JSONL-view separation — already correct: `.txt` session files only ever store the clean prompt (never the instruction block), and `ClaudeSessionImportService.ExtractUserContent` already strips the instruction block unless `raw: true` (used by the JSONL toggle), which is the existing mechanism for "cut it out in txt view, show everything in JSONL view"

---

## Phase 21 — Orchestrated Child Profile Inheritance (ISSUE.002) — DONE

### P21.1 Fix spawned children falling back to Default profile — DONE
- [DONE] `Models/SessionNodeModel.cs` — add persisted `ProfileName` (pinned profile for a session's headless turns; null = resolve from history, then Default)
- [DONE] `Services/ISessionTurnService.cs` / `SessionTurnService.cs` — add `ResolveProfileName(node)` (returns pinned `ProfileName` if set, else last profile from history); `ResolveProfileConfigDir` now builds on it
- [DONE] `Services/AgentMcpServer.cs` — `InheritSupervisorProfile(child, supervisor)`, called in `spawn_session`, `orchestrate_parallel`, and `orchestrate_pipeline` right after child creation, pinning the supervisor's resolved profile onto the child so it authenticates with the same account instead of falling back to a possibly-revoked Default
- Documented as `docs/issues/ISSUE.002.orchestrated-child-profile-inheritance.md`; `ISSUE.001` marked fixed with a pointer to this follow-on

---

## Phase 22 — Markdown-Centric Orchestration I/O + CMX Orchestration Skill (FR.15.7, FR.15.8, FR.15.11)

### P22.1 Replace JSON-Schema output validation with advisory markdown guidance (FR.15.7) — DONE
- [DONE] `Services/AgentMcpServer.cs` — `spawn_session`, `send_to_session`, `orchestrate_parallel`, `orchestrate_pipeline` now accept `outputFormat` (free-text/markdown shape hint) instead of `schema` (JSON Schema)
- [DONE] `ApplyOutputFormat` helper appends the hint as advisory prompt guidance (plus the file-artifact convention for large/structured results); applied to every worker's prompt for `orchestrate_parallel`, and to the **last stage's** prompt only for `orchestrate_pipeline`
- [DONE] Removed `ValidateAndRetryAsync`, the `JsonSchema.Net` (`Json.Schema`) usage, and `Constants.Agent.MaxSchemaRetries` — no retry loop, no `[schema-invalid]` prefix
- [DONE] All four tool input schemas + `HandleToolsList` descriptions updated; `Constants.Instructions.NoWorkflowTool` reworded (`schema` args → `outputFormat`)

### P22.2 Markdown aggregation for fan-out tools (FR.15.8) — DONE
- [DONE] `orchestrate_parallel` / `orchestrate_pipeline` no longer return a JSON array — `BuildWorkerResultsMarkdown` / `BuildPipelineResultsMarkdown` assemble one markdown document, one `## name`/`## item` section per result with a trailing `_tokens: in=… out=…_` line

### P22.3 CMX Orchestration Skill, provisioned into every profile (FR.15.11) — DONE
- [DONE] `Constants.cs` — `Agent.OrchestrationSkillName` (`cmx-orchestration`), `SkillsFolderName`, `SkillFileName`, and the `OrchestrationSkillMarkdown` body (supervisor guidance + worker guidance sections)
- [DONE] `Services/ISkillProvisioningService.cs` / `SkillProvisioningService.cs` — new service; `ProvisionAll()` writes `SKILL.md` into every registered profile's `skills/cmx-orchestration/` folder, write-if-changed (mirrors `AgentMcpServer.EnsureConfigFileAsync`), gated on `AgentToolsEnabled`, skips the Default profile (no dedicated config dir)
- [DONE] `App.axaml.cs` — registered the service; `ProvisionAll()` called at startup alongside `AgentMcpServer.Start()`
- [DONE] Skill pointer folded into `Constants.Instructions.NoWorkflowTool` — surfaced once per fresh session under the existing `IsAgentToolsEnabled && IsNewSession` gate, not repeated every turn
- [DONE] Provisioned immediately into all 7 currently-registered profiles by hand (the running app instance hosting this session can't be restarted to pick up the new build without severing it) — verified byte-identical across all 7 against the un-indented `Constants.cs` source

## Phase 23 — Favorite Sessions (FR.9.7)

### P23.1 Model & persistence
- [ ] `SessionNodeModel.IsFavorite` (bool), persisted via existing tree serialization
- [ ] `SessionNodeViewModel.IsFavorite` — reactive, writes through to `Model.IsFavorite`
- [ ] `SessionViewModel.IsFavorite` passthrough (persists via `_appSettings.Save()`)

### P23.2 Recent-dropdown inclusion + affordance
- [ ] `MainWindowViewModel.RefreshRecentDropdown` — favorites always included regardless of recency/hidden state; sort order unchanged
- [ ] Extracted, unit-tested predicate for dropdown inclusion
- [ ] `MainWindowViewModel.SetFavorite(session, value)`
- [ ] Dropdown row: star (favorited, click to un-favorite) vs hide icon (not favorited, click to hide) — mutually exclusive by `IsFavorite`
- [ ] Replace hide icon glyph (👁) with monochrome ⦸ (U+29B8)

### P23.3 UI
- [ ] Shared star `StreamGeometry` resource in `App.axaml`
- [ ] Command-bar star `ToggleButton` in `SessionView.axaml`, left of Model selector (grey/transparent unchecked, black/gold checked)
- [ ] Display-only favorite star in `SessionTreeView.axaml` and `RecentSessionsView.axaml` top row, right side, above last-activity date/time

### P23.4 Tests
- [ ] Unit tests for the recent-dropdown inclusion predicate (favorite+stale+hidden included; non-favorite+stale excluded; non-favorite+hidden excluded; non-favorite+recent+not-hidden included)

---

## Phase 24 — Streaming Session Mode (FR.19) ✓ DONE

### [DONE] P24.0 Spike — verify `--input-format stream-json` against the installed CLI (gate before any other P24 work)
- [DONE] Confirmed exact spawn args and stdin-message JSON schema empirically: one JSON object per line, `{"type":"user","message":{"role":"user","content":[{"type":"text","text":"..."}]}}`, stdin never closed. `claude` does not exit after a turn's `result` event under `--input-format stream-json` — confirmed via a standalone Node harness, not assumed from `--help` text.
- [DONE] Confirmed a `run_in_background` bash task survives across turns: the process spontaneously emitted an unprompted `task_notification` → `assistant` message reporting the background task's completion mid-way between two explicit sends, with no app-side polling.

### [DONE] P24.1 Persistent process lifecycle (FR.19.2, FR.19.3, FR.19.4, FR.19.5)
- [DONE] `Services/ClaudeStreamEventParser.cs` — extracted from `ClaudeProcessManager` (same static parsing logic, now shared)
- [DONE] `Services/ClaudeProcessLauncher.cs` — extracted the `ProcessStartInfo` build + Windows cmd.exe retry + profile/Ollama/proxy env-var setup from `ClaudeProcessManager.TryStartProcess`, shared by both One-shot and Streaming managers
- [DONE] `Services/IClaudeStreamingSessionManager.cs` + `ClaudeStreamingSessionManager.cs` — `EnsureStarted`, `SendTurnAsync` (turn-boundary via the next `result` event), `IsAlive`, `Stop` (kills process tree), `TerminateAll`, `ProcessExited` event
- [DONE] `MainWindowViewModel.TerminateAllSessions` calls `IClaudeStreamingSessionManager.TerminateAll()` alongside the existing one-shot `TerminateAll()`
- [DONE] **Bug found and fixed during verification**: `Encoding.UTF8` (used for `StandardInputEncoding`/`StandardOutputEncoding`/`StandardErrorEncoding` in the launcher) writes a BOM preamble on the first write to a redirected stream. One-shot Mode's `--input-format text` silently tolerated the stray BOM character at the start of every prompt ever sent; `--input-format stream-json`'s strict per-line JSON parser rejected it outright ("Unrecognized token"). Fixed by switching to `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` for all three streams — benefits both modes.

### [DONE] P24.2 Model & persistence (FR.19.6)
- [DONE] `SessionNodeModel.IsStreamingMode` (bool, default false), persisted via existing tree serialization

### [DONE] P24.3 Turn routing + Stop semantics (FR.19.1, FR.19.4, FR.19.5, FR.19.7, FR.19.8)
- [DONE] `TurnController.SendAsyncCore` branches between One-shot Mode (existing path, now also passing `disallowNativeScheduling`) and Streaming Session Mode (`SendViaStreamingAsync`, new manager)
- [DONE] `CanStop` extended: `IsBusy || IsNodeRunning || IsStreamingProcessAlive`
- [DONE] `Stop()` kills the streaming process first when one is alive (takes priority over the one-shot cancel/orchestrated-cancel paths)
- [DONE] `IsStreamingMode` setter defers to a confirmation dialog (`StreamingDisableConfirmationRequested` event, mirroring `CancelScheduleRequested`) when turning off while a process is alive; `SessionView.axaml.cs` wires it to `ShowConfirmOverlayAsync`
- [DONE] Model/Profile/Effort `ComboBox`es bound `IsEnabled="{Binding !IsStreamingProcessAlive}"` in `SessionView.axaml`

### [DONE] P24.4 Steering gating (FR.19.9, FR.19.10; amends FR.11.2, FR.14.11)
- [DONE] `InstructionOptionsModel.IsStreamingMode`; `InstructionBlockBuilder.Build` omits `NativeSchedulingRedirect` when `IsStreamingMode` is true, keeps `NoWorkflowTool` unconditional on mode
- [DONE] Threaded through both call sites: `SessionPromptComposer.BuildInstructionBlock` (from `_node.Model.IsStreamingMode`) and `SessionTurnService.RunTurnAsync` (hardcoded `false` — headless turns are always one-shot, FR.19.11)
- [DONE] `Constants.Instructions.NativeSchedulingToolNames` array; `ClaudeProcessManager.BuildArguments` appends `--disallowed-tools` from it when `disallowNativeScheduling` is true; all One-shot Mode call sites (main send, context retry, compaction, mid-run corrections) pass `AgentToolsEnabled` for this flag. Streaming spawns never pass it.

### [DONE] P24.5 Headless-turn limitation (FR.19.11) — documentation only, no code change planned for v1
- [DONE] Confirmed `SessionTurnService` routes scheduled/orchestrated turns through the existing one-shot `--resume` path unconditionally, unaffected by the target session's `IsStreamingMode`

### [DONE] P24.6 UI (FR.19.1)
- [DONE] Streaming `ToggleButton` (📡) in `SessionView.axaml`, beside the existing `--chrome` toggle in the command bar

### P24.7 Tests
- [DONE] `InstructionBlockBuilder` gating unit tests (mode × `AgentToolsEnabled` × `IsNewSession` matrix) — `InstructionBlockBuilderTests.cs`, 5 tests
- [DONE] Streaming manager lifecycle verified end-to-end against the real CLI via a throwaway harness project (not committed as an automated test — this codebase has no precedent for mocking `Process`; `ClaudeProcessManager` itself has never had unit tests, see the deferred backlog item below): confirmed single spawn reused across two turns, correct turn-boundary detection, `Stop` kills the process and fires `ProcessExited`, and `SendTurnAsync` throws after Stop
- [ ] `CanStop`/toggle-confirmation tests — deferred; no existing test precedent for ViewModel-layer classes in this codebase (`TurnController`/`SessionViewModel` live in the `TessynDesktop` project, not `.Core`, and pull in Avalonia/ReactiveUI)
- [ ] E2E use case for Streaming Session Mode — deferred, needs Architect sign-off on whether it belongs in the existing E2E harness

---

## Phase 25 — Usage Cache Staleness on Profile Selection (FR.18.8) ✓ DONE

### P25.1 Staleness policy & wiring
- [DONE] `Services/ClaudeUsageStalenessService.cs` — static `IsFiveHourStale`/`IsSevenDayStale`/`ZeroStaleWindows`, mirroring the existing `ModelTierService` pure-utility pattern (no interface, no DI registration)
- [DONE] `Constants.Usage.NormalSeverity`; replaced the 4 inline `"normal"` literals in `ClaudeUsageService.ParseUsage`
- [DONE] `ClaudeUsageService.SetActiveProfile` — cache-restore path now runs through `ZeroStaleWindows` before being shown
- [DONE] `MainWindowViewModel.UpdateStatusBarUsage` — reuses `IsFiveHourStale`/`IsSevenDayStale` to suppress the "resets …" suffix for whichever window was just zeroed

### P25.2 Tests
- [DONE] `ClaudeUsageStalenessServiceTests.cs` — fresh/five-hour-stale/seven-day-stale/both-stale/exact-boundary cases, 9 tests

### P25.3 Docs
- [DONE] `docs/requirements.md` — FR.18.8

---

## Phase 26 — External MCP Server Registry & Background Model Routing (FR.20, FR.12.20) ✓ DONE

### [DONE] P26.1 Background ("small-fast") model routing (FR.12.20)
- [DONE] `ClaudeProcessLauncher.TryStartProcess` — sets `ANTHROPIC_SMALL_FAST_MODEL` to the curated Haiku catalog entry (FR.18.7, `Constants.ClaudeAssist.PreferredModelId`) for Anthropic-routed sessions only
- [DONE] Confirmed Ollama-routed sessions never set `ANTHROPIC_SMALL_FAST_MODEL` — the line lives in the Anthropic-routing `else` branch, never the Ollama `if` branch

### [DONE] P26.2 Registry data model & persistence (FR.20.1)
- [DONE] `Models/ExternalMcpServerModel.cs` — Id, DisplayName, Enabled, Transport (Stdio/Http), Command/Arguments/WorkingDirectory, Url, AuthType (None/StaticHeader/OAuth2), StaticHeaderName/Value, OAuth config
- [DONE] `Models/McpOAuthConfig.cs`, `McpServerTransport.cs`, `McpServerAuthType.cs`, `McpTokenRecord.cs`, `McpAuthorizeResult.cs`
- [DONE] `AppSettingsModel.ExternalMcpServers` (`List<ExternalMcpServerModel>`)

### [DONE] P26.3 OAuth client (FR.20.3–FR.20.6)
- [DONE] `Services/IMcpOAuthService.cs` / `McpOAuthService.cs` — endpoint discovery (`.well-known/oauth-protected-resource` resolves the authorization-server base, then `.well-known/oauth-authorization-server` for the actual endpoints), fallback to manual config
- [DONE] Dynamic Client Registration (RFC 7591) attempt when `registration_endpoint` is present
- [DONE] Interactive authorization: PKCE (S256) + ephemeral loopback `HttpListener` + system browser + code exchange
- [DONE] Token storage: DPAPI-protected (`ProtectedData`, `CurrentUser`) files under `%AppData%\TessynDesktop\mcp-tokens\<Id>.token` (`McpTokenStore`)
- [DONE] Transparent refresh (`GetValidAccessTokenAsync`) before each `--mcp-config` build; marks entry "Needs re-authorization" and omits it from that turn's config on refresh failure

### [DONE] P26.4 CLI merge (FR.20.2)
- [DONE] `Services/IMcpConfigMerger.cs` / `McpConfigMerger.cs` — extracted from `AgentMcpServer` (single `IMcpOAuthService` dependency, independently unit-testable) to build the registry-derived `mcpServers` entries; `AgentMcpServer.BuildMcpConfigJsonAsync` merges its output alongside the always-present `cmx` entry
- [DONE] Stdio → `type:"stdio"` + split args; Http → `type:"http"` + resolved bearer/static header; disabled entries and unauthorized/refresh-failed OAuth2 entries omitted entirely
- [DONE] Reuses the existing tool-capability gate — tool-incapable Ollama models still skip `--mcp-config` entirely (verified at both the interactive send path in `TurnController` and the headless path in `SessionTurnService`)

### [DONE] P26.5 Settings UI (FR.20.7)
- [DONE] New "MCP Servers" tab in `SettingsWindow.axaml` — list (enable checkbox, transport/auth summary, live status) + Add/Edit/Remove/Authorize buttons acting on the current selection (mirrors the P10.6 profile-management pattern)
- [DONE] `McpServerRowViewModel` — per-row display + status (`Not connected` / `Connected as <identity>` / `Needs re-authorization` / `—` for non-OAuth2 entries), computed from `IMcpTokenStore.Load` + `McpOAuthConfig` flags
- [DONE] `McpServerEditViewModel` + `Views/McpServerEditDialog` — full Add/Edit form covering Stdio and Http × None/StaticHeader/OAuth2, with conditional field visibility; preserves `DiscoveryCompleted`/`NeedsReauthorization`/`ConnectedIdentity` across an edit of an already-authorized entry
- [DONE] `Views/ConfirmDialog` — new generic Yes/No dialog (reusable from any owner `Window`, unlike `MainWindow.ShowConfirmOverlayAsync` which only renders into `MainWindow`'s own visual tree); used for the destructive Remove action
- **Implementation note:** registry Add/Edit/Remove/Enable-toggle persist immediately (`_appSettings.Save()` on each action), independent of the Settings window's main Save button which continues to govern the window's other (copied-field) settings — chosen because Authorize already has its own immediate side effect (network call + token write), so gating the rest of the registry behind a separate Save click would be an inconsistent, partial undo story

### [DONE] P26.6 Tests
- [DONE] `McpConfigMergerTests.cs` — enabled/disabled entries, Stdio vs Http shape, None/StaticHeader/OAuth2 header resolution, unauthorized OAuth2 entry omitted, mixed-entry filtering (9 tests)
- [DONE] `McpOAuthServiceTests.cs` — non-OAuth entry, never-authorized, valid non-expiring token (no HTTP call), far-from-expiry (no refresh), near-expiry (triggers refresh), successful refresh + persistence, refresh response omitting `refresh_token`, no-refresh-token failure, refresh-endpoint-error failure, refresh-endpoint-throws failure, `NeedsReauthorization` cleared on subsequent success (11 tests), via a hand-rolled `FakeHttpMessageHandler` (no mocking library, matching this project's existing test conventions)
- [DONE] `McpTokenStoreTests.cs` — DPAPI round-trip (all fields, null refresh/expiry), overwrite, delete, no-file, independent per-server-id storage (6 tests)
- Full suite: 266 passing, 0 failing

### [DONE] P26.7 Bug fix — unbounded discovery-on-blur could hang Settings with climbing memory (FR.20.3)
- [DONE] `Constants.ExternalMcp.DiscoveryTimeoutMs` (8s) — `McpOAuthService.DiscoverAsync` now bounds its `.well-known/...` probes to this internal timeout via a linked `CancellationTokenSource`, independent of the shared `HttpClient`'s 100s default; without it, a server that doesn't 404 unknown paths could hold the connection open and let `GetStringAsync` buffer an unbounded response body in memory
- [DONE] `McpServerEditDialog` cancels any in-flight discovery before starting a new one on the next URL-field `LostFocus`, and on dialog close — previously every focus loss fired an un-cancelled, un-de-duped discovery request, so repeatedly tabbing in/out of the URL field could stack up multiple concurrent unbounded probes
- [DONE] `McpServerEditViewModel.IsDiscovering` — bound in `McpServerEditDialog.axaml` to disable the URL and OAuth endpoint fields (and show a "Discovering…" label) while a probe is running, per the Architect's request
- [DONE] `McpOAuthServiceTests` (15 tests) still green after the timeout change
- [DONE] Follow-up: the 8s timeout was verified correct against a real hung socket (a throwaway probe using a non-responding `HttpListener` confirmed `DiscoverAsync` returns in ~8.1s), so the *network* side wasn't the residual freeze the Architect reported on **reopening** (not first-opening) the dialog. Root cause was `IsDiscovering`'s `IsEnabled` bindings disabling still-tab-adjacent, focusable controls (the Authentication `ComboBox`, `ClientId`) synchronously from within the `LostFocus` handler that was still mid-dispatch — reentrant focus-manager territory, and exactly the kind of bug that manifests as a hang on a second/later open rather than the first. Fix: `IsEnabled="{Binding !IsDiscovering}"` trimmed to only the fields `DiscoverAsync` actually overwrites (URL, Authorization/Token/Registration Endpoint), and `OnUrlLostFocus` now `await Task.Yield()`s before touching `IsDiscovering`, so the disabling happens on the next UI-thread turn, after the originating `LostFocus` dispatch has fully unwound.

---

## Phase 27 — UI-Initiated Delayed Send (FR.14.12) ✓ DONE

### P27.1 Schedule-on-right-click
- [DONE] `Constants.Agent.DelayedSendNote` — note text recorded on UI-created schedules
- [DONE] `TurnController.ScheduleSend(inputText, delay)` — builds a `Delay`-kind self-wake `ScheduleModel` (same shape as `AgentMcpServer.ScheduleWakeAsync`) and calls `IDaemonClient.AddSchedule`; clears the input box/draft and posts a UI-only confirmation system message with the fire time
- [DONE] `SessionViewModel.ScheduleSend(delay)` — thin forwarding method (mirrors `DetachSession`)
- [DONE] `MainWindow` — new `ScheduleDelayCard` overlay (amount TextBox + Minutes/Hours/Days ComboBox) and `ShowScheduleDelayOverlayAsync()`, following the existing `ShowConfirmOverlayAsync`/`ShowInputOverlayAsync` TaskCompletionSource pattern
- [DONE] `SessionView` — Send button gets `PointerPressed` right-click detection (mirrors `OnMenuBarPointerPressed`'s button-discrimination pattern); no-ops when the input box is empty
- [DONE] No changes needed to `SchedulerService`, `ScheduleModel`, or `SessionTurnService` — the feature is pure UI wiring onto the existing, already-tested scheduling engine
- Build green; full suite 270 passing, 0 failing (no new automated tests: the scheduling engine itself is unchanged and already covered by `SchedulerServiceTests`, and `TurnController`/`SessionViewModel`/`MainWindow` are Avalonia-bound ViewModel/View-layer classes with no existing test precedent in this codebase, per the P24.7 note)

---

## Phase 28 — Telegram Notification Bridge (FR.21)

### P28.1 Data model & constants ✓ DONE
- [DONE] `AppSettingsModel`: `TelegramEnabled`, `TelegramChatId`, `TelegramAllowedUserIds`, `TelegramTopicThreadIdsByNodeId`, `TelegramUpdateOffset` (FR.21.1)
- [DONE] `Constants.Telegram` (API base URL, long-poll timeout, token file name)
- [DONE] `TurnSource.Telegram` new enum value
- [DONE] `SessionSummaryModel` gains `IsFavorite`, `LastActivityUtc`; `SessionRegistry.BuildSummary` populates both (FR.21.8)
- [DONE] `InstructionOptionsModel` gains `IsTelegramEnabled`; `Constants.Instructions.TelegramNotifyGuidance`; `InstructionBlockBuilder` emits it once per fresh session when agent tools + Telegram are both enabled (FR.21.3)

### P28.2 Core services ✓ DONE
- [DONE] `ITelegramTokenStore` / `TelegramTokenStore` — DPAPI single-file bot token store (FR.21.6)
- [DONE] `ITelegramClient` / `TelegramClient` — raw Bot API HTTP wrapper (sendMessage, createForumTopic, getUpdates)
- [DONE] `ITelegramBridgeService` / `TelegramBridgeService` — Start/Stop long-poll loop, `EnsureTopicForSessionAsync`, `NotifyAsync`, inbound routing + `/sessions` + `/open` (FR.21.2, FR.21.4)
- [DONE] Hook `EnsureTopicForSessionAsync` into `SessionTurnService.RunTurnAsync` (headless: scheduled/orchestrated/Telegram-triggered) and `TurnController.SendAsyncCore` (interactive)
- [DONE] `mcp__cmx__notify_user` tool on `AgentMcpServer` (FR.21.3)
- [DONE] `CompositeNotificationService` (app project) wraps `WindowsNotificationService` + `ITelegramBridgeService.NotifyAsync`, registered as `INotificationService` (FR.21.5)
- [DONE] DI wiring in `App.axaml.cs`: token store, client, bridge service (+ `Lazy<>` pair with `SessionTurnService` to avoid a DI cycle), Start/Stop alongside `AgentMcpServer`/`SchedulerService`, gated on `TelegramEnabled`

### P28.3 Settings UI ✓ DONE
- [DONE] Telegram panel: enable toggle, bot token field + immediate-save action, chat ID field, allowed-user-IDs field (FR.21.7)

### P28.4 Tests (see `docs/testcases/TC.21-Telegram-Bridge.md`) ✓ DONE (automated)
- [DONE] `TelegramTokenStoreTests`, `TelegramClientTests`, `TelegramBridgeServiceTests`
- [DONE] `SessionRegistryTests` / `InstructionBlockBuilderTests` additions
- [DONE] Full suite green (300 passing, 0 failing)
- [ ] Manual E2E checklist run against a real bot/supergroup (requires the Architect to provision a bot per FR.21's setup steps) before marking FR.21 fully verified end-to-end
  - [DONE] UC.21.1 (notify_user → topic auto-created, message delivered) — verified 2026-08-20 against `@VitalyTessynBot` / "Tessyn Desktop" supergroup
  - [DONE] UC.21.2/UC.21.3 (reply in topic → routed back as a turn on the correct session, response posted back to the topic) — verified 2026-08-20; confirmed the per-node turn lock correctly queues a Telegram-routed turn behind an in-flight interactive turn on the same session
  - [ ] UC.21.4 (`/sessions`), UC.21.5 (`/open`), UC.21.6 (unauthorized sender dropped), and update-offset persistence across an app restart — not yet exercised live

---

## Phase 29 — Telegram as a Daemon Client (FR.21.9–FR.21.11)

Reworks the Telegram Bridge from a notification-only channel into a full mirror of every session's conversation, and moves it onto the `IDaemonClient` boundary instead of Core service internals. Long polling only (D1) — the webhook/S3 relay (`tdcp-spec.md` §3) stays parked, see requirements.md Out of Scope.

### P29.1 Universal I/O mirror (FR.21.9)
- [DONE] `SessionEntryAppendedEventArgs` model; `ISessionFileService.EntryAppended` raised by `AppendMessage`/`AppendCompactionSeparator` (not by the bulk `RewriteSessionFile`/`WriteSessionFile` paths)
- [DONE] `ISessionRegistry.FindModelByFileName` / `IDaemonClient.FindSessionByFileName` — resolves an appended entry's session for topic lookup
- [DONE] `IDaemonClient.EntryAppended` relayed by `InProcDaemonClient` from `ISessionFileService`, same pattern as `TreeChanged`/`ScheduleChanged`
- [DONE] `TelegramBridgeService` subscribes on `Start()`: role-based prefixes (`USER:`, `⚙️`, none for assistant), skips empty `COMPACTION` entries, chunks over ~4096 chars on newline boundaries, echo-suppresses the `ROLE=USER` entry a `TurnSource.Telegram` turn appends for its own routed message

### P29.2 Bridge as a daemon client (FR.21.10)
- [DONE] `TelegramBridgeService` constructor swapped from `ISessionRegistry`/`Lazy<ISessionTurnService>`/`ISessionFileService` to `IDaemonClient` (`IDirectoryLabelService`/`IAppSettingsService`/`ITelegramClient` remain direct — stateless utilities outside the daemon domain)
- [DONE] `AppSettingsModel.TelegramMentionHandle`; `TelegramBridgeService.NotifyAsync` prefixes it onto outbound pings (`notify_user`, scheduled/orchestrated completion)
- [DONE] `CompositeNotificationService.ShowResult` posts a short completion ping instead of the full assistant text (mirror already streamed it)
- [DONE] Settings UI: mention-handle field alongside chat ID / allowed-user-IDs (FR.21.7)

### P29.3 Telegram-driven turn control (FR.21.11)
- [DONE] `/stop` inside a session's own topic calls `IDaemonClient.CancelTurn`

### P29.4 Tests
- [DONE] `SessionFileServiceTests`, `SessionRegistryTests`, `InProcDaemonClientTests` additions for the new event/lookup
- [DONE] `TelegramBridgeServiceTests` rewritten onto a fake `IDaemonClient`; new coverage for mirror prefixes/chunking/echo-suppression, mention-ping prefix, and `/stop`
- [ ] Manual E2E re-run against a real bot/supergroup (extends the Phase 28 checklist) — not yet exercised live

### P29.5 Duplicate entries in the session pane (fix)
Reported by the Architect against a Telegram-bridged session right after a machine restart: the user message and the assistant reply both appeared twice in the pane. Root cause was a pre-existing race between `SessionContentViewModel`'s two file watchers (`.txt` and JSONL), which only became load-bearing once daemon-orchestrated turns (this phase) kept both watchers armed for a turn's full duration — see `docs/issues/ISSUE.009.jsonl-watcher-txt-writeback-duplicate-entries.md` for the full writeup.
- [DONE] `SessionContentViewModel.RefreshFromJsonl` now writes new entries to the `.txt` file inside the same `Dispatcher.UIThread.Post` callback that adds them to `Messages`, after the `Messages.Add` loop — closing the async gap the `.txt` watcher's own debounced refresh could race
- [DONE] `docs/issues/ISSUE.009.jsonl-watcher-txt-writeback-duplicate-entries.md` — investigation/resolution writeup
- [ ] Manual re-verification against a live Telegram-bridged session under load (e.g. right after a restart) — not yet exercised live post-fix

**Follow-up (2026-09-14):** first fix only closed the *display*-only variant. A second, distinct race duplicated an assistant reply for real on disk in a Streaming Session Mode (FR.19) session ("VPN test stand") sent through the normal UI — unrelated to Telegram. Root cause: `TurnController`'s pre-`IsBusy=false` JSONL-entry-count snapshot can go stale when a persistent process's transcript JSONL flush lands after the snapshot but before the "turn complete" signal, letting the JSONL watcher replay the last entry. See the "Follow-up (2026-09-14)" section of `docs/issues/ISSUE.009...md`.
- [DONE] `RefreshFromJsonl` now skips any candidate entry matching the current tail of `Messages` (role + content), checked live inside the `Dispatcher.UIThread.Post` callback
- [DONE] `RefreshFromFile` restructured so its "is this new" decision also runs inside the post, against `Messages.Count` read at execution time, closing the same class of race for symmetry
- [ ] Manual re-verification against a live Streaming Session Mode turn — not yet exercised live post-fix

---

## Phase 30 — Streaming/Headless Context Divergence Fix (FR.19.13)

Confirmed live bug (reported by the Architect against the "LoRA training of LLM" session, 2026-08-26): a headless turn (Telegram/scheduled/orchestrated) against a session with an alive Streaming Session Mode process used to spawn a competing one-shot `--resume` process, silently diverging from the persistent process's own in-memory context — see `docs/issues/ISSUE.006.streaming-headless-context-divergence.md` for the full incident writeup.

- [DONE] `IClaudeStreamingSessionManager.SendTurnAsync` gains an optional, additive `onTurnEvent` observer param — delivered alongside (not instead of) the process's existing primary `onEvent` callback, so a second caller can observe a turn's outcome without disturbing the UI's own file/UI writes (`ClaudeStreamingSessionManager`'s `Handle.TurnObserver`)
- [DONE] `SessionTurnService.RunTurnAsync` checks `IClaudeStreamingSessionManager.IsAlive(nodeId)` before the one-shot path; when alive, routes via the new `RunTurnViaStreamingAsync` (sends through the persistent process, observes events only to build `TurnResultModel`, does not append assistant/system content itself)
- [DONE] `InstructionOptionsModel`/`SessionTurnService` doc comments updated to match (`IsStreamingMode: true` on the redirected path)
- [DONE] `docs/requirements.md` FR.19.11 rewritten (no longer describes headless-vs-streaming as an unsolved limitation), new FR.19.13 added; `docs/glossary.md` "Persistent Session Process" updated + new "Streaming Redirect" term
- [DONE] `docs/issues/ISSUE.006.streaming-headless-context-divergence.md` — full investigation/resolution writeup (first entry in `/docs/issues/`)
- [DONE] `SessionTurnServiceTests.cs` (new) — one-shot fallback baseline + streaming-redirect coverage (routes correctly, does not double-append assistant content, propagates errors/tokens, sends the augmented prompt, running-state set/unset)
- [ ] Manual re-verification against the actual "LoRA training of LLM" session (or any Streaming Session Mode session) — send a Telegram message while a desktop tab is open on the same session, confirm the next desktop-typed turn has full context — not yet exercised live post-fix

---

## Phase 31 — Local Tool Profile (FR.12.21, FR.12.22)

Curates the native tool + MCP surface handed to the claude CLI for local (Ollama) model turns, since weak/quantized/abliterated local models mis-call tools out of the full ~30-tool surface even though the Ollama-served Anthropic `/v1/messages` protocol itself correctly handles `tools`/`tool_use`. See `docs/business-analysis/Local-Models_Agentic-Coding-Findings.md` for the empirical findings behind this phase.

### P31.1 Data model & persistence (FR.12.21)
- [DONE] `Models/LocalToolProfileModel.cs` — `List<string> NativeTools`, `bool IncludeCmxMcp`, `List<string> IncludedExternalMcpServerIds`
- [DONE] `Models/DirectoryNodeModel.cs` — add nullable `LocalToolProfile` property; `null` = default lean profile (backward-compatible, no migration needed)
- [DONE] `Constants.cs` — `Constants.LocalTools` with `DefaultNativeTools` (`Read`/`Write`/`Edit`/`Bash`/`Glob`/`Grep`/`LS`) + full selectable `Catalog` (core + `WebFetch`/`WebSearch`/`NotebookEdit`/`TodoWrite`/`Task`)

### P31.2 CLI argument/config wiring (FR.12.21)
- [DONE] `Services/LocalToolProfileResolver.cs` — single source of truth: `DirectoryNodeModel.LocalToolProfile` (or default lean) → `LocalToolProfileResolution`, only when the session's model is `ModelProvider.Ollama` and `SupportsTools = true` (returns `null` otherwise). Shared by headless + interactive paths.
- [DONE] `ClaudeProcessManager.BuildArguments` (+ `StreamingSpawnParams`/`ClaudeStreamingSessionManager`) — append `--tools "<space-separated names>"` (or `""` for empty) per the resolved `LocalToolSpec`
- [DONE] `--mcp-config` build for local turns — `AgentMcpServer.EnsureConfigFileAsync` overload filters to `cmx` (if `IncludeCmxMcp`) + matching `IncludedExternalMcpServerIds`; append `--strict-mcp-config`; omit `--mcp-config` when nothing selected
- [DONE] Tool-incapable local models (`SupportsTools = false`) continue to get `--tools ""` and no `--mcp-config` regardless of any saved profile (resolver returns `null`; existing `disableTools` path unchanged)
- [DONE] Anthropic-routed sessions unaffected — resolver returns `null`, no `--tools`/`--strict-mcp-config` emitted (covered by a regression test)

### P31.3 Command bar UI (FR.12.22)
- [DONE] Model/Profile/Effort selector boxes fixed to 120px via `ComboBox.cmxCompact`; dropdown flyouts keep natural width (template-part `Width` overrides on `PART_Popup`/`PART_ItemsPresenter`)
- [DONE] `Converters/LeftEllipsisConverter.cs` — left-truncating converter wired to the Model box via `SelectionBoxItemTemplate` (right-aligned; `ConverterParameter=8`)
- [DONE] Local Tool Profile selector: `ToolProfileToggle` toggle-label + `Popup` checkbox list (native tools + `cmx` + external MCP servers), matching the existing `ProfileActionsToggle`/`Popup` pattern — visible only when the directory's selected model is local; disabled w/ tooltip when tool-incapable

### P31.4 Tests
- [DONE] `LocalToolProfileResolverTests.cs` (7) + `ClaudeProcessManagerBuildArgumentsTests.cs` (6) — resolver decision (Anthropic/tool-incapable/null → `null`, default-lean, custom echo, empty selection) and `--tools`/`--strict-mcp-config` arg assembly (`BuildArguments` made `internal` + `InternalsVisibleTo`). Full suite: 362/362 pass.
- [DONE] `AgentMcpServerLocalConfigTests.cs` (5) — filtered `--mcp-config` JSON via the extracted `internal AgentMcpServer.BuildMcpConfigJson`: subset of external ids, `cmx` present w/ token header, `cmx`-only, `null`=all registry servers, empty=empty map.
- See `docs/testcases/TC.LC.01-Local-Tool-Profile.md` for the full integration test list.

### P31.5 Docs
- [DONE] `docs/requirements.md` — FR.12.21, FR.12.22; FR.12.14 bare-host bug fix
- [DONE] `docs/glossary.md` — Local Tool Profile entry; ANTHROPIC_BASE_URL/Local Model/Model Provider updated
- [DONE] `docs/usecases/UC.LC.01-Local-Tool-Profile.md`, `docs/testcases/TC.LC.01-Local-Tool-Profile.md`
- [DONE] `docs/business-analysis/Local-Models_Agentic-Coding-Findings.md`

### P31.6 — Local Tools master switch + command-bar refinements (FR.12.22 amendment, FR.12.23)
- [DONE] Model/Profile/Effort `.cmxCompact` local `Width` overrides changed to Model ~120px / Profile ~100px / Effort ~60px (dropdown flyouts still open at natural width)
- [DONE] `:latest` stripped from the Model box's *displayed* text only (closed-box label + open dropdown list) — `ModelDisplayText` helper + `OllamaLatestSuffixConverter` (list) + `LeftEllipsisConverter` extended to strip-then-truncate (closed box); underlying `SelectedModelId` string, `--model` flag, and `LocalToolProfileResolver` matching untouched
- [DONE] `LocalToolProfileModel.Enabled` (bool, default `true`, backward-compatible deserialization for profiles saved before this field existed)
- [DONE] `LocalToolProfileResolver.Resolve` — returns `null` when `Enabled == false`, regardless of saved `NativeTools`/`IncludeCmxMcp`/`IncludedExternalMcpServerIds`; saved selection left untouched for when re-enabled; covered by a new resolver test
- [DONE] Relocated the Local Tool Profile control out of the selector row to a 26×26 icon-style button (🔧) next to the Session Completion Sound button (FR.22); visible only for local models, disabled (existing tooltip) when the model doesn't support tools
- [DONE] Short-click vs press-and-hold (~450ms `DispatcherTimer`) gesture on the relocated button, implemented via manual `PointerPressed`/`PointerReleased`/`PointerCaptureLost` handlers (plain `Button`, not `ToggleButton`): short click toggles+persists `Enabled`; hold opens the native-tools/`cmx`/external-MCP checkbox popup (`IsOpen` bound `TwoWay`, `IsLightDismissEnabled=True`) without touching `Enabled`; clicking away or clicking the button again while open just closes the popup; checkbox/label clicks inside the popup toggle+persist that tool immediately and keep the popup open. Dimmed via a bound `Classes.toolsOff` + `Button.toolsOff` style when the master switch is off. Verified: build 0 errors, full suite 368/368 (was 367, +1 for the `Enabled=false` bypass test). Not run in the live app — hold-timing feel and dim opacity worth a manual check.
- [ ] Dimmed-opacity visual state when `Enabled` is false, full opacity when true (FR.11.9 convention)
- [ ] Resolver test: `LocalToolProfileResolver_Resolve_EnabledFalse_ReturnsNullRegardlessOfSavedSelection`

### Bugfix — Concurrent `appsettings.json` Save lost updates (ISSUE.008)
- [DONE] Root cause: `AppSettingsService.Save()` was a completely unsynchronized full-graph serialize + atomic rename; concurrent Saves from multiple threads (orchestration workers, Streaming Session Mode, per-turn Saves) could race, with a stale-snapshot Save landing last and silently overwriting a newer one (e.g. one that had just added an orchestrated sub-agent session node) — confirmed cause of orchestrated worker sessions vanishing from the tree (NR.3)
- [DONE] Fix: `IAppSettingsService.SyncRoot` (backed by `AppSettingsService`'s private `_gate`) now guards the full bodies of `Save()`/`Load()`; `SessionRegistry`'s tree-mutation methods now take that same lock (its own private `_lock` removed — reentrant `lock`, no deadlock) instead of a separate one, so a registry mutation and its Save can never be split by a concurrent Save from another thread
- [DONE] `AppSettingsServiceConcurrencyTests.cs` (3 tests) — many-threads lost-update repro (mutate+Save under `SyncRoot`, fresh `Load()` confirms every node survived), concurrent mutate-vs-redundant-Save (no `InvalidOperationException`), and bare unsynchronized `Save()` calls from multiple threads with no caller-side lock (mirrors `SessionTurnService`/UI-toggle call sites) — manually verified this last test fails (reproducible `IOException` on the shared `.tmp` path) with the lock removed from `Save()`, passes with it restored. Full suite: 371/371 pass.
- [DONE] `docs/requirements.md` NR.3 amended; `docs/issues/ISSUE.008.concurrent-settings-save-lost-update.md` written
- Known residual (not addressed this round): a background Save is only serialized against a *UI-thread* mutation if that UI code path also takes `SyncRoot` — most UI toggles currently call `Save()` bare (now safe against torn writes/lost Saves thanks to the internal lock) but don't wrap their own field mutation in the lock. Not the data-loss path that caused this issue (that was registry-mutation-vs-Save, now fully closed); flagged as a minor follow-up.

---

## Backlog / Future

- [ ] **Visual Studio / Roslyn MCP server (planned after Phase 26)** — standalone Roslyn (`MSBuildWorkspace` or the published `roslyn-language-server`) core for rename/find-references/go-to-def/call-hierarchy/symbol-search, no running VS required; optional thin VSIX bridge (named-pipe) only for unsaved editor buffers + Build/Error List when VS happens to be running. Registered as a `Stdio` entry in the FR.20 registry once built. Research written to `docs/business-analysis/RESEARCH.01.VS-MCP-Server-Automation-Approaches.md`. Requirements not yet drafted — needs Architect sign-off before an FR group is added.
- [ ] **Claude-CLI-free agent loop (planned after the VS/Roslyn server)** — in-process C# agentic loop (Microsoft.Extensions.AI `IChatClient`/`FunctionInvokingChatClient` + OllamaSharp + official Anthropic SDK + official MCP C# SDK as an MCP client) replacing the `claude` subprocess for Ollama (and optionally direct-Anthropic) turns; consumes the same FR.20 registry + token store instead of re-implementing OAuth. Requirements not yet drafted — needs Architect sign-off before an FR group is added.

- [ ] **Investigate whether Claude Code's own context compaction silently drops the once-per-fresh-process instruction steering** — `InstructionOptionsModel.IsNewSession` currently gates `NativeSchedulingRedirect`/`NoWorkflowTool` on `ClaudeSessionId == null`, so within a *resumed* (never-detached) session the redirects are sent once and never again; if Claude Code internally compacts context mid-session, the steering may fall out of context and never return, even though `ClaudeSessionId` stays non-null throughout. We already parse `system`/`compact` stream events (`TurnController.HandleStreamEvent`, `SessionTurnService.HandleEvent`) — a fix, if the hypothesis is confirmed, would re-inject the redirects on the next turn after a `compact` event. Raised while investigating unreliable native-scheduling steering; not yet confirmed or scheduled.

- [ ] **P2.3 Search unit tests** — match / no-match / ancestor expansion
- [ ] Session file watcher (detect disk deletion so tree delete button becomes available)
- [ ] Integration tests for `ClaudeProcessManager` with a mock process
- [ ] **Touchscreen UX review** — Review all UI interactions for touchscreen compatibility, plan touch-friendly gestures (long-press for context menu, swipe for move, tap-and-hold for drag), review hit target sizes, consider touch-specific affordances
