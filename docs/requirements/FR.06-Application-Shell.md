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

**FR.6.5 — Application icon.** The window, the taskbar button, the tray icon (background host) and the executable use **one icon: the Tessyn Web app icon** (`code/Tessyn.Web/ClientApp/public/icon.svg`, rendered to the sizes Windows needs, 16 to 256 px). The earlier logo is not used. The same artwork serves the web (`icon-192`, `icon-512`) and the desktop, so the product looks one across clients.

**FR.6.6 — Version text.** The version text is a plain label: **not focusable**, no tab stop, no focus rectangle, no selection. A press on it starts a window **drag**, as a press on the empty title bar does (FR.6.4); a double-click maximises or restores.

**FR.6.7 — Title bar contents (WPF).**
- Left to right: chevron toggle, hamburger, day/night, the **instruction toolbar** (icon buttons, each with its text as the tooltip: Auto-Commit cycling, New branch, Document, Compact, Compact now, Terminate; a one-line notice after them, full text in its tooltip), the **Recent dropdown** filling the middle, then the window controls. A 48 px gap right of the dropdown stays free for dragging.
- The **hamburger menu** lists, in order, the registered entries (Import, then Settings), a separator and Exit.
- The dropdown button shows the **open session's name** ("Recent" when none is open); its popup is as wide as the button.

**FR.6.8 — Status bar (WPF).** The two plan-usage meters (FR.18) fill the bar's full height and take all the width left by the other items. The Profiles button is at the far right.

**FR.6.9 — Screensaver picture mode: fill (WPF).** The screensaver (idle cover with a slideshow of the pictures of its folder) shows each picture in **fill** mode: the picture **covers the whole screen**, keeps its **aspect ratio** and is **cropped** where the ratio differs from the screen's (centre crop), instead of being fitted inside the screen with bars. The folder, idle time and slide interval settings are unchanged.

---

## Amendments (2026-11-18, preview windows)

| Existing | Amended by | Change |
|---|---|---|
| FR.6.7 Title bar contents (WPF) | FR.6.10 | A **Monitor** toggle follows the day/night toggle. |

**FR.6.10 — Monitor toggle in the title bar (WPF).** Right after the day/night toggle (before the separator and the instruction toolbar) the title bar has a **Monitor** icon button. It is **enabled only while two or more monitors are connected** (tooltip with one monitor: "Connect a second monitor to open previews in their own windows"); with two or more it is a toggle that looks checked when on, remembered in the client settings, default on. When on, items that have a preview open in their own windows on the extra monitors. Details, the preview groups and the window rules: FR.29.1 and FR.29 (use cases UC.PW.01, test cases TC.PW.01).

---

## Docked shell (2026-11-19)

Status: **Approved by the Architect 2026-11-19** (with the decisions at the end of this section). **Stage 1** (FR.6.11 to FR.6.20, FR.6.22) built 2026-11-19 on branch `wpf/docked-shell` (merge `5c91a24`; unit tests, WPF E2E `WpfDockedShellE2ETests`); the programmatic test hook of FR.6.22 is built with stage 3. **Stages 2 and 3** (FR.6.21; the docked-preview items FR.29.19, FR.29.22, FR.29.23; the test hook) built 2026-11-20 on branch `wpf/docked-shell-23` (unit tests, WPF E2E `WpfDockingThemeE2ETests`, `WpfDockingHook*E2ETests`, `WpfDockedPanelsStage3E2ETests`, `WpfDockedPreviewsRestartE2ETests`). Source: the Architect's request of 2026-11-19 ("reword all the WPF UI to be docked panels ... the whole UI composed of docked tabs: Session tree, Recent sessions, Tags, Session View, Focus View"; concern that a tab row would take space only to host Session and Focus). The Architect chose **option A**: Focus becomes a side panel, not a tab; the Session view is the one central document with **no tab row while it is alone**; a tab row appears only while something shares the session area (FR.29.19, TC.PW.104); the transcript and Focus are side by side by default; the old full-size Focus is one drag away. Use cases: UC.DS.01 to UC.DS.12. Test cases: TC.DS.01 to TC.DS.26. WPF only: no change to the daemon, TDCP, the web client or MCP. Library and the docking rules: FR.29.17 to FR.29.26 (AvalonDock).

