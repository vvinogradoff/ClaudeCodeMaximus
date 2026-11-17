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

---
