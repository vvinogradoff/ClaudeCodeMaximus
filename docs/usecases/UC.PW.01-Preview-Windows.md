# UC.PW.01 — Preview Windows on Extra Monitors

Related requirements: FR.29.1–FR.29.13, FR.27.1–FR.27.9, FR.6.10 (see `docs/requirements/`). Test cases: [TC.PW.01](/docs/testcases/TC.PW.01-Preview-Windows.md).

## Actors
- **User**. **Tessyn** (WPF client). **Windows** (monitors, display changes).

## Preconditions (common)
- The WPF client runs; the preview groups are the defaults (FR.29.2) unless a use case says otherwise.
- A session whose output mentions a markdown file, a `.cpp` file, an image and some web links (Slack, GitHub, Linear, another site).

---

## UC.PW.01 — Turn preview windows on with a second monitor
**Main flow:** with one monitor the Monitor button after the day/night toggle is disabled and its tooltip says "Connect a second monitor to open previews in their own windows". The User connects a second monitor; within about a second the button is enabled and shows the checked look (the default is on). The mode is on.
**Alternative flows:** **A1** — The User switched the toggle off earlier: after plugging in, the button is enabled but unchecked; the mode stays off. **A2** — The User clicks the toggle off: every group window closes, pinned windows stay; the choice is remembered after a restart.
**Covers:** FR.29.1, FR.6.10.

## UC.PW.02 — Preview a file in its group's window
**Main flow:** with the mode on, the User Ctrl+clicks the path of a `.md` file in the transcript. A window "<file name> - Text and code" opens on the other monitor, with the markdown rendered; the main window keeps the keyboard focus. The User Ctrl+clicks another `.md` path: the same window shows the new file.
**Alternative flows:** **A1** — The window is minimized: it is restored. **A2** — The User clicks a Focus item's file reference or opens a link inside the markdown: the same rules apply (FR.29.4). **A3** — The policy refuses the file: a dismissible message in the window, no dialog. **A4** — The User toggles Source or clicks Open in default app in the title bar.
**Covers:** FR.29.4, FR.29.5, FR.29.14.

## UC.PW.03 — Two groups side by side
**Main flow:** the User has split `cpp h` out of "Text and code" (UC.PW.09). The User opens a `.md` file and a `.cpp` file: two windows appear, one per group. The User arranges them next to each other. Opening another `.md` file changes only the `.md` window.
**Covers:** FR.29.2, FR.29.3, FR.29.5.

## UC.PW.04 — Place a group window once and get it back there
**Main flow:** the first `.md` window opens centred on the largest monitor that does not hold the main window, at 60 % of its work area. The User moves it to the left of that monitor and makes it taller. The User closes it. The next `.md` item opens in exactly that place and size, on the same monitor.
**Alternative flows:** **A1** — The User maximized the window: the next one opens maximized, on that monitor. **A2** — The remembered monitor is gone: the window opens centred on a monitor other than the main window's. **A3** — The User picks **Forget window positions**: the next window opens at the first-time position. **A4** — Windows moves a window because of a display change: that move is not remembered. **A5** — The User snaps the window to the right half of a monitor with Snap layouts (or drags it to a screen edge): the next window of the group opens in that half, at the same size, also when the monitors have different scales.
**Covers:** FR.29.5, FR.29.6, FR.29.2, FR.29.16.

## UC.PW.05 — Preview a web page and sign in once
**Main flow:** the User Ctrl+clicks a Slack link in the transcript. A window "<page title> - Slack" opens with the embedded browser and shows Slack's sign-in. The User signs in with the User's email code. Later the User opens another Slack link: the same window shows it, signed in. After a restart of Tessyn the sign-in is still there, and a GitHub link opens in the "Windows app" group's own window.
**Alternative flows:** **A1** — The page opens a sign-in popup: it opens as a popup of the same profile and closes itself when the page is done. **A2** — A page tries to open `slack://...`: nothing opens, the attempt is logged. **A3** — A page asks for notifications or the camera: denied silently. **A4** — The User downloads a file: the download goes to the Downloads folder and nothing opens. **A5** — Google refuses to sign in: the User uses the service's own sign-in, or **Open in system browser**. **A6** — The User types an address into the address box and presses Enter: the window opens it.
**Covers:** FR.27.1–FR.27.7, FR.27.9, FR.27.10, FR.29.4, FR.29.5.

## UC.PW.06 — Pin an item
**Main flow:** a "Linear" window shows an issue the User wants to keep. The User clicks **Pin**: the window shows the pinned state and loses its Auto-close button. The User opens another Linear issue: a **new** Linear window opens at the group's remembered place; the pinned one stays as it is. The User moves the pinned one: the group's placement does not change. The User closes it by hand.
**Alternative flows:** **A1** — The User turns the toggle off or unplugs the monitor: pinned windows stay. **A2** — The User quits Tessyn: pinned windows close and do not come back at the next start.
**Covers:** FR.29.7, FR.29.8, FR.29.10, FR.29.11.

