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

### P2.3 Search (FR.1.9) ← NEXT
- [ ] `ISessionSearchService` + `SessionSearchService` — linear scan over session `.txt` files
- [ ] Wire `SessionTreeViewModel.SearchText` changes to filter tree
- [ ] Sessions not matching query hidden; ancestor nodes of matches expanded
- [ ] Unit tests: match / no-match / ancestor expansion

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

## Backlog / Future

- [ ] **P2.3 Search** — see above, the only item from original scope not yet done
- [ ] Session file watcher (detect disk deletion so tree delete button becomes available)
- [ ] Integration tests for `ClaudeProcessManager` with a mock process
- [ ] Search indexing (replace linear scan when session count grows)
- [ ] Light/dark theme toggle
