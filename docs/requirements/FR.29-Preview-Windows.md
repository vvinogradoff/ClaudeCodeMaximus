> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). New area, drafted 2026-11-18. FR.29 was reserved for "multi-screen layout" by the WPF programme plan; it now specifies **preview windows on extra monitors**. Use cases: [UC.PW.01](/docs/usecases/UC.PW.01-Preview-Windows.md). Test cases: [TC.PW.01](/docs/testcases/TC.PW.01-Preview-Windows.md). Related: FR.27 (the embedded browser these windows use), FR.23.18 (preview pane), FR.24 (tag chips), FR.26 (links and paths), FR.6.10 (title bar toggle), FR.4.7 (Settings close guard).

### FR.29 — Preview Windows on Extra Monitors

The Architect works with two or more monitors and wants previews out of the main window. With the **monitor toggle** on, an item that has a preview (a `.md` file, an image, a web link) opens in **its own window** that can be moved to any monitor. The windows are kept per **preview group** (a kind of item that the user defines), so every group has its own window, remembers where the user put it and keeps its content until the next item of the group arrives. WPF only (decision D8): no change to the daemon, TDCP, the web client or MCP. Groups, the toggle and the placements live in the client settings file (`wpf.json`), because each client owns its layout (P7).

**FR.29.1 — Monitor toggle.**
- A **Monitor** icon button in the title bar, right after the day/night toggle (FR.6.10).
- It is **enabled only while two or more monitors are connected**. Monitors are counted at start and after every display change (debounced, about 1 s). With one monitor the button is disabled and its tooltip reads **"Connect a second monitor to open previews in their own windows"**.
- With two or more monitors the button is a **toggle** (it looks checked when on). The choice is saved in the `previewWindows` section of `wpf.json`; the **default is on**, so a second monitor that is plugged in enables previews in windows by itself unless the user switched the toggle off.
- This is the **preview windows mode**. It is "on" only when the toggle is on **and** two or more monitors are present. Otherwise every item opens as before (FR.26.3, FR.26.5, FR.23.18).
- Turning the toggle off closes every group window. Pinned windows (FR.29.8) stay.

**FR.29.2 — Preview groups and Settings → Previews.**
- A **preview group** has a **name**, one text of **patterns** (FR.29.3), an **Auto-close** switch (FR.29.9) and a remembered window placement (FR.29.6). Every group has its own window.
- Settings has a **Previews** tab with the ordered list of groups. Columns: **Name**, **Patterns** (one text box; patterns are separated by spaces or commas) and **Auto-close** (check box). Buttons: **Add**, **Remove**, **Move up**, **Move down**, **Reset to defaults**, **Forget window positions** and **Clear browser data** (FR.27.8). The list order matters only as the tie-break of FR.29.3.
- **Save** validates the list: an empty name, two groups with the same name, a group without a valid pattern, or a pattern that is neither of the forms in FR.29.3 is refused with a message that names the group and the pattern; nothing is saved until the list is valid. Saving drops the remembered placements of groups that no longer exist. The tab counts as an unsaved change for the close guard (FR.4.7).
- **Reset to defaults** puts the default list below into the tab. Like any edit in the tab it takes effect with **Save** and is dropped with Discard.
- **Forget window positions** deletes every remembered placement at once, after the user confirms. The groups stay; the next window of each group opens at the first-time position (FR.29.5).
- **Defaults** (seeded on first use; auto-close off). They are the Architect's list of 2026-11-18; the image group also lists `gif`, `webp`, `bmp` and `tif`, so that every image the preview shows goes to the same window (TIFF: FR.29.12). Each line is one group, so each gets its own window:

| Name | Patterns |
|---|---|
| Images | `jpg jpeg png gif webp bmp tif tiff` |
| Text and code | `md txt h cpp` |
| CSV | `csv` |
| Slack | `https://hardstop.slack.com/*` |
| Windows app | `https://github.com/tryhardstop/windows-app/*` |
| Backend and frontend | `https://github.com/tryhardstop/backend-v1/* https://github.com/tryhardstop/frontend-v1/*` |
| Linear | `https://linear.app/hardstop/issue/*` |
| Web | `https://*` |

- The list is stored in the `previews` section of `wpf.json` (no value = the defaults). The user may split a group later (for example `cpp h` on its own), so that a `.md` and a `.cpp` preview show side by side.

