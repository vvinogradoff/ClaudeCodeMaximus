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

Status: Specified and approved 2026-11-18; built 2026-11-18 on branch `wpf/preview-windows` (merge `1d07de9`; unit tests, WPF E2E `WpfPreviewWindows*E2ETests`). FR.29.14 and FR.29.15: approved by the Architect's feedback 2026-11-18, built on branch `wpf/preview-window-polish` (merge `cd82193`). Accepted by the Architect in use on two monitors after both restarts (2026-11-18); the MANUAL rows of TC.PW.01 were not ticked one by one. Summary: `docs/features/preview-windows.md`.
