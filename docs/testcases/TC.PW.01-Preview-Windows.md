# TC.PW.01 — Preview Windows on Extra Monitors

Related requirements: FR.29.1–FR.29.13, FR.27.1–FR.27.9, FR.6.10. Use cases: [UC.PW.01](/docs/usecases/UC.PW.01-Preview-Windows.md).

**Legend.** AUTOMATED = unit tests (the pattern matcher, the placement planner, the monitor watcher with a scripted monitor list, the router with a fake window factory, the browser navigation policy, view models). E2E = WPF end-to-end through `tools\e2e\Run-WpfE2E.ps1` (NR.6) with `--test-window --test-monitors <n>` (FR.29.13); preview windows are off-screen and never activated. MANUAL = needs a real second monitor, a real sign-in or a real display change; run by the Architect.

E2E tests: `WpfPreviewWindowsToggleE2ETests`, `WpfPreviewWindowsRoutingE2ETests`, `WpfPreviewWindowsFocusE2ETests` (see report 06 for the row mapping). E2E rows without an E2E test yet: TC.PW.11, 21, 26, 31, 34, 49, and in TC.PW.16 the tag chip, markdown-preview link and Focus-card-text link (covered by unit tests).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.PW.01 | With one monitor the Monitor button after the day/night toggle is disabled and its tooltip is "Connect a second monitor to open previews in their own windows" | E2E | UC.PW.01 | FR.29.1, FR.6.10 |
| TC.PW.02 | With `--test-monitors 2` the button is enabled and checked (default on); a click unchecks it and the choice is saved in `previewWindows` | E2E | UC.PW.01 | FR.29.1, FR.6.10 |
| TC.PW.03 | The monitor watcher counts monitors at start and after a display change, debounced about 1 s; a burst of changes gives one notification | AUTOMATED | UC.PW.01, UC.PW.10 | FR.29.1 |
| TC.PW.04 | Plugging in a second monitor enables the toggle and applies the remembered choice (on by default, off when the user had switched it off) | MANUAL | UC.PW.01 | FR.29.1 |
| TC.PW.05 | Turning the toggle off closes every group window and keeps pinned windows | AUTOMATED, E2E | UC.PW.01, UC.PW.06 | FR.29.1, FR.29.8 |
| TC.PW.06 | The defaults are the eight groups of FR.29.2 in that order, auto-close off, Images = `jpg jpeg png gif webp bmp tif tiff`; no stored value means the defaults | AUTOMATED | UC.PW.09 | FR.29.2 |
| TC.PW.07 | The Previews tab lists the groups; Add, Remove, Move up and Move down change the list; Save writes the `previews` section | AUTOMATED, E2E | UC.PW.09 | FR.29.2 |
| TC.PW.08 | A pattern that is neither an extension nor an http(s) URL pattern (`*.md`, `ftp://x`, `md/x`), an empty name, two groups with the same name and a group without a valid pattern are refused on Save, with a message naming group and pattern; nothing is saved | AUTOMATED, E2E | UC.PW.09 | FR.29.2, FR.29.3 |
| TC.PW.09 | Patterns are separated by spaces or commas; `md` and `.md` mean the same; extensions and URLs match case-insensitively; `*` matches `/`, `?` and `#` | AUTOMATED | UC.PW.09 | FR.29.3 |
| TC.PW.10 | A URL that matches `https://hardstop.slack.com/*` and `https://*` goes to Slack whatever the list order; with equal specificity the group higher in the list wins | AUTOMATED | UC.PW.09 | FR.29.3 |
| TC.PW.11 | An item with no matching group opens as without this feature: a file in the pane, a video in the default player, a link in the system browser | AUTOMATED, E2E | UC.PW.11 | FR.29.3, FR.29.4 |
| TC.PW.12 | Reset to defaults puts the eight default groups into the tab as an unsaved edit; Save keeps them and drops the placements of groups that no longer exist; Discard changes nothing | AUTOMATED, E2E | UC.PW.09 | FR.29.2 |
| TC.PW.13 | Forget window positions asks first and deletes every remembered placement; the groups stay | AUTOMATED | UC.PW.04, UC.PW.09 | FR.29.2, FR.29.6 |
| TC.PW.14 | An unsaved change in the Previews tab triggers the Settings close guard | AUTOMATED, E2E | UC.PW.09 | FR.29.2, FR.4.7 |
| TC.PW.15 | Ctrl+click on a transcript path of a `.md` file opens it in the Text and code window; a plain click opens nothing | E2E | UC.PW.02 | FR.29.4, FR.26.6 |
| TC.PW.16 | A transcript web link, a Focus file reference, a Focus url reference, a session tag chip, a link in a markdown preview and a link in Focus card text each go to the matching group's window | AUTOMATED (router with a fake window factory), E2E | UC.PW.13 | FR.29.4 |
| TC.PW.17 | A video path goes to a window only when a group lists its extension; otherwise it opens in the default player | AUTOMATED | UC.PW.11 | FR.29.4, FR.26.6 |
| TC.PW.18 | A tag chip whose URL `SessionTagUrlPolicy` refuses opens nowhere, with the mode on or off | AUTOMATED | UC.PW.13 | FR.29.4, FR.24.7 |
| TC.PW.19 | Open in default app and Open in system browser are never routed to a preview window | AUTOMATED | UC.PW.13 | FR.29.4 |
| TC.PW.20 | With the mode off, or with one monitor, a path opens in the preview pane and a link in the system browser | AUTOMATED, E2E | UC.PW.12 | FR.29.4, FR.26.3, FR.26.5 |
| TC.PW.21 | A file the access rules refuse shows a dismissible message in the window and no dialog | E2E | UC.PW.02 | FR.29.4, FR.23.18 |
| TC.PW.22 | A second item of the group replaces the content of the open window; no second window appears | AUTOMATED, E2E | UC.PW.02 | FR.29.5 |
| TC.PW.23 | A minimized group window is restored by a new item; the main window keeps the keyboard focus | MANUAL | UC.PW.02 | FR.29.5 |
| TC.PW.24 | The title is `<item name> - <group name>`; for a web page it is the page title | AUTOMATED | UC.PW.02, UC.PW.05 | FR.29.5 |
| TC.PW.25 | A `.md` file and a `.cpp` file in two split groups give two windows; a new `.md` item changes only the `.md` window | AUTOMATED, E2E | UC.PW.03 | FR.29.5, FR.29.2 |
| TC.PW.26 | The file content of a window uses the pane's renderer: markdown with Source toggle, text, image, PDF hint, Open in default app, progress and Cancel; a closed window releases its renderer | AUTOMATED, E2E | UC.PW.02 | FR.29.5 |
| TC.PW.27 | The placement planner: first time, the largest monitor without the main window, centred, 60 % of its work area; one monitor only: the main window's monitor | AUTOMATED | UC.PW.04 | FR.29.5 |
| TC.PW.28 | The placement planner: a remembered placement is clamped to the monitors present; with the remembered monitor gone the window is centred on another monitor than the main window's with its size clamped | AUTOMATED | UC.PW.04 | FR.29.6 |
| TC.PW.29 | Moving, resizing, maximizing and restoring a group window saves its placement for the group after 500 ms (rectangle, maximized flag, monitor device name); the next window opens there | AUTOMATED, MANUAL (real second monitor) | UC.PW.04 | FR.29.6 |
| TC.PW.30 | A move forced by a display change, the minimized state and any change of a pinned window are not saved | AUTOMATED | UC.PW.04, UC.PW.06 | FR.29.6, FR.29.8 |
| TC.PW.31 | The title bar has Pin, Auto-close, Minimize, Maximize/Restore and Close; a drag moves the window and a double-click maximizes or restores; the file header or the browser bar sits under it | E2E | UC.PW.02 | FR.29.7 |
| TC.PW.32 | The Auto-close button flips the group's switch and saves it at once; the Settings tab shows the same value | AUTOMATED, E2E | UC.PW.07 | FR.29.7, FR.29.9 |
| TC.PW.33 | Pin detaches the window: the pinned state shows, Auto-close is hidden, there is no unpin; the group's next item opens a new window | AUTOMATED, E2E | UC.PW.06 | FR.29.7, FR.29.8 |
| TC.PW.34 | Content stretches with the window and the buttons keep their places | E2E | UC.PW.02 | FR.29.7 |
| TC.PW.35 | A pinned window never auto-closes and does not write its placement to the group | AUTOMATED | UC.PW.06, UC.PW.07 | FR.29.8, FR.29.9 |
| TC.PW.36 | With Auto-close on, selecting another session (tree, Recent, Tags, dropdown, reference jump, Back) closes the group window; with it off the window stays | AUTOMATED, E2E | UC.PW.07 | FR.29.9 |
| TC.PW.37 | With Auto-close on, a window opened from a Focus item's reference closes when the item is answered, resolved or dismissed; a pinned one stays | AUTOMATED, E2E | UC.PW.08 | FR.29.9 |
| TC.PW.38 | The last extra monitor leaves (scripted monitor list): the toggle is disabled, group windows close, their forced placement is not saved, pinned windows stay | AUTOMATED | UC.PW.10 | FR.29.10 |
| TC.PW.39 | Unplugging and plugging in a real monitor: the toggle follows, nothing reopens, the next item opens at the remembered place | MANUAL | UC.PW.10 | FR.29.10, FR.29.6 |
| TC.PW.40 | A layout change with two or more monitors still present closes nothing and does not save the moved places | AUTOMATED | UC.PW.10 | FR.29.10, FR.29.6 |
| TC.PW.41 | Closing Tessyn closes every preview window and remembers it; the list in `previewWindows` is saved whenever a window opens, closes, is pinned or navigates | AUTOMATED, E2E | UC.PW.16 | FR.29.11 |
| TC.PW.42 | A `.tif` and a `.tiff` file are decoded to PNG in the preview cache and shown in the pane and in the Images window; over 25 MB or damaged gives "Preview unavailable"; the transcript's inline images are unchanged | AUTOMATED, E2E | UC.PW.15 | FR.29.12 |
| TC.PW.43 | A `.csv` file keeps the text preview | AUTOMATED | UC.PW.15 | FR.29.12 |
| TC.PW.44 | `--test-monitors <n>` is accepted only with `--test-window`; with `--test-window` preview windows are off-screen, not in the taskbar, never activated and use the focus guard | AUTOMATED, E2E | UC.PW.01 | FR.29.13 |
| TC.PW.45 | The browser uses one persistent profile folder `%AppData%\TessynDesktop\browser\` (not in-private) created on the first web preview; the transcript and preview pages do not use it | AUTOMATED | UC.PW.05 | FR.27.1 |
| TC.PW.46 | The sign-in survives a restart and is shared by two web windows | MANUAL (real sign-in) | UC.PW.05 | FR.27.1 |
| TC.PW.47 | The navigation policy allows http and https and cancels and logs `slack:`, `mailto:`, `file:` and other schemes; nothing is handed to the OS | AUTOMATED | UC.PW.05 | FR.27.2 |
| TC.PW.48 | `target=_blank`, `window.open` and sign-in popups open a popup of the same profile that closes when the page closes it; the popup has no Pin and no Auto-close | AUTOMATED, MANUAL | UC.PW.05 | FR.27.3 |
| TC.PW.49 | The browser bar has Back, Forward, Reload/Stop, a read-only selectable address and Open in system browser; the page title is the window title | AUTOMATED, E2E | UC.PW.05 | FR.27.4 |
| TC.PW.50 | A download goes to the Downloads folder and nothing opens | MANUAL | UC.PW.05 | FR.27.5 |
| TC.PW.51 | Permission requests (notifications, camera, microphone, geolocation) are denied without a prompt | AUTOMATED | UC.PW.05 | FR.27.6 |
| TC.PW.52 | No host objects, no web messages, DevTools, status bar and password autosave are off; no script is injected | AUTOMATED | UC.PW.05 | FR.27.7 |
| TC.PW.53 | Clear browser data asks first, deletes cookies, storage and cache and signs out; Cancel deletes nothing | AUTOMATED, MANUAL | UC.PW.14 | FR.27.8, FR.29.2 |
| TC.PW.54 | Google's refusal in the embedded browser is documented; the service's own sign-in works and Open in system browser is always there | MANUAL | UC.PW.05 | FR.27.9 |
| TC.PW.55 | After a restart the group windows reopen at their group's placement and the pinned windows reopen pinned at their own last placement, with their items | AUTOMATED, E2E | UC.PW.16 | FR.29.11, FR.29.8 |
| TC.PW.56 | A web page window reopens at the address it showed when Tessyn closed | AUTOMATED | UC.PW.16 | FR.29.11 |
| TC.PW.57 | A file window reopens from its Focus `refId`; a transcript path is asked from the daemon again through a new `file.preview` | AUTOMATED | UC.PW.16 | FR.29.11 |
| TC.PW.58 | An item that cannot be opened (file gone or refused, session or entry gone) is skipped and the log says why | AUTOMATED | UC.PW.16 | FR.29.11 |
| TC.PW.59 | With the mode off at start only the pinned windows reopen | AUTOMATED | UC.PW.16 | FR.29.11, FR.29.8 |
| TC.PW.60 | A window of an auto-close group whose item came from another session than the one the main window opens with is not reopened | AUTOMATED | UC.PW.16 | FR.29.11, FR.29.9 |
| TC.PW.61 | A window the user closed before quitting is not reopened | AUTOMATED, E2E | UC.PW.16 | FR.29.11 |
| TC.PW.62 | Reopened windows do not take the keyboard focus from the main window | MANUAL | UC.PW.16 | FR.29.11 |
| TC.PW.63 | Under `--test-window` the list of open windows survives a restart in the test data root; placements are not saved there | E2E | UC.PW.16 | FR.29.11, FR.29.13 |
| TC.PW.64 | Only a settled same-site address is saved for a web window; sign-in parameters are never saved | AUTOMATED | UC.PW.16 | FR.29.11 |
| TC.PW.65 | A popup opened by a click in the page may take the focus; a script's popup never does; the 6th popup of a window is dropped and logged | AUTOMATED (the decision), MANUAL (real popups) | UC.PW.05 | FR.27.3 |
| TC.PW.66 | A TIFF over 100 megapixels is refused before decoding | AUTOMATED | UC.PW.15 | FR.29.12 |
| TC.PW.67 | Moving or resizing a pinned window saves the open-window list after 500 ms; its group's placement is untouched | AUTOMATED | UC.PW.06 | FR.29.8, FR.29.11 |
| TC.PW.68 | The resize border on all four edges and the corners is free of page content when the window is not maximized, and gone when it is maximized | AUTOMATED (the inset), MANUAL (the cursor) | UC.PW.04 | FR.29.15 |
| TC.PW.69 | A typed address opens on Enter; a bare host gets `https://`; `ftp://x` and `javascript:x` are refused with a message and nothing opens; Esc and losing the focus restore the page's address; typing is not overwritten by a navigation | AUTOMATED (unit; E2E for the editable box), MANUAL (Enter, Esc and focus loss in a real window: a test window never takes the keyboard focus, FR.29.13) | UC.PW.05 | FR.27.10 |
| TC.PW.70 | A file window has no header row; Source and Open in default app are title-bar icons left of the title, shown only when they apply, and work; the pane in the main window keeps its header | AUTOMATED (unit, E2E) | UC.PW.02 | FR.29.14 |
| TC.PW.71 | A window Windows arranged (Snap to a half or a quarter, Snap layouts) saves its actual rectangle, not the one from before the snap; the next window of the group opens exactly there | AUTOMATED (the capture with a fake Win32 seam), MANUAL (real Snap and Snap layouts) | UC.PW.04 | FR.29.16, FR.29.6 |
| TC.PW.72 | A placement saved on a monitor of one scale reopens on the same monitor, now of another scale, at exactly the remembered size; a `DpiChanged` during the restore applies the remembered rectangle again, a later one (the user drags across monitors) is left alone | AUTOMATED (the planner and the re-apply decision), MANUAL (two real monitors of different scales) | UC.PW.04 | FR.29.16 |
| TC.PW.73 | The remembered monitor is gone and the window opens on a monitor of another scale: the size is converted by the ratio of the two scales, then clamped; an older placement without a scale is clamped only | AUTOMATED | UC.PW.04 | FR.29.16 |
| TC.PW.74 | A maximized window remembers the actual normal rectangle it had before maximizing (also after a snap), and reopens maximized with that rectangle behind it; a pinned window's own last place follows the same rules | AUTOMATED | UC.PW.04, UC.PW.06 | FR.29.16, FR.29.11 |
| TC.PW.75 | The remembered monitor is still there but its scale was changed since saving: the size and position are converted by the ratio of the old and the new scale, so the window keeps its apparent size | AUTOMATED | UC.PW.04 | FR.29.16 |
| TC.PW.76 | A maximized window moved to another monitor (Win+Shift+Arrow) is remembered with the restored rectangle, scale and monitor it had together, never the rectangle of one monitor paired with another; a drag in the first moments of a restore is never undone | AUTOMATED (the capture, the restore phase), MANUAL (real monitors) | UC.PW.04 | FR.29.16 |