**FR.29.3 — Patterns and the matching rule.**
- **File extension:** `md` or `.md`. Letters and digits only; case-insensitive. It matches a local file with that extension.
- **URL pattern:** an absolute `http://` or `https://` URL in which `*` matches any run of characters (also `/`, `?` and `#`). Case-insensitive. `https://*` matches every https URL. A pattern that ends with `/*` also matches the address without that last slash, so `https://github.com/tryhardstop/windows-app/*` also matches the repository page `https://github.com/tryhardstop/windows-app` (a repository tag chip, FR.24).
- Anything else is refused on Save (FR.29.2).
- **When several groups match, the group with the most specific matching pattern wins:** the one with the most characters other than `*` in the matching pattern. A tie goes to the group higher in the list. (So `https://hardstop.slack.com/*` wins over `https://*` whatever their order.)
- An item that matches **no group** opens as without this feature: a file in the preview pane (a video in the default media player, FR.26.6), a link in the system browser.

**FR.29.4 — Which clicks go to a preview window.** With the preview windows mode on (FR.29.1) and a matching group (FR.29.3), the item opens in the group's window (FR.29.5) instead of the preview pane or the system browser. This applies to all of these:
1. a local path in the transcript (Ctrl+left-click, FR.26.5, FR.26.6). A video path goes to a window only when a group lists its extension; otherwise it opens in the default media player as before;
2. a web link in the transcript (Ctrl+left-click, FR.26.3, FR.26.6);
3. a Focus item's file reference and url reference (FR.23.18);
4. a session tag chip and the chip's **Open in browser** menu entry (FR.24.7). The URL policy of the chips (`SessionTagUrlPolicy`) is unchanged: only the URLs it allows open at all;
5. a link clicked inside a markdown preview (in the pane or in a window) and inside the markdown of a Focus card.

- An item with no matching group, and every item with the mode off, opens as before. The preview pane stays for them.
- The explicit buttons **Open in default app** and **Open in system browser** always act on the system and are never routed.
- The item remembers where it came from: the session shown in the main window (items 1 to 5) and, for a Focus reference, the Focus item (FR.29.9).
- A refused or failing item shows the same dismissible message as in the pane (FR.23.18); it never shows a dialog.

**FR.29.5 — The group window.**
- There is **one window per group**. While it is open, a new item of the group **replaces its content**. A minimized window is restored. The window is brought to the top **without taking the keyboard focus** from the main window.
- When it is not open, it opens at the group's remembered placement (FR.29.6). **First time** (nothing remembered): on the largest monitor that does not hold the main window, centred, at 60 % of that monitor's work area.
- **Title:** `<item name> - <group name>` (the file name, or the page title for a web page).
- **Content:** a file uses the preview pane's renderer with the same sandbox and limits (FR.23.18): markdown with the Source toggle, text and code, image, video, the PDF hint, **Open in default app**, progress and Cancel. A web page uses the embedded browser (FR.27). Every window has its own renderer, so windows do not disturb each other or the preview pane.
- The user may move, resize, minimize or maximize the window on any monitor and may close it. Closing it only closes it; the group, its settings and its placement stay.
- A preview window is a top-level window of its own with its own taskbar button. It is not owned by the main window: minimizing or moving the main window leaves it where it is. It follows the app's theme and colours.

**FR.29.6 — Remembered placement.**
- Moving, resizing, maximizing or restoring a group window remembers its placement for the group (saved 500 ms after the last change, as the main window does): the normal rectangle in physical pixels, the maximized flag and the **device name of the monitor**. It is stored per group id in the `previewWindows` section of `wpf.json`.
- The next window of the group opens there, clamped to the monitors present. The remembered monitor is found by its device name, or, when Windows renamed it, by its place in the monitor layout (the origin of its work area). When it moved in the layout, the window moves with it. When the remembered monitor is gone, the window opens on a monitor other than the main window's, centred, with its size clamped.
- Never remembered for the group: a move that Windows forces (a monitor unplugged, a resolution change), the minimized state, and everything a pinned window does (FR.29.8) (a pinned window keeps its own last place only to reopen after a restart, FR.29.11).

