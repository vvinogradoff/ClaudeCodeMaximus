> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.4 — Application Configuration

**FR.4.1** All application configuration is stored in a single `appsettings.json` file in the platform application data directory (e.g., `%APPDATA%\TessynDesktop\appsettings.json` on Windows).

**FR.4.2 — Tree structure in appsettings.json:** The complete tree (Directory nodes with their paths, Group nodes with their names, Session nodes with their names and bound session file paths) is persisted in `appsettings.json`. Directory nodes are the roots; Group and Session nodes are nested within.

**FR.4.3 — appsettings.json is written atomically:** On every save, write to a temporary file in the same directory then rename over the target, to prevent corruption on crash.

**FR.4.4 — Settings window:** A Settings window (accessible from the hamburger menu) allows the user to configure:
- Session Files Root directory (folder picker)
- `claude` CLI executable path (default: resolved from `PATH`)
- Theme selection: Dark or Light (radio buttons)
- Per-theme color customization (see FR.9)

**FR.4.5** Application window state (size, position, left-panel splitter position, and maximized state) is also persisted in `appsettings.json` and restored on next launch. When the window is closed while maximized, the normal (restored) bounds are preserved so un-maximizing restores the previous size and position. The saved position is validated against connected screens on startup; if the saved center point is off-screen, the window is centered on the primary display.

**FR.4.6 — Session state persistence:** The following per-session and global UI state is persisted in `appsettings.json` and restored on next launch:
- **Active session selection:** the file name of the last selected session, so reopening the app returns to the same session.
- **Tree expand/collapse state:** each Directory and Group node stores an `IsExpanded` flag.
- **Scroll position:** each session stores a `ScrollOffset` value for the message output area, so returning to a session resumes at the same scroll position.
- **Tree panel visibility:** whether the left panel is collapsed or visible.

## Amendments and additions (batch 2026-11-18)

| Existing | Amended by | Change |
|---|---|---|
| FR.4.4 Settings window | FR.4.7 | Closing the window with unsaved changes asks first. |
| FR.4.4 Settings window | FR.4.8 | The General tab gains the **Code references** checkbox. |

**FR.4.7 — Ask before closing Settings with unsaved changes (WPF; decision A54).** When the Settings window is closed (the **Close** button, Esc, the title bar's close button or Alt+F4) while **any tab** has unsaved changes, including pending colours (FR.9.19), a dialog asks: **Save**, **Discard** or **Cancel**.
- **Save** saves every changed tab, as the window's Save button does, then closes. If a save fails, the window stays open and shows the error, as Save does.
- **Discard** drops the changes and closes; nothing is stored.
- **Cancel** keeps the window open with the changes as they were.
- **No changes:** the window closes at once, with no dialog.
- The dialog names the tabs with unsaved changes (for example `Unsaved changes in Colours, Appearance.`).
- Closing the main window, or the application exiting, while Settings is open behaves the same; Cancel then also stops the main window from closing.

**FR.4.8 — Code references checkbox in Settings > General (decision of the Architect, 2026-11-19).** The General tab of the Settings window shows a checkbox **Code references**, bound to the daemon setting `CodeReferencesEnabled` (default off; FR.7.13).
- A hint under the checkbox says what it does: when on, `#` in the message box suggests C# code symbols and the code index parses C# symbols; when off, `#` suggests sessions and no new symbol parsing is done; `##` files and drive paths work either way; the change applies at the next suggestion.
- It is a daemon setting: it is stored in `appsettings.json`, saved by the Save button with the other General settings (FR.4.7 applies: closing with the box changed and unsaved asks first), and read again by the daemon on each suggestion, with no restart.
- The control has the UI Automation name `Code references`.

---
