# TC.DL.01 — Short directory labels

Related requirement: [FR.1.3](/docs/requirements.md). Related use case: [UC.DL.01](/docs/usecases/UC.DL.01-Directory-Label.md).

The rule is pure string logic, so the unit tests need no filesystem. The tree itself (a WPF view) is verified manually.

## Integration tests (xUnit, `TessynDesktop.Tests`)

### TC.DL.01.1 — `DirectoryLabelServiceTests` (`GetLabel`) — FR.1.3
Each case asserts the exact returned label. Table rows 1–12 plus a few extras (`HardStop\Code\Data`, `C:\code\`, `C:`, `/home/u/proj`) are one theory: `GetLabel_PathVariants_ReturnsExpectedLabel`.

| Input | Expected |
|---|---|
| `C:\Projects\oDesk\HardStop` | `HardStop` |
| `C:\Projects\oDesk\HardStop\` (trailing separator) | `HardStop` |
| `C:\Projects\oDesk\HardStop\Code\` | `HardStop` |
| `C:\Projects\oDesk\HardStop\data` (lowercase) | `HardStop` |
| `C:\Projects\oDesk\HardStop\Code\Docs` (repeated stripping) | `HardStop` |
| `C:\Projects\EyeLabX\Code\EyeCura\` (well-known folder not last) | `EyeCura` |
| `C:\Projects\Personal\QuickBooksSQL\ContentManagementSystem\` | `ContentManagementSystem` |
| `C:\Code` (parent is a drive designator) | `Code` |
| `C:\` (drive root) | `C:\` |
| `\\server\share\Proj` (UNC) | `Proj` |
| `/home/u/proj/Code` (forward slashes) | `proj` |
| `""` (empty) | `""` |

Additional cases:
- **Git ignored:** `GetLabel_WhenGitRootIsTheDirectory_ReturnsDirectoryName` (temp dir with its own `.git`), `GetLabel_WhenGitRootIsAncestor_ReturnsOwnLastSegmentOnly` (`.git` in an ancestor: own last segment, not `gitRoot\relative\path`), `GetLabel_WhenNoGitRootAnywhere_ReturnsLastSegmentNotFullPath`.
- **Original casing kept:** `GetLabel_WhenWellKnownFolderCasingDiffers_MatchesCaseInsensitivelyAndKeepsParentCasing` (`C:\Proj\MyApp\CODE` → `MyApp`); `GetLabel_WhenLastSegmentHasMixedCasing_KeepsOriginalCasing` (`C:\Proj\mYapp` → `mYapp`).
- **Non-existent path:** `GetLabel_WhenPathDoesNotExist_StillReturnsLastSegment` (no filesystem access).
- **Callers:** a session whose working directory is `C:\Projects\oDesk\HardStop\Code` gets the Telegram topic `HardStop / <session name>`, covered by bridge tests using the real `DirectoryLabelService`: `EnsureTopicForSessionAsync_RealLabelService_NamesTopicWithShortDirectoryLabel` (topic created with the short name) and `Reconcile_RealLabelService_RenamesStoredFullPathNameToShortName` (an old full-path name is renamed to the short one); both in the `TelegramBridgeServiceTests` partial class. See also TC.21.3, TC.21.10.

## Manual UI verification

1. Open the tree with Directory nodes for e.g. `C:\Projects\oDesk\HardStop` and `C:\Projects\EyeLabX\Code\EyeCura`: the nodes show `HardStop` and `EyeCura`, not full paths (UC.DL.01.1).
2. Add a directory ending in `\Code` (e.g. `...\HardStop\Code`): its node shows `HardStop`, and it remains a separate node from `...\HardStop` (no duplicate-by-label merging).
3. Hover over a Directory node: the tooltip shows the full path (UC.DL.01.2).
4. Check the recent-sessions dropdown and the import picker show the same short labels, including the import picker's "switch to original directory" target (`ImportPickerViewModel.SwitchToOriginalDirectory`).