## UC.PW.07 — Auto-close on a session switch
**Main flow:** the User clicks **Auto-close** in a "Text and code" window; the Settings tab shows the group's check box checked. The User selects another session in the tree: the window closes. With Auto-close off a window stays open when the User switches sessions.
**Alternative flows:** **A1** — The switch happens through Recent, Tags, the dropdown, a reference jump or Back: the same. **A2** — The window is pinned: it stays.
**Covers:** FR.29.7, FR.29.9.

## UC.PW.08 — Auto-close when the Focus item is resolved
**Main flow:** the group "Images" has Auto-close on. The User opens an image from a Focus ask's file reference; the window shows it. The User answers the ask: the item is no longer open and the window closes.
**Alternative flows:** **A1** — The item is dismissed or resolved by the session: the same. **A2** — The window is pinned: it stays.
**Covers:** FR.29.9.

## UC.PW.09 — Manage groups in Settings
**Main flow:** the User opens Settings → Previews and sees the eight default groups. The User edits "Text and code" to `md txt`, adds a group "Code" with `cpp h` and moves it up, ticks Auto-close for it and saves. Both groups now have their own windows.
**Alternative flows:** **A1** — A pattern that is not an extension or an http(s) URL pattern (for example `*.md` or `ftp://x`): Save refuses and names the group and the pattern. **A2** — An empty group name: refused. **A3** — The User closes Settings with unsaved changes in this tab: the close guard asks (FR.4.7). **A4** — **Reset to defaults**: the eight defaults are back in the tab; Save keeps them, Discard leaves the old list. **A5** — **Forget window positions**: after confirming, every group window opens at the first-time position again. **A6** — A URL matches `https://hardstop.slack.com/*` and `https://*`: the Slack group wins whatever the order. A tie is decided by the list order.
**Covers:** FR.29.2, FR.29.3, FR.4.7.

## UC.PW.10 — A monitor is unplugged and plugged in again
**Main flow:** the mode is on with a group window and a pinned window open. The User unplugs the second monitor: the toggle becomes disabled, the group window closes, the pinned window moves to the remaining monitor and stays. Items now open as without this feature. The User plugs the monitor in again: the toggle is enabled and checked again; nothing reopens; the next `.md` item opens in its group window at the remembered place.
**Covers:** FR.29.10, FR.29.6, FR.29.1.

## UC.PW.11 — An item without a group
**Main flow:** the User deletes the "Web" group and Ctrl+clicks a link to a site no other group matches: it opens in the system browser. A file with an extension no group lists (a `.json` file) opens in the preview pane; a video opens in the default media player.
**Covers:** FR.29.3, FR.29.4.

## UC.PW.12 — Mode off
**Main flow:** with one monitor, or with the toggle off, the User Ctrl+clicks a path and a link: the file opens in the preview pane and the link in the system browser, as before.
**Covers:** FR.29.1, FR.29.4, FR.26.3, FR.26.5.

## UC.PW.13 — Click sources
**Main flow:** with the mode on, the User opens an item from each source: a transcript path, a transcript link, a Focus file reference, a Focus url reference, a session tag chip, a link inside a markdown preview and a link inside a Focus card. Each opens in its group's window. The chip's URL policy is unchanged: a tag with a host other than Linear or GitHub does not open at all.
**Alternative flows:** **A1** — **Open in default app** and **Open in system browser** always act on the system.
**Covers:** FR.29.4, FR.24.7, FR.23.18.

## UC.PW.14 — Clear browser data
**Main flow:** the User picks **Clear browser data** in Settings → Previews and confirms. Cookies, storage and cache are deleted; the next web preview asks to sign in again.
**Alternative flows:** **A1** — The User cancels the question: nothing is deleted.
**Covers:** FR.27.8.

## UC.PW.15 — Preview a TIFF image and a CSV file
**Main flow:** the User opens a `.tiff` path: the Images window shows it as a picture. The User opens a `.csv` file: the CSV window shows the text.
**Alternative flows:** **A1** — A TIFF over 25 MB or damaged: the usual "Preview unavailable" message.
**Covers:** FR.29.12.

## UC.PW.16 — Restart: the preview windows reopen
**Main flow:** the User quits Tessyn with a group window and a pinned window open and starts it again. Once the main window is connected, both windows reopen with their items, without taking the keyboard focus: the group window at the group's remembered place, the pinned window pinned again at its own last place.
**Alternative flows:**
- **A1** — The preview windows mode is off at start (the toggle is off or only one monitor is present): only the pinned window reopens.
- **A2** — The file of a window was deleted (or is refused now, or its session or entry is gone) before the start: that window does not reopen; the log says why.
- **A3** — A window of an Auto-close group came from another session than the one the main window opens with: it does not reopen.
- **A4** — The User had closed a window before quitting: it does not reopen.
**Covers:** FR.29.11, FR.29.6, FR.29.8.
---

# Docked previews inside the main window (draft 2026-11-19, FR.29.17 to FR.29.26)

