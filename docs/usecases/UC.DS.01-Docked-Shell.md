# UC.DS.01 — Docked Shell

Related requirements: FR.6.11–FR.6.23, FR.29.17–FR.29.26, FR.23.12, FR.23.13 (see `docs/requirements/`). Test cases: [TC.DS.01](/docs/testcases/TC.DS.01-Docked-Shell.md). Status: draft, not built.

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

## UC.DS.13 — Rename, delete and star from the Focus panel
**Preconditions:** a session is shown in the Session view and has Focus items; Focus floats on the second monitor, or is tabbed over the session area.
**Main flow:** 1. The Focus panel's top row shows Delete, the session's name, Running / Idle and the star, with Send answers (n) at the right end, in the same size and place as the Session view header's first row. 2. The User double-clicks the name in Focus, types a new name and presses Enter; the Session view header and the tree show it too. 3. The User clicks the star in Focus; the star in the Session view and in the tree change with it. 4. A turn starts: the text reads Running in both headers, then Idle. 5. The User clicks Delete in Focus; the confirmation appears; confirming removes the session.
**Alternative flows:** **A1** — Esc in the rename box keeps the old name. **A2** — Cancelling the delete confirmation keeps the session. **A3** — No session is shown: only Send answers and Session are in the row. **A4** — The panel is narrow: the name is trimmed with an ellipsis, the buttons keep their places. **A5** — Focus is tabbed over the session area: the User clicks **Session** (or presses Ctrl+1) and the Session view's tab is selected; Ctrl+2 (or the Focus button) goes back to Focus.
**Covers:** FR.6.23, FR.1.25, FR.9.7, FR.23.21.

## UC.DS.14 — Change model, effort, profile, streaming and sound from the Focus panel
**Preconditions:** a session is shown in the Session view and has Focus items; Focus floats on the second monitor, or is tabbed over the session area. *Status: approved (A36, FR.6.24).*
**Main flow:** 1. The Focus panel shows the settings bar at its bottom, with the same controls as the Session view's bar. 2. The User picks another model in the Focus bar; the Session view's bar shows it too. 3. The User picks another profile and toggles sound in the Session view's bar; the Focus bar follows. 4. The User answers a card and clicks Send answers (n); the turn runs with the model, effort and profile the bars show. 5. The User clicks the **Settings** toggle between Send answers (n) and Session; both bars hide (the flag is the Session view's), both toggles show unchecked, and a click on either toggle shows both again; the state is kept after a restart.
**Alternative flows:** **A1** — The User flips the composer's gear instead: the Focus bar follows, and the Focus toggle's checked state follows too. **A2** — No session is shown: the bar is shown as the Session view's bar is without a session. **A3** — The panel is narrow: the controls wrap, the cards area keeps scrolling.
**Covers:** FR.6.24, FR.6.16, FR.12.11, FR.12.35, FR.12.29.

## UC.DS.15 — Back to the Session view when the next session has nothing open in Focus
**Preconditions:** Focus is tabbed with the Session view in the session area and its tab is selected. *Status: draft, awaiting the Architect (A39, FR.6.25).*
**Main flow:** 1. The User, reading Focus, picks another session in the tree (or Recent, Tags, the Recent drop-down, a session reference). 2. The Session view shows the new session and its Focus items load. 3. The session has no open items (badge count 0). 4. The Session view's tab is selected, as if the User had clicked Session or pressed Ctrl+1; the keyboard focus stays where it was; one log line says why.
**Alternative flows:** **A1** — The new session has open items: Focus stays selected and shows them. **A2** — Focus is a side panel, floating or auto-hidden: nothing changes. **A3** — The User answers or dismisses the last open item while reading Focus: Focus stays (empty state). **A4** — The items are still loading, or the load fails: nothing changes. **A5** — The Session view's tab is selected and the User picks a session that has open items: it stays on the Session view, the badge shows the items. **A6** — The only open items are Decided: the badge is 0, so the switch happens.
**Covers:** FR.6.25, FR.6.16, FR.6.23, FR.23.13, FR.23.16, FR.6.20.
