# Session-Tree_Directory-Label-derivation

*Backs [FR.1.3](/docs/requirements.md). Use case [UC.DL.01](/docs/usecases/UC.DL.01-Directory-Label.md), test cases [TC.DL.01](/docs/testcases/TC.DL.01-Directory-Label.md).*

## How the label is computed now

`DirectoryLabelService.GetLabel(path)` (`code/TessynDesktop.Core/Services/DirectoryLabelService.cs:18`) is pure string logic — no filesystem access, no git:

1. Split on `\` and `/`, dropping empty segments (trailing separators, UNC prefixes, forward slashes); no segments → return the input unchanged (`:20-22`).
2. Start at the last segment; while it is a well-known project folder, has a parent, and the parent is not a drive designator (`X:`), step to the parent (`:24-28`). Repeats, so `HardStop\Code\Docs` → `HardStop`.
3. A drive designator result gets a trailing `\` (`C:\` → `C:\`, `:31`).
4. The segment keeps its original casing.

The well-known list is `Constants.WellKnownFolders` (`code/TessynDesktop.Core/Constants.cs:367`: `Code`, `Docs`, `Data`; `All` at `:379`), matched case-insensitively (`DirectoryLabelService.cs:35`). It is not user-configurable.

## Why it changed

The previous rule walked up looking for `.git`: `.git` in the directory gave its name, `.git` in an ancestor gave `gitRoot\relative\path`, and **no `.git` gave the full absolute path**. Many working folders are not git repositories (e.g. `C:\Projects\oDesk\HardStop`, fanvue.one, TelegramAnalyzer), so their tree nodes and Telegram topics showed long full paths. The Architect asked for the last segment only, with the conventional `Code`/`Docs`/`Data` folders replaced by their parent's name (`HardStop\Code` → `HardStop`).

## Consumers

Everything goes through `IDirectoryLabelService` (registered at `code/TessynDesktop/App.axaml.cs:116`), so one change updated all of them:

- Session tree node: `DirectoryNodeViewModel.Label` (`code/TessynDesktop/ViewModels/DirectoryNodeViewModel.cs:18`).
- Tooltip: the same node shows the full path via `ToolTip.Tip="{Binding Path}"` (`code/TessynDesktop/Views/SessionTreeView.axaml:65`).
- Recent sessions dropdown (`RecentSessionsViewModel.cs:60`).
- Import pickers: directory target names (`ImportPickerWindow.axaml.cs:47`) and the "switch to original directory" target (`ImportPickerViewModel.SwitchToOriginalDirectory`, `ImportPickerViewModel.cs:198`, label at `:215`; called from `ImportPickerWindow.axaml.cs:89`).
- MCP `list_sessions`: `SessionSummaryModel.DirectoryLabel` populated in `SessionRegistry.cs:436`; session location label at `SessionRegistry.cs:466`.
- Telegram topic names: `TelegramBridgeService.BuildTopicName` (`TelegramBridgeService.cs:150`, `"{label} / {session name}"`), see [Telegram-Bridge_Topic-Name-Reconciliation](/docs/business-analysis/Telegram-Bridge_Topic-Name-Reconciliation.md).
- Telegram `/sessions` and `/open` replies use the summaries' `DirectoryLabel` (`TelegramBridgeService.cs:561`, `:583`).

## Gotchas

- **Labels can coincide.** `HardStop` and `HardStop\Code` both show `HardStop`. Directory-node duplicates are prevented by path (FR.1.5), never by label, so both nodes can exist side by side. Disambiguation is out of scope.
- **The tooltip is the only place the full path is visible** in the tree.
- **No persistence and no migration.** `DirectoryNodeModel` stores only `Path`; the label is recomputed on every display. Existing trees need no change. Only Telegram topic names persisted outside the app, hence the reconciliation run.
- `C:\Code` stays `Code` (parent is a drive designator); a lone `Code` segment without parent is kept.