Preconditions: docked mode is active (one monitor, or the toggle off) unless a use case says otherwise.

## UC.PW.17 - Preview in a docked panel on one monitor
**Main flow:** with one monitor the User Ctrl+clicks the path of a `.md` file in the transcript. A panel "<file name> - Text and code" opens to the right of the session pane with the markdown rendered; the composer keeps the keyboard focus. The User Ctrl+clicks another `.md` path: the same panel shows the new file. A `.cpp` file and a Slack link open their own panels (the Slack panel shows the embedded browser with its bar).
**Alternative flows:** **A1** - The User closes the panel; the next `.md` item opens it again at its remembered place. **A2** - A file with no group opens in the default Preview panel; a link with no group opens in the system browser. **A3** - The policy refuses a file: a dismissible message in the panel, no dialog. **A4** - The toggle is off with two monitors: the same as with one monitor.
**Covers:** FR.29.17, FR.29.18.

## UC.PW.18 - Drag a panel to dock it or stack it as a tab
**Main flow:** the User drags the "Text and code" panel by its title; drop guides show the targets. The User drops it on the left edge of the session pane: it docks to the left. The User drags the "Images" panel onto its tab strip: both are tabs of one pane. The User drags a splitter to resize.
**Covers:** FR.29.19, FR.29.21.

## UC.PW.19 - Float a panel
**Main flow:** the User drags a panel out of the main window (or picks Float). It becomes a window of its own that stays part of the docking layout; the User moves it to another monitor and resizes it. The User drags it back into the docking area and it docks again. The web page in it was not reloaded.
**Alternative flows:** **A1** - A second monitor is plugged in with the toggle on: floating group panels close, windows take over (UC.PW.23).
**Covers:** FR.29.19, FR.29.21.

## UC.PW.20 - Auto-hide and slide out
**Main flow:** the User clicks the auto-hide button of the "Linear" panel: it collapses into a tab on the edge of the main window. The User clicks the tab: the panel slides out over the session pane (above the transcript). The User clicks the transcript: it slides away. The User toggles auto-hide again: the panel docks where it was.
**Alternative flows:** **A1** - A new Linear item arrives while the panel is collapsed: it slides out with the item, without taking the keyboard focus (FR.29.17, Open points 3). **A2** - The pointer is over the web page when the User clicks away: Open points 4.
**Covers:** FR.29.19, FR.29.17.

## UC.PW.21 - Pin and Auto-close in a panel
**Main flow:** the User clicks **Pin** in a "Linear" panel: it shows the pinned state and loses its Auto-close button. The User opens another Linear issue: a **new** Linear panel opens at the group's remembered place; the pinned one stays and can still be docked, floated or auto-hidden, without changing the group's layout. With **Auto-close** on in the "Text and code" panel, selecting another session closes the panel; a panel opened from a Focus reference closes when the item is answered.
**Alternative flows:** **A1** - A pinned panel never auto-closes. **A2** - The default Preview panel has neither button.
**Covers:** FR.29.20, FR.29.9.

## UC.PW.22 - The dock layout is remembered per group, apart from windows
**Main flow:** the User docks "Text and code" at the bottom at a chosen height, stacks "Images" with "Linear" as tabs, auto-hides "Slack", floats "CSV". The User closes the panels and opens the items again: each panel is back where it was. The User connects a second monitor, uses preview windows and moves them, then unplugs the monitor: the panels open in the layout from before, and the windows' placements are unchanged.
**Alternative flows:** **A1** - **Forget window positions** (with confirmation) also forgets the dock layout: the panels open at the first-time place (right, 40 %). **A2** - The remembered monitor of a floating panel is gone: it opens clamped on a monitor present.
**Covers:** FR.29.21, FR.29.16.

## UC.PW.23 - A second monitor is connected: windows take over
**Main flow:** with docked group panels and one pinned panel open, the User connects a second monitor with the toggle on: the group panels (also floating ones) and the default Preview panel close with their layout saved, the pinned panel stays docked. The next `.md` item opens in its group window on the other monitor. The User unplugs the monitor: group windows close, pinned windows stay, and the next items open in docked panels at the remembered layout. Nothing reopens by itself.
**Covers:** FR.29.23, FR.29.10.

## UC.PW.24 - Restart: the docked panels reopen
**Main flow:** the User quits Tessyn with two group panels (one docked, one auto-hidden) and a pinned panel open and starts it again. Once the main window is connected the panels reopen with their items, in their layout, without taking the keyboard focus; the web page reopens at its settled address.
**Alternative flows:** **A1** - The file of a panel was deleted (or is refused, or its entry is gone): it does not reopen, the log says why. **A2** - A panel of an Auto-close group came from another session than the one the main window opens with: it does not reopen. **A3** - The mode with external windows is active at start: only pinned panels reopen. **A4** - A panel the User had closed does not reopen.
**Covers:** FR.29.22, FR.29.11, FR.29.20.
