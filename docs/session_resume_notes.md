# Session Resume Notes

## Project Status

ClaudeMaximus is a working Avalonia desktop app that wraps the Claude Code CLI.
Phases 1-5 are functionally complete and building clean with 13 passing tests.
The app features full session state persistence, theme customization, and a
redesigned title bar with hamburger menu, tree panel toggle, and theme switch.

---

## What Is Implemented

### Solution layout
```
code/
  ClaudeMaximus.sln
  NuGet.config                  <- locks to nuget.org only
  ClaudeMaximus/
    Constants.cs                <- all magic strings/numbers
    App.axaml / App.axaml.cs   <- DI container + ThemeApplicator.Apply on startup
    Program.cs                  <- UseReactiveUI()
    Models/
      AppSettingsModel.cs       <- root JSON model (tree, settings, window, theme, colors, active session)
      DirectoryNodeModel.cs     <- top-level tree node (IsExpanded persisted)
      GroupNodeModel.cs         <- virtual group node (IsExpanded persisted)
      SessionNodeModel.cs       <- session leaf (ScrollOffset persisted)
      WindowStateModel.cs       <- width/height/position/splitter
      ThemeColorsModel.cs       <- per-theme color settings (input, bubble, code, system)
      SessionEntryModel.cs      <- one parsed line from a session file
      ClaudeStreamEvent.cs      <- parsed event from claude --output-format stream-json
    Services/
      IAppSettingsService / AppSettingsService      <- atomic JSON load/save
      IDirectoryLabelService / DirectoryLabelService <- git-root label resolution
      ISessionFileService / SessionFileService       <- create/read/append session text files
      IClaudeProcessManager / ClaudeProcessManager  <- spawn claude per message, parse stdout
      ThemeApplicator.cs        <- applies theme variant + custom color brushes to app resources
    ViewModels/
      MainWindowViewModel.cs     <- ActiveSession, splitter, tree toggle, theme toggle, settings
      SessionTreeViewModel.cs    <- Directories collection, Add/Rename/Delete, search
      DirectoryNodeViewModel.cs  <- Label, Children, IsExpanded (two-way bound)
      GroupNodeViewModel.cs      <- Name, Children, IsExpanded (two-way bound)
      SessionNodeViewModel.cs    <- Name, FileName, IsRunning, IsResumable
      SessionViewModel.cs        <- Messages, SendCommand, ScrollOffset, font sizes
      SettingsViewModel.cs       <- SessionFilesRoot, ClaudePath, Theme, all color fields
    Views/
      MainWindow.axaml/.cs       <- hamburger menu, chevron toggle, theme icon, two-panel layout
      SessionTreeView.axaml/.cs  <- tree with IsExpanded/IsVisible bindings + context menus
      SessionView.axaml/.cs      <- message list with theme-aware colors + scroll persistence
      SettingsWindow.axaml/.cs   <- folder picker, claude path, theme radio, color hex inputs
      MarkdownView.cs            <- uses ThemeApplicator resource keys for code colors
  ClaudeMaximus.Tests/
    Services/
      AppSettingsServiceTests.cs
      DirectoryLabelServiceTests.cs
      SessionFileServiceTests.cs
```

### State persistence (Phase 5)
- **Active session**: `ActiveSessionFileName` in appsettings.json, restored on startup
- **Tree expand/collapse**: `IsExpanded` on DirectoryNodeModel/GroupNodeModel, bound two-way via `ReflectionBinding`
- **Scroll position**: `ScrollOffset` on SessionNodeModel, saved on session switch and window close
- **Tree panel collapsed**: `IsTreePanelCollapsed` in appsettings.json

### Title bar
- Chevron left/right: toggles tree panel visibility (persisted)
- Hamburger icon: flyout menu with Settings and Exit
- Sun/moon icon: toggles dark/light theme (persisted)

### Theme system
- `ThemeApplicator` sets `RequestedThemeVariant` + injects custom `SolidColorBrush` resources
- Resources: `CmxInputBg`, `CmxInputFg`, `CmxUserBubbleBg`, `CmxUserBubbleFg`, `CmxCodeBg`, `CmxCodeFg`, `CmxInlineCodeBg`, `CmxInlineCodeFg`, `CmxSystemBubbleBg`
- Settings window: Dark/Light radio + 9 hex color inputs per theme
- `ThemeColorsModel` stores per-theme colors; `AppSettingsModel.LightColors` + `DarkColors`

---

## Known Gotchas

### Avalonia compiled bindings in DataTemplates
`x:DataType` on a `DataTemplate`/`TreeDataTemplate` inside a control's `DataTemplates`
collection does NOT correctly scope compiled bindings. **Fix:** `x:CompileBindings="False"`.

### git-root label for directory picker paths
`Path.GetFileName` returns `""` for trailing separator paths. **Fix:** `TrimSeparators()`.

### TreeViewItem IsExpanded binding
Uses `ReflectionBinding` (not compiled) because TreeViewItem.IsExpanded is on the container,
not the data template content. SessionNodeViewModel has no IsExpanded (leaf nodes).

---

## Architecture Decisions to Preserve

1. **One file = one type** — enforced across the whole codebase
2. **No SQLite** — all session data is plain text; `appsettings.json` holds all tree metadata
3. **Atomic settings write** — write to `.tmp` then rename
4. **Immediate flush** — every session file append calls `stream.Flush(flushToDisk: true)`
5. **Per-message process spawn** — `--resume <session_id>` provides continuity
6. **WorkingDirectory stored at creation** — avoids tree traversal at process-launch time
7. **`x:CompileBindings="False"` on tree DataTemplates** — do not remove
8. **ThemeApplicator is static** — utility class, applies theme globally

---

## Backlog / Future

- P2.3 Search unit tests
- Session file watcher (detect disk deletion)
- Integration tests for ClaudeProcessManager
