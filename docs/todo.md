# ClaudeMaximus — TODO

## Phase 1 — Shell & Tree (no Claude process)

### P1.1 Solution scaffold
- [ ] Create `ClaudeMaximus.sln` and project structure under `code/`
- [ ] Create `ClaudeMaximus` Avalonia app project (MVVM, ReactiveUI)
- [ ] Create `ClaudeMaximus.Tests` xUnit test project
- [ ] Add project references and NuGet packages

### P1.2 Configuration & persistence (FR.4)
- [ ] Define `AppSettings` model (tree, window state, settings values)
- [ ] Define tree node models: `DirectoryNodeModel`, `GroupNodeModel`, `SessionNodeModel`
- [ ] Implement `IAppSettingsService` + `AppSettingsService` (load/save `appsettings.json` atomically)
- [ ] Unit tests for load/save round-trip and atomic write

### P1.3 Session tree UI (FR.1)
- [ ] `SessionTreeViewModel` with observable collection of tree roots
- [ ] `DirectoryNodeViewModel`, `GroupNodeViewModel`, `SessionNodeViewModel`
- [ ] Tree panel with search box above (FR.1.9)
- [ ] Add Directory node (folder picker) — FR.1.5
- [ ] Add Group node (inline name entry) — FR.1.5
- [ ] Add Session node (inline name entry, creates session file) — FR.1.5, FR.3
- [ ] Rename Group / Session node (inline edit) — FR.1.6
- [ ] Delete rules enforced in UI — FR.1.7
- [ ] Running session indicator placeholder — FR.1.8
- [ ] git-root label resolution for Directory nodes — FR.1.3
- [ ] Unit tests: add/rename/delete rules, git-root label logic

### P1.4 Application shell (FR.6)
- [ ] Two-panel `MainWindow` layout with resizable splitter
- [ ] Persist/restore window state via `AppSettingsService` — FR.4.5
- [ ] Settings window with folder picker + claude path field — FR.4.4
- [ ] Empty right-panel placeholder when no session selected

---

## Phase 2 — Session Storage & Display (FR.2, FR.3)

### P2.1 Session file service
- [ ] `ISessionFileService` + `SessionFileService`
  - Create session file with timestamped random name (FR.3.2)
  - Append message entry (FR.3.3, FR.3.5)
  - Append compaction separator (FR.3.4)
  - Read all entries from file
- [ ] Unit tests for entry serialisation/deserialisation round-trip

### P2.2 Session view
- [ ] `SessionViewModel` — loads entries from file, exposes observable list
- [ ] Message rendering: USER / ASSISTANT / SYSTEM / COMPACTION types (FR.2.3)
- [ ] Compaction separator displayed inline; full pre-compaction history above (FR.3.4)
- [ ] Multi-line input box, Ctrl+Enter / Send button (FR.2.4)
- [ ] Busy state disables input (FR.2.5)
- [ ] Session name header (FR.2.2)

### P2.3 Search (FR.1.9)
- [ ] `ISessionSearchService` + `SessionSearchService` (linear file scan)
- [ ] Wire search box to tree filter
- [ ] Unit tests: match / no-match / ancestor expansion logic

---

## Phase 3 — Claude Code Process Integration (FR.5)

### P3.1 Process management
- [ ] `IClaudeProcessManager` + `ClaudeProcessManager`
  - Launch `claude --output-format stream-json` with correct working directory
  - Write user input to stdin
  - Parse stream-json events from stdout
  - Handle unexpected exit + restart — FR.5.4
- [ ] Integration tests with a mock process (fake stdout stream)

### P3.2 Wire process to session view & storage
- [ ] On Send: write to process stdin, disable input
- [ ] On stream-json event: append to session file, append to `SessionViewModel` message list
- [ ] On compaction event: append `[COMPACTION]` separator
- [ ] Running indicator on Session node in tree (FR.1.8)
- [ ] Background sessions continue when switching views (FR.5.5)

---

## Backlog / Future

- [ ] Search indexing (replace linear scan)
- [ ] Session file watcher (detect external file deletion for FR.1.7)
- [ ] Light/dark theme toggle
