# Session Resume Notes

## Project Status

ClaudeMaximus is a working Avalonia desktop app that wraps the Claude Code CLI.
Phases 1, 2 and 3 are functionally complete and building clean with 13 passing tests.
The app can be launched from `code/ClaudeMaximus/`. One remaining feature from the
original scope is **search** (P2.3). Everything else is implemented.

---

## What Is Implemented

### Solution layout
```
code/
  ClaudeMaximus.sln
  NuGet.config                  ← locks to nuget.org only (avoids private Datum feed)
  ClaudeMaximus/
    Constants.cs                ← all magic strings/numbers
    App.axaml / App.axaml.cs   ← DI container wired here (ConfigureServices)
    Program.cs                  ← UseReactiveUI()
    Models/
      AppSettingsModel.cs       ← root JSON model for appsettings.json
      DirectoryNodeModel.cs     ← top-level tree node (filesystem dir)
      GroupNodeModel.cs         ← virtual group node (WorkingDirectory field)
      SessionNodeModel.cs       ← session leaf (FileName, WorkingDirectory, ClaudeSessionId)
      WindowStateModel.cs       ← width/height/position/splitter
      SessionEntryModel.cs      ← one parsed line from a session file
      ClaudeStreamEvent.cs      ← parsed event from claude --output-format stream-json
    Services/
      IAppSettingsService / AppSettingsService      ← atomic JSON load/save
      IDirectoryLabelService / DirectoryLabelService ← git-root label resolution
      ISessionFileService / SessionFileService       ← create/read/append session text files
      IClaudeProcessManager / ClaudeProcessManager  ← spawn claude per message, parse stdout
    ViewModels/
      ViewModelBase.cs           ← extends ReactiveObject
      MainWindowViewModel.cs     ← ActiveSession, splitter, OpenSettings, Exit
      SessionTreeViewModel.cs    ← Directories collection, Add/Rename/Delete operations
      DirectoryNodeViewModel.cs  ← Label (git-root derived), Children collection
      GroupNodeViewModel.cs      ← Name (reactive), Children, WorkingDirectory
      SessionNodeViewModel.cs    ← Name (reactive), FileName, IsRunning
      SessionViewModel.cs        ← Messages list, SendCommand, LoadFromFile(), process wiring
      MessageEntryViewModel.cs   ← Role, Content, Timestamp, IsUser/IsAssistant/etc booleans
      SettingsViewModel.cs       ← SessionFilesRoot, ClaudePath, SaveCommand
    Views/
      MainWindow.axaml/.cs       ← two-panel layout; wires ActiveSession → SessionView.DataContext
      SessionTreeView.axaml/.cs  ← tree + search box + folder picker + context menus
      SessionView.axaml/.cs      ← message list + input box + auto-scroll
      SettingsWindow.axaml/.cs   ← folder picker + claude path
      InputDialog.axaml/.cs      ← reusable single-input modal dialog
  ClaudeMaximus.Tests/
    Services/
      AppSettingsServiceTests.cs
      DirectoryLabelServiceTests.cs
      SessionFileServiceTests.cs
```

### appsettings.json location
`%APPDATA%\ClaudeMaximus\appsettings.json` (Windows)
`~/.config/ClaudeMaximus/appsettings.json` (Linux/macOS via Environment.SpecialFolder.ApplicationData)

### Session files location
Configured in Settings window. Default: `%APPDATA%\ClaudeMaximus\sessions\`
Named: `YYYY-MM-dd-HHmm-xxxxxx.txt` (6 random lowercase alpha chars)

### Session file format
```
[2026-03-12T14:30:00Z] USER
message content here

[2026-03-12T14:30:05Z] ASSISTANT
response content here

[2026-03-12T14:30:10Z] COMPACTION

[2026-03-12T14:30:15Z] ASSISTANT
content after compaction
```

### Claude process integration
- Per-message process spawn: `claude --output-format stream-json [--resume <session_id>]`
- User message written to stdin; stdin closed (EOF triggers processing)
- stdout parsed as JSONL: handles `assistant`, `system`, `result` event types
- `session_id` captured from first `result` event → stored in `SessionNodeModel.ClaudeSessionId` → saved to `appsettings.json` → used for `--resume` on all subsequent messages
- Process errors (bad path, non-zero exit) surface as SYSTEM messages in the session view
- `claude` path defaults to `"claude"` (resolved from PATH); override in Settings

---

## Known Gotchas

### Avalonia compiled bindings in DataTemplates
`x:DataType` on a `DataTemplate`/`TreeDataTemplate` inside a control's `DataTemplates`
collection does NOT correctly scope compiled bindings — Avalonia resolves bindings against
the outer control's `x:DataType` instead. **Fix already applied:** all three tree item
templates in `SessionTreeView.axaml` have `x:CompileBindings="False"`.
See `/docs/shell_commands.md` for the note.

### git-root label for directory picker paths
`Path.GetFileName` returns `""` for paths with a trailing directory separator (e.g.
`C:\datum_api\`) — which is exactly what the Windows folder picker returns.
**Fix already applied:** `DirectoryLabelService.TrimSeparators()` strips trailing
separators before any `GetFileName` call.

### Delete behaviour
- Sessions: only deletable from the tree if the session FILE is already gone from disk
- Groups/Directories: only deletable if they have no children
- No cascading deletion — by design (FR.1.7)

### Working directory
Sessions inherit the working directory from their ancestor `DirectoryNodeViewModel.Path`.
This path is stored in `SessionNodeModel.WorkingDirectory` and `GroupNodeModel.WorkingDirectory`
at creation time so the tree doesn't need to be traversed when launching a process.

---

## What Remains

### P2.3 — Search (the only unfinished item from original scope)
- `ISessionSearchService` + `SessionSearchService`: linear full-text scan over all `.txt`
  files in the configured `SessionFilesRoot`
- The search box in `SessionTreeView` already binds to `SessionTreeViewModel.SearchText`
  (two-way). The filter logic needs to be wired up: when `SearchText` changes, run the
  scan and set a `IsVisible` flag (or filtered collection) on each `SessionNodeViewModel`
  so non-matching sessions are hidden and ancestor nodes of matches are expanded
- Search is intentionally a linear file scan for v1 (no indexing)

### Backlog / future
- Session file watcher: detect when a session file is deleted on disk so the tree
  delete option becomes available without restarting the app
- Search indexing to replace the linear scan
- Light/dark theme toggle

---

## Architecture Decisions to Preserve

1. **One file = one type** — enforced across the whole codebase (see code_standards.md)
2. **No SQLite** — all session data is plain text; `appsettings.json` holds all tree metadata
3. **Atomic settings write** — write to `.tmp` then rename; never write directly to `appsettings.json`
4. **Immediate flush** — every session file append calls `stream.Flush(flushToDisk: true)`
5. **Per-message process spawn** — simpler than keeping a persistent `claude` process alive;
   `--resume <session_id>` provides continuity across spawns
6. **WorkingDirectory stored at creation** — set on `GroupNodeModel` and `SessionNodeModel`
   when the node is created; avoids tree traversal at process-launch time
7. **`x:CompileBindings="False"` on tree DataTemplates** — do not remove; see gotchas above
