> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.6 — Application Shell

**FR.6.1** The main window uses a two-panel layout:
- Left panel: search box + session tree
- Right panel: active session view (or empty placeholder when no session selected)

**FR.6.2** The left panel width is adjustable via a draggable splitter.

**FR.6.3** The title bar replaces the traditional menu bar with a compact toolbar of icon buttons (left-to-right):
1. **Chevron toggle** — collapses/expands the left tree panel. Shows chevron-left when the tree is visible (click to hide), chevron-right when hidden (click to show). Collapse state is persisted in `appsettings.json`.
2. **Hamburger menu** — opens a flyout menu with Settings and Exit entries.
3. **Day/night toggle** — switches between dark and light themes. Shows a sun icon in dark mode (click for light), moon icon in light mode (click for dark). Theme choice is persisted in `appsettings.json`.

**FR.6.4** Window control buttons (minimize, maximize/restore, close) remain on the right side of the title bar. The title bar background is draggable for window repositioning.

---

---

## Amendments and additions (batch 2026-11-17)

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.6.3 Title bar toolbar | FR.6.7 | Chevron, hamburger and day/night stay. The session instruction toolbar (FR.11) now lives in the title bar too. The hamburger lists Import, Settings and Exit. |
| FR.6.4 Window controls and draggable title bar | FR.6.6 | Non-interactive text in the title bar (the version) is not focusable and does not stop a drag. |

### New requirements

**FR.6.5 — Application icon.** The window, the taskbar button, the tray icon (background host) and the executable use **one icon: the Tessyn Web app icon** (`code/Tessyn.Web/ClientApp/public/icon.svg`, rendered to the sizes Windows needs, 16 to 256 px). The old Avalonia logo is not used by the WPF client. The same artwork serves the web (`icon-192`, `icon-512`) and the desktop, so the product looks one across clients.

**FR.6.6 — Version text.** The version text is a plain label: **not focusable**, no tab stop, no focus rectangle, no selection. A press on it starts a window **drag**, as a press on the empty title bar does (FR.6.4); a double-click maximises or restores.

**FR.6.7 — Title bar contents (WPF).**
- Left to right: chevron toggle, hamburger, day/night, the **instruction toolbar** (icon buttons, each with its text as the tooltip: Auto-Commit cycling, New branch, Document, Compact, Compact now, Terminate; a one-line notice after them, full text in its tooltip), the **Recent dropdown** filling the middle, then the window controls. A 48 px gap right of the dropdown stays free for dragging.
- The **hamburger menu** lists, in order, the registered entries (Import, then Settings), a separator and Exit.
- The dropdown button shows the **open session's name** ("Recent" when none is open); its popup is as wide as the button.

**FR.6.8 — Status bar (WPF).** The two plan-usage meters (FR.18) fill the bar's full height and take all the width left by the other items. The Profiles button is at the far right.