**FR.29.7 — Window chrome.**
- A preview window has its own title bar in the style of the main window's (FR.6.4, FR.6.6): icon, title, then **Pin**, **Auto-close**, Minimize, Maximize/Restore and Close. It can be dragged by the title bar; a double-click maximizes or restores.
- **Auto-close** shows the group's switch (FR.29.9). A click flips the group's switch and saves it at once; the Settings tab shows the same value. It is hidden on a pinned window.
- **Pin** detaches the window from its group (FR.29.8). Pin is **one-way**: a pinned window shows the pinned state and stays pinned until it is closed.
- Under the title bar: for a file the preview header (Source toggle, **Open in default app**, progress, messages; no Close of its own); for a web page the browser bar (FR.27.4).
- Stretching rule (NR.7): the content stretches with the window; the buttons keep their places.

**FR.29.8 — Pinned window.**
- Pin detaches the window from its group. The window keeps its item until the user closes it; it never auto-closes; its moves and size changes are never written to its group (FR.29.6), and its own last place is kept only to reopen it after a restart (FR.29.11). Pinning tells Tessyn that the window no longer belongs to its group (a window showing `credentials.md` must not be reused for the next `.md` preview); it keeps its own size and position changes; there is nothing to give back, so there is no unpin. After a restart it reopens pinned, with its item (FR.29.11).
- The group's next item opens a **new** group window at the group's remembered placement. Any number of windows of one group can be pinned.
- Turning the toggle off, or losing the extra monitor, leaves pinned windows open (FR.29.1, FR.29.10).

**FR.29.9 — Auto-close.**
- Per group, **default off**. With it on, the group's window closes when:
  1. the main window shows another session than the one the item was opened from (the tree, Recent, Tags, the dropdown, a reference jump or Back); or
  2. the item was opened from a Focus item's reference and that item is no longer open (answered, resolved or dismissed).
- Rule 2 applies only to items opened from a Focus item's reference. Every other item (a transcript path or link, a tag chip, a link inside a preview) counts as opened from the session the main window showed at that moment, so only rule 1 applies to it. An item opened while no session was shown closes at the next session the main window shows.
- Pinned windows never auto-close.

**FR.29.10 — Monitors change.**
- When the **last extra monitor is unplugged**, the toggle becomes disabled (FR.29.1) and the mode is off: group windows close and their forced placement is **not** saved. Pinned windows stay; Windows moves them onto the remaining monitor.
- When a second monitor is **plugged in again**, the toggle is enabled again and the remembered choice applies. Nothing reopens by itself.
- While two or more monitors stay connected, a change of the monitor layout does not close anything; windows that Windows moved are not remembered at the new place (FR.29.6).