| Existing | Amended by | Change |
|---|---|---|
| FR.6.1 Two-panel layout (left: search + tree; right: session view) | FR.6.11, FR.6.12 | The window between the title bar and the status bar is one docking layout; the left panel and the session view are panels of it. |
| FR.6.2 Adjustable left panel width (splitter) | FR.6.12, FR.6.13 | The splitter is the docking splitter; the remembered width is the left pane's (default 320 px). |
| FR.6.3 item 1, chevron toggle (collapses/expands the tree; state in `appsettings.json`) | FR.6.15, FR.6.18 | The chevron toggles auto-hide of the pane that holds Sessions; the state is part of the remembered layout (`shellLayout`). Hamburger and day/night are unchanged. |
| FR.1.15 Left panel tabs (Tree, Recent, Tags; starts on Tree) | FR.6.11, FR.6.12 | Three panels, **Sessions** (the Tree), **Recent** and **Tags**, are tabs of the left pane by default. Their content and the pick-a-session rule of FR.1.15 are unchanged; "the app starts on Tree" now means: the default layout selects Sessions, and a later start restores the selected tab (FR.6.18). |
| FR.1.19 Equal tabs (same width, filling the panel) | FR.6.13 | Replaced by the docking tabs of the pane (compact chrome); the look follows FR.6.21. |
| FR.1.25 Tree toolbar at the bottom of the tree panel | (note) | Unchanged: the toolbar belongs to the Sessions panel's content. |
| FR.12.29 Collapsible input area ("each view's default input area"; Session/Focus switch) | FR.6.17 | The input area belongs to the Session view only; Focus has its own reply boxes. Its collapse state and height (FR.12.36) are unchanged. |
| FR.23.12 Session/Focus switch (segmented, per-session selection, Enter in search switches to Session) | FR.6.16 | Replaced by a **Focus** button; Focus is a panel. The "selection per session" and "Enter in search switches to Session" rules no longer apply (the Session view is always shown; Focus is beside it or elsewhere). |
| FR.23.13 Focus badge (visible only while Session is selected) | FR.6.16 | The badge is shown on the Focus button and on the Focus panel's tab whenever the count is > 0; colour and count rules unchanged. |
| FR.23.21 Send answers (n) left of the Session/Focus switch, visible only while Focus is selected | FR.6.16 | Moves into the **Focus panel** (always visible there, as the panel's own header line); name, enabling rule and Ctrl+Enter (FR.23.22) unchanged. |
| FR.29.18 first time right (and decision 6) | FR.6.12 | A preview panel opens the first time as a **tab in the pane that holds Focus**; the "right, 40 %" rule stays as the fallback. |
| FR.29.24 Theme | FR.6.21 | The theme requirement now covers the whole shell and is specified there (it takes over FR.29.24). |
| FR.29.19 "docking area is the content to the right of the session tree" | FR.6.11 | The docking area is the whole window content between the title bar (with banners) and the status bar. |

**FR.6.11 — Docked shell.**
- Between the title bar (with the banners under it) and the status bar, the window is **one docking layout** (AvalonDock, FR.29.25). Its panels: **Sessions** (the tree with its search box, scope toggles and toolbar), **Recent** (FR.1.16), **Tags** (FR.24.11), the **Session view**, **Focus** (FR.23), and the preview panels (FR.29.17 onwards).
- The **Session view** is the one central document of the layout: the session header (title, tags, output search), the transcript, the find bar and the composer. Everything else is a panel around it.
- Every panel has a **stable id**: `sessions`, `recent`, `tags`, `session`, `focus`; preview panels use the ids of FR.29.21.

**FR.6.12 — Default layout.** On the first start and after **Reset layout** (FR.6.19):
- **Left pane:** Sessions, Recent and Tags as tabs (tabs on top), Sessions selected; width 320 px, or the remembered tree width (FR.6.18).
- **Centre:** the Session view, **no tab row** while it is alone (FR.29.19).
- **Right pane:** **Focus**, 35 % of the docking area's width.
- **Preview panels** open the first time **as tabs in the pane that holds Focus** (amends FR.29.18, decision 6). If Focus is closed or floating, the panel docks to the right of the session area at 40 % as before (FR.29.21).
- Later opens of a panel use its remembered place (FR.29.21, FR.6.18).

**FR.6.13 — Compact chrome.**
- Panels have **no title (caption) bars**. Each pane shows its **tabs on top**; a pane with one panel shows its one tab. **The tab is the handle:** drag it to dock the panel to another side, to stack it as a tab on another pane, or to float it (AvalonDock's drop guides).
- **Right-click on a tab** opens a menu: **Float**, **Dock**, **Auto-hide**, **Close** (each only where it is allowed, FR.6.14).
- A **pin icon at the right end of a pane's tab row** toggles auto-hide of that pane. Auto-hidden panes collapse into **thin tabs on the window edge** and slide out on click, over the session area and over a WebView2; **Esc**, a click on the edge tab or the collapse button closes the slide-out (FR.29 decision 4).
- The session area shows **no tab row while the Session view is alone** (FR.29.19); a tab row appears only while a panel shares the session area.
- Panels are resized by their splitters; the content area stretches and the buttons keep their places (NR.7).

**FR.6.14 — What can close or float; the Panels menu.**
- The **Session view never closes and never floats** (it is the main document; dragging it out is not offered). It can still be tabbed with other panels (FR.29.19).
- **Sessions, Recent, Tags and Focus can be closed.** A closed panel is shown again from the **Panels** group of the **hamburger menu** (FR.6.7): one entry per panel (Sessions, Recent, Tags, Focus), each with a check mark while the panel is open; picking a closed one shows it again **at its last place**; picking an open one brings it forward (shows it if auto-hidden or behind another tab). The group comes after the registered entries (Import, Settings) and before the separator and Exit; **Reset layout** (FR.6.19) is its last entry.
- **Sessions, Recent, Tags and Focus can all float**, for example Focus on the second monitor. Floating panels are owned by the main window (FR.29 decision 5: no taskbar button, they minimize with it), are **hidden while the screensaver covers the window** (FR.6.9), and follow FR.29.16 for monitors and scales.

**FR.6.15 — The chevron.** The chevron toggle of FR.6.3 toggles **auto-hide of the pane that holds Sessions**: the pane collapses to the left window edge, or expands again. Its icon shows the state (chevron-left while the pane is shown, chevron-right while it is collapsed, as FR.6.3 says). The state is remembered (FR.6.18). If the Sessions panel is closed or floating, the chevron is disabled (the Panels menu shows the panel again).

**FR.6.16 — Focus as a panel.**
- The **Session/Focus switch** in the session header (FR.23.12) becomes a **Focus** button, in the same place right of the output search box, with the **badge** (FR.23.13). A click **brings the Focus panel forward**: it shows it if closed (at its last place), slides it out if auto-hidden, selects its tab if tabbed behind another panel, and raises it if floating. It takes the keyboard focus only where FR.6.20 allows.
- The **Focus panel's tab** shows "Focus" and the badge (count and colour as FR.23.13); the badge shows whenever the count is > 0.
- Focus **follows the session shown in the Session view**, as today (FR.23.16). **Everything inside Focus is unchanged**: cards, replies, **Send answers (n)** (now at the top of the panel, FR.23.21), Ctrl+Enter (FR.23.22), the Closed history, Decisions, drafts, references and previews.
- **Dragging Focus onto the session area** gives the old full-size Focus: the Focus panel becomes a tab beside the Session view and a tab row appears (FR.29.19). The Session view is still one tab away.
- The MD and JSONL toggles of the session header, hidden while Focus was selected (FR.23.12), now stay visible: the Session view is always shown.

**FR.6.17 — Composer.** The composer (text box, side buttons, command bar, splitter, FR.12.29) belongs to the **Session view**. FR.12.29's "each view's default input area" no longer applies to Focus, which has its own reply boxes; the Session view's input area keeps its collapse state and its height (FR.12.36). Opening, closing or moving panels never changes the composer's text or collapse state.

**FR.6.18 — Layout memory.**
- The **whole layout** is remembered: the panes, their sides and sizes, the tabs and their order and the selected tab, auto-hide (and its edge), floating windows (position, size, monitor device name and scale, with the rules of FR.29.16) and closed panels. It is saved **500 ms after the last change** in the WPF client settings (`wpf.json`, a new section **`shellLayout`**) and restored at the next start. Panels are identified by the stable ids of FR.6.11 (preview panels as FR.29.21).
- The preview panels' own per-group layout (`previewDock`, FR.29.21) stays its own section; `shellLayout` holds the arrangement of the five shell panels and the places of preview panels that are tabbed or docked next to them, resolved by panel id.
- **First start after the update:** with no `shellLayout`, the old settings are taken over once: the saved **tree width** becomes the left pane's width, and a **collapsed tree** (FR.6.3) becomes an **auto-hidden left pane**. The old values are not deleted.
- A saved layout that **cannot be read** (damaged, or written by a newer version: an unknown version number) is **ignored**, the default layout is used, and the log says why. A floating panel whose monitor is gone is clamped to a visible place (FR.29.16); a saved panel id that is unknown is skipped.
- A move that Windows forces (a monitor unplugged, a resolution change) is never saved for a floating panel (FR.29.21).

**FR.6.19 — Reset layout.** The hamburger menu's Panels group (FR.6.14) ends with **Reset layout**. It restores the default layout (FR.6.12): all five panels shown at their default places, no floating panels, no auto-hide, the left pane at 320 px, Focus at 35 %. Preview panels that are open are kept open and go to the default place for a preview panel (FR.6.12); the previews' remembered group layouts (`previewDock`) are not deleted. The layout is saved at once. The command asks first (**Open points, 1**). Reset layout does not close the session and does not touch the composer, the theme or the Monitor toggle.

**FR.6.20 — Keyboard.**
- Every shell shortcut (the command registry, FR.6; the title bar's commands) works **whatever panel has the keyboard focus**, also in **floating panels**: a floating panel routes unhandled key gestures to the same command bindings as the main window.
- Opening, closing, docking, floating, tabbing, auto-hiding or resizing a panel **never moves the keyboard focus** away from the composer or the transcript, **unless the user clicked into the panel** (the click gives the panel the focus, as any click does). The Focus button of FR.6.16 and the Panels menu do not take the focus either; picking a panel from the menu with the keyboard returns the focus to where the menu was opened from.
- Tab order inside a panel is unchanged; Esc closes an open slide-out (FR.6.13) when the focus is in it.

**FR.6.21 — Theme (takes over FR.29.24).** The docking chrome (tabs, pane borders, splitters, drop guides, the thin edge tabs, the slide-out, floating window frames, the pin icon and the tab context menu) uses **Tessyn's light and dark colours** (the brushes of FR.9; a custom AvalonDock theme built on the VS2013 resource dictionaries, re-keyed to Tessyn's tokens) and the **look of today's left-panel tabs** (flat, the accent line on the selected tab). Switching day/night recolours all of it **at once**, with no restart and no panel recreated; a WebView2 inside a panel is not reloaded by it.

**FR.6.22 — Test seam.** With `--test-window` (FR.29.13) the layout is **neither restored from nor saved to the user's data**: the default layout is used; a test data root may hold a `shellLayout` so that a restart can be tested, and then it is read and saved there, not elsewhere. A **test hook** can, by programmatic calls, **dock** a panel to a side, **tab** it with another, **float** it, **auto-hide** it, open its slide-out, **close** it and **show** it again, apply **Reset layout**, and **report each panel's state** (side, pane, tab index, selected, size, auto-hide and edge, floating rectangle, shown or closed, badge text). The main window and the floating panels are off-screen, not in the taskbar and never activated, and use the test-window focus guard (FR.29.13, FR.29.26). Mouse drags of tabs are not driven by E2E tests (MANUAL, TC.DS).

### Decisions (Architect, 2026-11-19, docked shell)

The draft was approved with these answers:
1. **Reset layout asks first**, with one confirmation ("Reset the window layout? Panels you moved, floated or closed go back to their default places.").
2. **The Session view cannot float** (FR.6.14); it can still be tabbed.
3. **Focus at 35 %** of the docking area's width (FR.6.12).
4. **Previews' first place is next to Focus, as tabs** (FR.6.12; amends decision 6 of FR.29, whose "right at 40 %" stays the fallback).
5. **Stages:** **1** the docked shell with the left panels, Focus as a panel, layout memory, Reset layout, the Panels menu, keyboard (FR.6.11 to FR.6.20, FR.6.22); **2** the Tessyn theme for the chrome (FR.6.21; until then the VS2013 default shows); **3** the remaining docked-preview items (restart of panels, auto-hide behaviours, floating panels across scales: FR.29.19, FR.29.22, FR.29.23).
6. **Focus closed while it holds the badge:** the Focus button in the session header keeps the badge; a click shows the panel again.
7. **Left pane in a narrow window:** no automatic collapse; the user collapses it with the chevron.
