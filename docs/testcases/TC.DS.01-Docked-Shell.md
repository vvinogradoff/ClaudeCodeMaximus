# TC.DS.01 — Docked Shell

Related requirements: FR.6.11–FR.6.22, FR.29.19, FR.23.12, FR.23.13, FR.12.29. Use cases: [UC.DS.01](/docs/usecases/UC.DS.01-Docked-Shell.md). Status: draft, not built.

**Legend.** AUTOMATED = unit / STA view test; E2E = WPF end-to-end through the test hook (NR.6, FR.6.22); MANUAL = done by hand (mouse drags of tabs, the look).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.DS.01 | First start: left pane Sessions/Recent/Tags (Sessions selected, 320 px), Session view alone in the centre with no tab row, Focus in the right pane at 35 % | E2E | UC.DS.01 | FR.6.11, FR.6.12 |
| TC.DS.02 | No panel has a caption bar; a single-panel pane shows one tab | MANUAL | UC.DS.01 | FR.6.13 |
| TC.DS.03 | Hook: dock Recent to the bottom, stack Tags on it as a tab; the state report matches (side, pane, tab index) | E2E | UC.DS.02 | FR.6.13 |
| TC.DS.04 | Drag a tab to another side, onto another pane's tab row, out to float; the drop guides appear | MANUAL | UC.DS.02 | FR.6.13 |
| TC.DS.05 | Right-click on a tab: Float, Dock, Auto-hide, Close; Close and Float are absent for the Session view | MANUAL | UC.DS.02 | FR.6.13, FR.6.14 |
| TC.DS.06 | The Session view cannot be closed or floated (hook calls are refused, state unchanged); it can be tabbed | E2E | UC.DS.02 | FR.6.14 |
| TC.DS.07 | Hook: float Focus; it is owned by the main window, not in the taskbar, never activated; the transcript widens; Send answers still works | E2E | UC.DS.03 | FR.6.14, FR.6.22 |
| TC.DS.08 | Drag Focus to the second monitor; it minimizes with the main window; hidden while the screensaver covers the window | MANUAL | UC.DS.03 | FR.6.14 |
| TC.DS.09 | A floating panel's monitor is removed (`--test-monitors`): it is clamped and the forced move is not saved | E2E | UC.DS.03 | FR.6.18, FR.29.16 |
| TC.DS.10 | Chevron: auto-hides the pane holding Sessions, icon flips, a second click restores it; state remembered after a restart; disabled while Sessions is closed or floating | E2E | UC.DS.04 | FR.6.15, FR.6.18 |
| TC.DS.11 | Slide-out of the auto-hidden left pane closes on Esc, on its edge tab and on its collapse button; it is drawn above a WebView2 | MANUAL | UC.DS.04 | FR.6.13 |
| TC.DS.12 | Hook: close Sessions; the Panels group shows it unchecked; picking it shows it at its last place; picking an open panel brings it forward | E2E | UC.DS.05 | FR.6.14 |
| TC.DS.13 | The Focus button with the badge shows a closed Focus panel, slides out an auto-hidden one, selects a tabbed one; the badge on the button and the Focus tab shows count and colour while the count is > 0 | E2E | UC.DS.06 | FR.6.16, FR.23.13 |
| TC.DS.14 | Hook: tab Focus into the session area: a tab row appears, Focus fills the area; move it back: the tab row disappears | E2E | UC.DS.06 | FR.6.16, FR.29.19 |
| TC.DS.15 | Focus content unchanged in the panel: cards, reply boxes, Send answers (n) at the top, Ctrl+Enter, Closed and Decisions, drafts; follows the session shown | E2E | UC.DS.06 | FR.6.16, FR.23.21, FR.23.22 |
| TC.DS.16 | Composer belongs to the Session view: text, collapse state and height survive every panel move | E2E | UC.DS.06 | FR.6.17, FR.12.36 |
| TC.DS.17 | Restart with a `shellLayout` in the test data root restores panes, sides, sizes, tab order, selected tabs, auto-hide, floating rectangles and closed panels; a layout saved 500 ms after the last change | E2E | UC.DS.07 | FR.6.18 |
| TC.DS.18 | An unreadable or newer-version `shellLayout` is ignored: the default layout shows and the log names the reason; an unknown panel id is skipped | AUTOMATED | UC.DS.07 | FR.6.18 |
| TC.DS.19 | Reset layout asks first; confirming restores the defaults and keeps the session, the composer text and open preview panels; cancelling changes nothing | E2E | UC.DS.08 | FR.6.19 |
| TC.DS.20 | Update from the old settings: tree width 400 px becomes the left pane width; a collapsed tree becomes an auto-hidden left pane; the old values are kept | AUTOMATED, E2E (the collapsed case) | UC.DS.09 | FR.6.18 |
| TC.DS.21 | A shell shortcut works with the focus in the composer, the Sessions search box, a Focus reply box and a floating panel | E2E | UC.DS.10 | FR.6.20 |
| TC.DS.22 | Docking, floating, closing and auto-hiding a panel while the composer has the focus leave the focus in the composer; a click into the panel moves it | E2E | UC.DS.10 | FR.6.20 |
| TC.DS.23 | Day/night recolours tabs, borders, splitters, edge tabs, the slide-out and floating frames at once; no WebView2 reloads | MANUAL | UC.DS.11 | FR.6.21 |
| TC.DS.24 | Theme brushes: the docking resources resolve to Tessyn's tokens in both themes and change on a theme switch (resource check) | AUTOMATED | UC.DS.11 | FR.6.21 |
| TC.DS.25 | Under `--test-window` nothing is read from or saved to the user's data; the hook's state report covers every panel | E2E | UC.DS.12 | FR.6.22 |
| TC.DS.26 | A preview panel opens the first time as a tab in the pane holding Focus; with Focus closed or floating, docked right at 40 % | E2E | UC.DS.01 | FR.6.12, FR.29.18, FR.29.21 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.6.11 | TC.DS.01 |
| FR.6.12 | TC.DS.01, 26 |
| FR.6.13 | TC.DS.02–05, 11 |
| FR.6.14 | TC.DS.05–08, 12 |
| FR.6.15 | TC.DS.10 |
| FR.6.16 | TC.DS.13–15 |
| FR.6.17 | TC.DS.16 |
| FR.6.18 | TC.DS.09, 10, 17, 18, 20 |
| FR.6.19 | TC.DS.19 |
| FR.6.20 | TC.DS.21, 22 |
| FR.6.21 | TC.DS.23, 24 |
| FR.6.22 | TC.DS.07, 25 |