**FR.29.11 — Restart: the preview windows reopen.**
- When Tessyn closes, every open preview window closes and is remembered with its item: group windows and pinned windows. A pinned window also remembers its own last placement, only for this (its group's placement is never touched, FR.29.8).
- **At the next start the preview windows reopen with their items** (Architect, 2026-11-18): a group window at its group's remembered placement (FR.29.6), a pinned window pinned again at its own last placement, both clamped to the monitors present. A web page reopens at the address it showed when Tessyn closed. A file is opened again from its Focus reference, or by asking the daemon again for the transcript path (the path must still occur in that entry and the file-access rules apply, FR.26.5). The address is saved once the page has finished loading and stayed for a second, only while it is on the site the item was opened on, and never with sign-in parameters: a query or fragment that carries a code, state, token or the like is dropped.
- Group windows reopen only when the preview windows mode is on at start (FR.29.1). Pinned windows always reopen, as they also stay open when the mode goes off (FR.29.10).
- A window whose item can no longer be opened (the file is gone or refused, the session or the entry no longer exists) is not reopened; the log says why. A window of an auto-close group is not reopened when its item came from another session than the one the main window opens with (it would close at once, FR.29.9).
- The windows reopen once the main window is connected to the daemon, and they do not take the keyboard focus. A window the user closed is not reopened.
- The list of open windows is kept in the `previewWindows` section of `wpf.json` and saved whenever a window opens, closes, is pinned, a pinned window is moved or resized (500 ms after the last change), or a web page settles at a new address (see above), so a crash loses nothing.

**FR.29.12 — File kinds.**
- **TIFF.** A `.tif` or `.tiff` file is decoded by the host (Windows Imaging Component) and shown as a PNG from the preview cache, in the preview pane and in preview windows. The 25 MB image limit of FR.23.18 applies, and a limit of 100 megapixels, checked before the image is decoded. The inline images in the transcript (FR.26.5) are unchanged.
- **CSV.** A `.csv` file keeps today's text preview; a table view is not part of this requirement.

**FR.29.13 — Test seam.** With `--test-window` the preview windows are off-screen, not in the taskbar and never activated, and they use the test window focus guard. The switch `--test-monitors <n>` (accepted only together with `--test-window`) makes the app see **n** monitors, so that end-to-end tests can enable the toggle on a machine with one screen. The list of open windows (FR.29.11) is kept under `--test-window` too, in the test data root, so a restart can be tested; placements are not saved there.

**Decisions (Architect, 2026-11-18).**
1. When several groups match, the most specific pattern wins; a tie goes to the group higher in the list (FR.29.3).
2. After a restart all preview windows reopen with their last items (FR.29.11).
3. Pin is one-way (FR.29.7, FR.29.8).
4. All five click sources are routed (FR.29.4).
5. CSV keeps the text view (FR.29.12).

## Amendments (2026-11-18, Architect feedback)

| Existing | Amended by | Change |
|---|---|---|
| FR.29.7 Window chrome | FR.29.14 | A file preview window has no header row; Source and Open in default app move to the title bar. |
| FR.29.5 The group window | FR.29.15 | The window can be resized from every edge and corner. |

**FR.29.14 — File controls in the title bar.** In a file preview window there is no header row under the title bar: the window title already names the file.
- **Source** (a toggle, only for markdown) and **Open in default app** (only for the types FR.23.18 allows) are icon buttons in the title bar, **left of the title**, with tooltips.
- Progress with Cancel and the messages appear under the title bar only while there is something to show.
- The web preview keeps its browser bar (FR.27.4, FR.27.10).
- The preview pane in the main window keeps its header as before.

**FR.29.15 — Resizing.** A preview window that is not maximized can be resized from every edge and corner: the resize cursor appears on all four edges, and the page content never covers the resize border. Maximized windows have no border.

## Amendments (2026-11-19, preview improvements)

| Existing | Amended by | Change |
|---|---|---|
| FR.29.6 Remembered placement | FR.29.16 | The remembered rectangle is the window's actual one, also after Snap; sizes keep their pixels across monitors of different scales. |
| FR.29.11 Restart | FR.29.16 | A pinned window's own last place follows the same rules. |

**FR.29.16 — Placement with Snap and monitor scaling.**
- The remembered placement is the window's **actual** rectangle as the user sees it, also when Windows arranged it (Snap to a half or a quarter, Snap layouts from the top of the screen). The next window of the group opens at exactly that rectangle on that monitor (Windows does not treat it as snapped then; it is a normal window of the same place and size).
- Sizes are kept in the pixels of the monitor the window was on, together with that monitor's scale (DPI). A window that reopens on a monitor with another scale than the one it was created on gets **exactly the remembered size** there; it is not rescaled on the way (a `DpiChanged` during the restore applies the remembered rectangle again; later ones, when the user drags the window across monitors, rescale normally). When the remembered monitor is gone and the window opens on another monitor, its size is converted by the ratio of the two scales, then clamped; a placement saved without a scale is only clamped. If the remembered monitor is still there but its scale was changed since, the size and position are converted by the ratio of the old and the new scale, so the window keeps its apparent size.
- The same rules hold for a pinned window's own last place (FR.29.11) and for maximized windows: what is remembered behind the maximized state is the actual normal rectangle the window had before it was maximized.

## In-window docked previews (Architect item 5, 2026-11-19)

Status: **Approved by the Architect 2026-11-19** (with the decisions at the end of this section); not built yet. Source: the Architect's request of 2026-11-19 ("Preview in Tessyn window: let me drag and dock this window and make it auto collapsible; same rules as for external monitors: remember per-group settings, add pin and auto-close, same behaviour; settings for external monitors and single monitor are remembered independently"). The Architect chose **AvalonDock**; the spike passed (WebView2 inside docked, tabbed, floating and auto-hidden panels, no reload, slide-out drawn above the transcript's WebView2; report `.mm-work/40-spike.md`). Use cases: UC.PW.17 to UC.PW.24. Test cases: TC.PW.77 to TC.PW.102. WPF only (decision D8): no change to the daemon, TDCP, the web client or MCP.

