# UC.DS.01 — Docked Shell

Related requirements: FR.6.11–FR.6.22, FR.29.17–FR.29.26, FR.23.12, FR.23.13 (see `docs/requirements/`). Test cases: [TC.DS.01](/docs/testcases/TC.DS.01-Docked-Shell.md). Status: draft, not built.

## Actors
- **User**. **Tessyn** (WPF client, the docked shell). **Windows** (monitors, display changes).

## Preconditions (common)
- The WPF client runs on a data root without a saved `shellLayout`, unless a use case says otherwise; at least one session with Focus items exists.

---

## UC.DS.01 — The default layout
**Main flow:** 1. The User starts Tessyn for the first time. 2. The left pane shows the tabs Sessions, Recent and Tags (Sessions selected, 320 px); the Session view is in the centre with no tab row; the right pane shows Focus at 35 % of the width. 3. No panel has a caption bar.
**Covers:** FR.6.11, FR.6.12, FR.6.13.

## UC.DS.02 — Rearrange by dragging a tab
**Main flow:** 1. The User drags the Recent tab to the bottom edge of the window. 2. Recent docks below the session area; the left pane keeps Sessions and Tags. 3. The User drags Tags onto Recent's tab row: it stacks as a tab. 4. A right-click on a tab offers Float, Dock, Auto-hide and Close.
**Alternative flows:** **A1** — The User tries to drag the Session view: it is not offered as floating (it can be tabbed).
**Covers:** FR.6.13, FR.6.14.

## UC.DS.03 — Float Focus to the second monitor
**Main flow:** 1. The User drags the Focus tab out of the right pane onto the second monitor. 2. Focus is a floating window, owned by the main window, with no taskbar button; the transcript widens. 3. Cards, reply boxes and Send answers work as before. 4. The User minimizes the main window: the floating Focus goes with it.
**Alternative flows:** **A1** — The screensaver covers the window: the floating panel is hidden, and back after it. **A2** — The monitor is unplugged: the panel is clamped to a visible place and that position is not saved.
**Covers:** FR.6.14, FR.6.18, FR.29.16.

## UC.DS.04 — Collapse the left pane with the chevron
**Main flow:** 1. The User clicks the chevron: the left pane collapses into a thin edge tab, the transcript widens, the icon turns to chevron-right. 2. A click on the edge tab slides the pane out over the transcript; Esc or a click on the edge tab closes it. 3. The chevron brings the pane back.
**Alternative flows:** **A1** — Sessions is closed or floating: the chevron is disabled.
**Covers:** FR.6.13, FR.6.15.

## UC.DS.05 — Close a panel and bring it back
**Main flow:** 1. The User closes Sessions from its tab's right-click menu. 2. The hamburger menu's Panels group shows Sessions unchecked. 3. The User picks it: Sessions returns at its last place.
**Alternative flows:** **A1** — The User picks an open panel: it is brought forward.
**Covers:** FR.6.14.

## UC.DS.06 — Focus beside the transcript, or full size
**Main flow:** 1. The session has Open items: the Focus button in the session header and the Focus tab show the badge. 2. With Focus closed, a click on the Focus button shows it again. 3. The User drags Focus onto the session area: a tab row appears with Session and Focus; Focus fills the area as the old full-size Focus did. 4. Dragging it back to the right edge restores the side by side view; the tab row disappears.
**Covers:** FR.6.16, FR.29.19.

## UC.DS.07 — The layout survives a restart
**Main flow:** 1. The User rearranges the layout (Focus floating, Recent docked at the bottom, a preview panel tabbed beside Focus) and waits one second. 2. Tessyn is restarted: the same layout is restored, including the selected tabs and closed panels.
**Alternative flows:** **A1** — The saved layout is damaged or from a newer version: the default layout appears and the log says why.
**Covers:** FR.6.18.

## UC.DS.08 — Reset layout
**Main flow:** 1. The User picks Reset layout in the hamburger menu's Panels group. 2. Tessyn asks to confirm. 3. After confirming, all five panels are back at their default places; floating panels are gone; the session and the composer text are untouched.
**Alternative flows:** **A1** — The User cancels: nothing changes.
**Covers:** FR.6.19.

## UC.DS.09 — Update from the old window layout
**Main flow:** 1. The User had a tree width of 400 px (or a collapsed tree) and no `shellLayout`. 2. After the update the left pane is 400 px wide (or auto-hidden); the rest is the default layout.
**Covers:** FR.6.18.

## UC.DS.10 — Shortcuts work in every panel
**Main flow:** 1. The User presses a shell shortcut with the keyboard focus in the composer, in the Sessions search box, in a Focus reply box and in a floating panel: it works the same each time. 2. Docking, floating or closing a panel while typing in the composer leaves the caret in the composer.
**Covers:** FR.6.20.

## UC.DS.11 — Day/night recolours the docking chrome
**Main flow:** 1. The User clicks the day/night toggle. 2. Tabs, pane borders, splitters, the edge tabs, an open slide-out and floating window frames switch to the other palette at once; no panel reloads.
**Covers:** FR.6.21.

## UC.DS.12 — Test seam
**Main flow:** 1. An end-to-end test starts Tessyn with `--test-window`: the default layout shows and nothing is read from or saved to the user's data. 2. The test hook docks, tabs, floats, auto-hides, closes and shows panels and reads each panel's state. 3. With a `shellLayout` in the test data root, a restart restores it.
**Covers:** FR.6.22, FR.29.13, FR.29.26.