Without external preview windows (one monitor, or the toggle off) previews no longer go to the single preview pane: each preview group gets a **docked panel** inside the main window, with the same groups, the same content, Pin and Auto-close as a preview window, and the layout the user gives it is remembered per group, separately from the external-window placements.

| Existing | Amended by | Change |
|---|---|---|
| FR.29.1 Monitor toggle (the mode is "on" only with the toggle on and 2+ monitors; otherwise items open as before) | FR.29.17 | The same condition now chooses between external windows and docked panels. With the mode off, items of a group no longer fall back to the preview pane or the system browser: they open in docked panels (a web link of a group opens in the panel's embedded browser). |
| FR.29.3 "An item that matches no group opens as without this feature" | FR.29.18 | In docked mode a file with no group opens in the default Preview panel (the old preview pane's role); a link with no group still opens in the system browser. In both modes a file with no group (not a video) opens in the default Preview panel of the main window; with external windows active the default panel still docks in the main window. |
| FR.29.4 Which clicks go to a preview window | FR.29.17 | The five click sources route to the group's docked panel when docked mode is active. Open in default app and Open in system browser are still never routed. |
| FR.29.8, FR.29.9 Pinned window, Auto-close | FR.29.20 | The same meanings for panels. |
| FR.29.10 Monitors change, FR.29.11 Restart | FR.29.22, FR.29.23 | Docked panels reopen and change with the mode by the same rules. |
| FR.23.18 the preview pane (file references open in the preview pane) | FR.29.17, FR.29.18 | The single preview pane column is replaced by the default Preview panel of the docking area. The file renderer, its limits and the sandbox are unchanged. |
| FR.27 Embedded browser | FR.29.18 | The embedded browser also runs inside a docked panel (same profile, same policies, same bar). |

**FR.29.17 - Docked mode.**
- **Docked mode** is active exactly when the preview windows mode (FR.29.1) is **not**: one monitor, or the toggle off. With two or more monitors and the toggle on, previews open in external windows (FR.29.4, FR.29.5); otherwise they open in docked panels inside the main window. The monitor toggle keeps its meaning and its look; its tooltip while it is disabled is unchanged.
- In docked mode every click source of FR.29.4 (transcript path, transcript link, Focus file reference and url reference, session tag chip and its **Open in browser** entry, link inside a markdown preview or Focus card) goes to the matching group's panel (FR.29.18). The URL policy of the chips, the video rule (a video goes to a panel only when a group lists its extension, else the default media player, FR.26.6), the access rules and the refusal message (dismissible, in the panel, no dialog) are unchanged.
- The panels belong to the main window's **docking area** (FR.29.19). Opening an item shows its panel without taking the keyboard focus from the composer; a collapsed or tabbed-behind panel is brought forward (an auto-hidden one: Open points, 3).

**FR.29.18 - One panel per group.**
- There is **one docked panel per preview group**, with the same groups and the same matching rule as FR.29.2 and FR.29.3. While the panel is open, a new item of the group **replaces its content**.
- **Content:** the same as a preview window (FR.29.5, FR.29.14): a file uses the preview pane's renderer with the same sandbox and limits (markdown with Source, text and code, image, video, PDF hint, **Open in default app**, progress and Cancel); a web page uses the embedded browser with its bar (FR.27.4, FR.27.10). Every panel has its own renderer or browser; a panel's web page is not reloaded when the panel is docked elsewhere, tabbed, floated, auto-hidden or hidden (the spike showed this works).
- **Title:** `<item name> - <group name>`, as FR.29.5 says, on the panel's tab and header.
- **No group.** A file with no matching group opens in the default **Preview** panel, which takes over the old preview pane's role: it shows one file at a time, has Source and Open in default app, and is remembered like a group panel (key `default`, FR.29.21). It has no Pin and no Auto-close of its own, because it is not a group. In both modes a file with no matching group (not a video) opens in the default Preview panel of the main window, which replaces the old preview pane; with external windows active the default panel still docks in the main window. A link with no matching group opens in the system browser as today. When no preview target can show a file (no docking area, the panel cannot be created), the session pane shows the dismissible notice "This file cannot be previewed here." instead of doing nothing.
- Closing a panel (its Close button, or the tab's close) only closes it; the group, its settings and its remembered layout stay. A panel closed by the user is not reopened by the next item's absence: the group's next item opens it again at the remembered place (FR.29.21).

**FR.29.19 - Docking, tabs, floating, auto-hide.**
- The docking area is the main window's content to the right of the session tree: the **session pane** (the transcript or the Focus view with the composer) is the central, non-closable document. The session tree on the left, the title bar and the status bar stay outside the docking area.
- A panel can be dragged by its title and **docked** to the left, right, top or bottom of the session pane, or onto another panel's tab strip to **stack as a tab**; AvalonDock's drop guides show the targets. Panels can be resized by their splitters. A panel dropped onto the centre of the session pane becomes a tab beside the session; the tab strip shows while the session pane holds more than the session itself.
- A panel can be **floated**: it becomes a window of its own that belongs to the docking layout and stays part of it (it is not an external preview window: it is not subject to the monitor toggle, FR.29.1, and has the layout's own tab, Pin and Auto-close in its header). It can be dragged back into the docking area. A floating panel is owned by the main window and does not have its own taskbar button.
- **Auto-hide.** A panel's pin-style header button collapses it into a **tab on the edge of the main window**. A click on that tab slides the panel out over the session pane (drawn above the transcript and above a WebView2); a click elsewhere, or the panel's own collapse button, closes it again. Several auto-hidden panels on one edge show one tab each. The user can toggle a panel back into the docked layout.
- A panel's header (title, **Pin**, **Auto-close**, auto-hide, float and close) and the content area follow the stretching rule (NR.7): the content stretches, the header buttons keep their places.

**FR.29.20 - Pin and Auto-close in panels.** The header of a group panel has **Pin** and **Auto-close**, with the meanings of FR.29.7 to FR.29.9:
- **Auto-close** shows and flips the group's switch (saved at once; the Settings tab shows the same value); hidden on a pinned panel. With it on, the group's panel closes when the main window shows another session than the one the item was opened from, or when the Focus item the item was opened from is no longer open (FR.29.9, rules 1 and 2, with the same exceptions for items not opened from a Focus reference).
- **Pin** is one-way: the panel detaches from its group, keeps its item until the user closes it, never auto-closes, and its moves, size and dock changes are never written to its group's layout (FR.29.21); its own last place is kept only to reopen it after a restart (FR.29.22). The group's next item opens a **new** panel at the group's remembered place; any number of panels of a group can be pinned. A pinned panel is a tab or a panel like any other for docking, floating and auto-hide.
- The default Preview panel (FR.29.18) has neither button.

**FR.29.21 - Remembered layout per group, independent of external windows.**
- A new `previewDock` section of `wpf.json` keeps, **per group id** (and for `default`), what the user did with the group's panel: the **side** (left, right, top, bottom of the session pane) and **which pane or tab group** it shared (by the group ids it was stacked with) and its **position in the tabs**; its **size** (width or height of its pane); **auto-hide** on or off (and the edge); the **floating rectangle** (position, size, the **device name of the monitor** and the **scale**, with the rules of FR.29.16: sizes kept in pixels, conversion by the ratio of the scales, clamping to the monitors present, a placement saved without a scale only clamped); and **shown or closed**.
- It is saved 500 ms after the last change (as FR.29.6) and **independently** of the `previewWindows` section (FR.29.6): turning external windows on and off never changes either one. Settings for one monitor and for external monitors are therefore remembered separately: what the user arranged on one screen is there again after the second monitor has come and gone.
- The next panel of the group opens at its remembered layout, clamped to what exists (a remembered neighbour that is not open: the panel docks to the same side of the session pane). First time (nothing remembered): docked to the **right** of the session pane at 40 % of the area's width.
- Never remembered: a move that Windows forces (a monitor unplugged, a resolution change) for a floating panel, and everything a pinned panel does (FR.29.20).
- Settings → Previews **Forget window positions** (FR.29.2) deletes the `previewDock` section too, after the user confirms.

**FR.29.22 - Restart.** When Tessyn closes, every open docked panel is remembered with its item (group panels, pinned panels, the default Preview panel with its file), in the `previewDock` section, saved whenever a panel opens, closes, is pinned, or its layout changes. At the next start the panels that were open **reopen with their items** by the rules of FR.29.11: a file is checked again (the access rules; the path must still occur in the entry; a Focus `refId` is asked again), a web page reopens at its settled address, a panel whose item can no longer be opened is skipped (the log says why), a panel of an auto-close group whose item came from another session than the main window opens with is not reopened, a panel the user closed is not reopened; they reopen once the main window is connected, without taking the keyboard focus. Group panels reopen only in docked mode; **pinned panels reopen in either mode** (in a mode with external windows, a pinned docked panel stays a docked panel, FR.29.23).

**FR.29.23 - The mode changes.**
- When external windows **become active** (a second monitor is connected with the toggle on, or the toggle is turned on): the docked **group** panels (floating ones too) close, **their layout is saved before they close**, and the next items go to windows (FR.29.4). **Pinned docked panels and the default Preview panel stay** where they are (Open points, 2; FR.29.18: the default panel is the old pane's replacement in both modes).
- When external windows **become inactive** (the last extra monitor is unplugged, or the toggle is turned off): group windows close as FR.29.10 says (pinned windows stay), and the next items go to docked panels. Nothing reopens by itself, in either direction.
- A placement forced by a display change is never saved for a floating panel (FR.29.21).

**FR.29.24 - Theme.** The panels follow Tessyn's light and dark theme: the tab strips, headers, splitters, drop guides, the auto-hide edge tabs and the slide-out use Tessyn's brushes (a custom AvalonDock theme built on the VS2013 resource dictionaries, re-keyed to Tessyn's tokens), so that switching the day/night toggle (FR.6.10) recolours them at once, and the colours match the main window.

**FR.29.25 - Library.** The docking is built on **AvalonDock**: the NuGet packages `Dirkster.AvalonDock` and `Dirkster.AvalonDock.Themes.VS2013`, version 5.0.2 (license **Ms-PL**, permissive; the license notice is kept with the product; a native `net10.0-windows7.0` build exists), added as a new dependency of `TessynDesktop.WPF` only. The Architect approves the dependency together with this draft. Tessyn stores its own per-group layout (FR.29.21) and does not use AvalonDock's layout serializer for it.

**FR.29.26 - Test seam.** With `--test-window` the docked panels stay testable: a test hook docks a group panel to a side, stacks it as a tab, floats it, toggles auto-hide, opens the slide-out and hides and shows it by programmatic calls, and reports each panel's state (side, pane, tab index, size, auto-hide, floating rectangle, shown); the main window and its floating panels are off-screen, not in the taskbar and never activated, and use the test-window focus guard (FR.29.13). `--test-monitors <n>` selects the mode (FR.29.13); the `previewDock` state is kept under `--test-window` in the test data root, so a restart can be tested; placements of floating panels are not saved there.

**Decisions (Architect, 2026-11-19).** The draft and the AvalonDock dependency (FR.29.25) were approved with these answers:
1. **Items without a group (FR.29.18):** a file with no group opens in the default **Preview** panel (the old preview pane's role, remembered like a group panel); a link with no group goes to the system browser.
2. **Pinned docked panels when external windows become active (FR.29.23):** they stay docked in the main window.
3. **A new item for an auto-hidden panel (FR.29.17):** the panel's edge tab is brought forward and the panel slides out over the session pane without taking the keyboard focus; a click elsewhere closes it again.
4. **Slide-out close over a web page:** when a click away over a WebView2 does not reach Tessyn, the slide-out also closes on a click on its edge tab, on its collapse button and on **Esc**; no global mouse hook.
5. **Floating panels** are owned by the main window: no taskbar button of their own, they minimize with it.
6. **First-time place:** docked to the right of the session pane at 40 % of the area's width.
Status: Specified and approved 2026-11-18; built 2026-11-18 on branch `wpf/preview-windows` (merge `1d07de9`; unit tests, WPF E2E `WpfPreviewWindows*E2ETests`). FR.29.14 and FR.29.15: approved by the Architect's feedback 2026-11-18, built on branch `wpf/preview-window-polish` (merge `cd82193`). Accepted by the Architect in use on two monitors after both restarts (2026-11-18); the MANUAL rows of TC.PW.01 were not ticked one by one. Summary: `docs/features/preview-windows.md`.
