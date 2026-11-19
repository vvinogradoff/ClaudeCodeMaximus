# Preview windows and the embedded browser — as built

> Draft for Architect review, 2026-11-18. Author: Developer/Claude.
> Requirements: FR.29 (preview windows), FR.27 (embedded browser), FR.6.10 (the title bar toggle). Use cases: UC.PW.01. Test cases: TC.PW.01.
> Built on branch `wpf/preview-windows`, merged to master as `1d07de9`. Review fixes are part of that merge.
> Items marked **follow-up of 2026-11-18** are the Architect's feedback after first use (FR.27.10, FR.29.14, FR.29.15). They were built on branch `wpf/preview-window-polish`, reviewed separately and merged to master the same evening.

## 1. What it is

With two or more monitors, an item that has a preview opens in its own window instead of the preview pane or the system browser. The item can be a `.md` file, an image, or a web link. The windows can be moved to any monitor. Each **preview group** has its own window. The window remembers where the user put it. WPF only: no change to the daemon, TDCP, the web client or MCP.

What the user sees:

- **Monitor toggle** in the title bar, after the day/night toggle (FR.29.1). Enabled only with two or more monitors. Default on. Turning it off closes the group windows.
- **Preview groups** in Settings → Previews (FR.29.2). A group has a name, patterns, and an Auto-close switch. Eight defaults: Images, Text and code, CSV, Slack, Windows app, Backend and frontend, Linear, Web.
- **Patterns** (FR.29.3): a file extension (`md`) or a URL with `*` as a wildcard. The most specific match wins; a tie goes to the group higher in the list.
- **Routing** (FR.29.4): five click sources go to a window when a group matches. These are a transcript path, a transcript link, a Focus file or url reference, a session tag chip (and its "Open in browser" entry), and a link inside a markdown preview or in the text of a Focus card.
- **One window per group** (FR.29.5). A new item of the group replaces the content. A minimized window is restored. The window is raised without taking the keyboard focus.
- **Placement** (FR.29.6). The first window opens on the largest monitor that does not hold the main window, centred, at 60 % of its work area. After that it opens where the user left it.
- **Pin** (FR.29.7, FR.29.8). Detaches the window from its group. One-way. A pinned window never auto-closes and its moves are not written to the group.
- **Auto-close** (FR.29.9). Per group, default off. The window closes when the main window shows another session, or when the Focus item it was opened from is no longer open.
- **Restart** (FR.29.11). Open windows reopen with their items. A web page reopens at the address it showed.
- **Monitors change** (FR.29.10). Losing the last extra monitor closes the group windows and keeps the pinned ones. Plugging a monitor in enables the toggle again; nothing reopens by itself.
- **TIFF** (FR.29.12). `.tif` and `.tiff` are decoded by Windows Imaging Component and shown as PNG, in the pane and in windows. CSV keeps the text view.
- **Embedded browser** (FR.27). One persistent, signed-in Chromium profile for all web windows. Free navigation over http and https. Popups of the same profile. Browser bar with Back, Forward, Reload/Stop, the address and "Open in system browser". Downloads go to the Downloads folder. Permission requests are denied. "Clear browser data" in Settings signs the user out.
- **Follow-up of 2026-11-18:**
  - Every window edge and corner can be used to resize (FR.29.15).
  - The address box is editable; Enter opens the address in this window, never in another group's (FR.27.10). A bare host gets `https://`. Anything else than http or https is refused with a message, also a non-web scheme that looks like `host:port` (`javascript:1`, `mailto:5`). Esc or leaving the box puts back the page's address and clears the message.
  - In a file window there is no header row. **Source** and **Open in default app** are icons in the title bar, left of the title (FR.29.14).

## 2. Key decisions and why

### The Architect's answers of 2026-11-18

| Question | Answer | Why it matters |
|---|---|---|
| Several groups match | The most specific pattern wins (most characters other than `*`); a tie goes to the higher group | `https://hardstop.slack.com/*` beats `https://*` whatever the order |
| After a restart | All preview windows reopen with their last items | The work layout survives a restart |
| Pin | One-way; no unpin | A window with `credentials.md` must never be reused for the next `.md` |
| Click sources | All five are routed | The user does not have to know which click is "special" |
| CSV | Stays a text preview | A table view is not part of this work |

### Design calls

- **WPF only, no daemon change.** The layout belongs to the client (P7). Files are read through the same daemon calls the preview pane uses (`file.preview`, the Focus file reference).
- **Settings in `wpf.json`.** Two sections: `previews` (the group list; no value means the defaults) and `previewWindows` (the toggle, the placement per group id, and the list of open windows).
- **One browser profile**, `%AppData%\TessynDesktop\browser\`, in a WebView2 environment of its own, created on the first web preview. Not in-private. The transcript and the file preview pages keep their in-private sandbox and never see it.
- **No read handle is stored.** A restored file is opened again from its Focus reference, or by asking the daemon for the transcript path again. A 1-byte read proves the daemon still serves it.
- **Web addresses are saved only when settled.** The page must have finished loading and stayed for one second. The address must be on the same site as the one the item was opened on. A query or fragment with a sign-in parameter (code, state, token and similar) is dropped whole. This keeps sign-in secrets out of `wpf.json`.
- **Popups take the focus only after a user click.** A popup a script opens on its own does not. A window has at most 5 popups; more are dropped and logged.
- **Windows are not owned by the main window and never activated.** Each has its own taskbar button. Raising uses `SW_SHOWNOACTIVATE`.
- **Mode off is the old behaviour.** An item that matches no group, and every item while the mode is off, opens as before. The buttons "Open in default app" and "Open in system browser" are never routed.

## 3. How it works

### The path of a click

```
click (transcript path/link, Focus ref, tag chip, link in a markdown preview)
   |
   |-- links: IUrlLauncher  = PreviewRoutingUrlLauncher (decorator)
   |-- files / Focus refs: decision points in SessionPaneViewModel
   v
IPreviewRouter  (PreviewRouter)
   |  mode active? group matches? (PreviewGroupMatcher)   no -> old path
   v
IPreviewWindowFactory (PreviewWindowFactory)  -> PreviewWindow + PreviewWindowViewModel
   v
file: FilePreviewPane (same renderer and limits as the pane)
web:  BrowserPreviewView + BrowserSurface (WebView2)
```

`PreviewRoutingUrlLauncher` wraps the system launcher and the URL policy. Links go through it. Files and Focus references are decided in `SessionPaneViewModel` (`PreviewLocalPathAsync`, the file reference, the url reference), because they need the session's client.

### The router

`PreviewRouter` has no WPF types. It owns every window, knows the mode (toggle on and two or more monitors), and keeps the list of open windows. It reacts to:

- `OnSessionShown` — closes auto-close windows opened from another session. `OnSessionShown(null)` closes nothing: "no session shown" is not proof that the session changed.
- `OnFocusItemsClosed` — closes auto-close windows opened from a Focus item that is now closed.
- Settings and monitor changes — mode on/off, group renamed or removed.
- `CloseAllForExit` — called from `MainWindow.Closed`, after the unsaved-changes guard (FR.4.7).

### The open-window list and the restore

- The list is saved whenever a window opens, closes, is pinned, a pinned window is moved or resized (500 ms after the last change), or a web page settles at a new address. A crash loses nothing.
- At start, `RestoreAsync` runs once the main window is connected to the daemon. Each saved window is checked (its file is read again) and reopened without taking the focus.
- While it restores, the saved list stays complete, so a window the user opens during the restore does not lose the others.
- Pinned windows always reopen. Group windows reopen only when the mode is active. A group window of an auto-close group is not reopened when its item came from another session than the one the main window opens with.
- A window whose item cannot be opened any more is skipped; the log says why.

### Placement

- **`PreviewPlacementPlanner`** decides where a window opens: the remembered rectangle clamped to the monitors present; the monitor found by device name, or by the origin of its work area when Windows renamed it; centred on another monitor with the size clamped when the monitor is gone; first time at 60 % on the largest non-main monitor.
- **`PreviewPlacementTracker`** (no WPF types, `TimeProvider` timer) decides what is remembered. It saves 500 ms after the last move, resize, maximize or restore. It saves nothing before the first frame, while minimized, while Windows is settling a monitor change (`IsSettling`), in test mode, or for a pinned window (a pinned window raises an event instead, so the router can save its own place for the restart).
- Placements of groups that no longer exist are dropped on Save and ignored on write.

### Monitors

`IMonitorWatcher` counts the monitors at start and after each display change (debounced about 1 s, with a settle period). `Win32MonitorWatcher` is the real one. `ScriptedMonitorWatcher` is used under `--test-monitors`. It reports a fixed count and never fires `Changed`.

### The browser

- `BrowserEnvironmentProvider` (singleton) creates the one WebView2 environment and profile.
- Each web window has its own `BrowserSurface`. It applies `BrowserNavigationPolicy` (http and https only; other schemes are cancelled and logged), denies permissions, turns DevTools, host objects, web messages, the status bar and password autosave off, and decides popups with `BrowserPopupPolicy`.
- `BrowserPreviewViewModel` holds the bar state. A new page in an open web window navigates the same browser.
- `BrowserDataCleaner` clears cookies, storage and cache through a hidden off-screen window. Settings asks first.

### TIFF

`FilePreviewLoader` writes the TIFF to the cache. `WicTiffConverter` reads the first frame size, refuses more than 100 megapixels before decoding, decodes, and writes a PNG to a new cache folder. The 25 MB image limit applies to the TIFF. A failure shows "This TIFF image could not be decoded."

## 4. Code map

All paths are under `code/TessynDesktop.WPF/` unless stated.

| File | Role |
|---|---|
| `Services/PreviewRouter.cs` (+ `IPreviewRouter`) | Owns windows and mode; routes files and URLs; auto-close; restore; saves the open-window list |
| `Services/PreviewRoutingUrlLauncher.cs` | `IUrlLauncher` decorator that asks the router before the system launcher |
| `ViewModels/SessionPaneViewModel.PreviewWindows.cs` | Pane side: hooks the router, session-shown and Focus-closed events, restore call |
| `Services/PreviewGroupMatcher.cs` (+ `PreviewPattern*`, `PreviewGroupMatch`) | Parses patterns, matches files and URLs, most specific wins |
| `Services/PreviewSettingsService.cs` (+ `IPreviewSettingsService`) | Groups, toggle, placements and open-window list in `wpf.json` |
| `Services/PreviewPlacementPlanner.cs` | Where a window opens |
| `Services/PreviewPlacementTracker.cs` | What is remembered, and when (500 ms) |
| `Services/PreviewAddressSanitizer.cs` | Same-site check and sign-in parameter removal before saving an address |
| `Services/Win32MonitorWatcher.cs`, `ScriptedMonitorWatcher.cs`, `IMonitorWatcher.cs` | Monitor count and changes (real and test) |
| `Services/BrowserNavigationPolicy.cs`, `BrowserPopupPolicy.cs`, `BrowserPopupDecision.cs` | Pure rules: schemes; popup limit and focus |
| `Services/WicTiffConverter.cs` (+ `ITiffConverter`) | TIFF to PNG with the pixel limit |
| `Views/PreviewWindow.xaml(.cs)` | The window: custom chrome, Pin, Auto-close, placement events |
| `Views/PreviewWindowHandle.cs`, `PreviewWindowFactory.cs` | The router's view of a window; creates windows |
| `Views/PreviewContentFactory.cs` (+ `IPreviewContentFactory`) | Builds the file pane or the browser view for a window |
| `ViewModels/PreviewWindowViewModel.cs` | Title, Pin and Auto-close state, address settle timer |
| `Views/BrowserSurface.cs`, `BrowserPreviewView.xaml(.cs)`, `BrowserPopupWindow` | WebView2 wrapper, the browser bar, popups |
| `Views/BrowserEnvironmentProvider.cs`, `BrowserDataCleaner.cs` | The one environment and profile; clearing it |
| `ViewModels/BrowserPreviewViewModel.cs` | Browser bar state and commands, the typeable address (editing, Enter, Esc) |
| `Services/BrowserAddressInput.cs` | Turns a typed address into the address to open: `https://` for a bare host, http and https only, the refused schemes in `Constants.Browser.RefusedSchemes` |
| `ViewModels/Settings/PreviewsSectionViewModel.cs`, `Views/Settings/PreviewsSettingsView.xaml` | Settings → Previews |
| `State/PreviewSettings.cs`, `PreviewGroupModel.cs`, `PreviewWindowsSettings.cs`, `PreviewPlacementModel.cs`, `PreviewItemModel.cs`, `OpenPreviewWindowModel.cs` | Persisted shapes of the two `wpf.json` sections |
| `Constants.Previews.cs`, `Constants.PreviewWindow.cs`, `Constants.Browser.cs` | Defaults, sizes, texts, the sign-in parameter list |
| `ServiceRegistration.cs` (`RegisterPreviewWindows`) | DI wiring; `IUrlLauncher` is the routing decorator |

## 5. Testing

**Unit tests** (`code/TessynDesktop.WPF.Tests`):
`PreviewGroupMatcherTests`, `PreviewPlacementPlannerTests`, `PreviewPlacementTrackerTests`, `PreviewSettingsServiceTests`, `PreviewRouterTests`, `PreviewRoutingUrlLauncherTests`, `PreviewRoutingSupportTests`, `SessionPanePreviewWindowsTests`, `PreviewWindowViewModelTests`, `PreviewWindowViewTests` (also the title bar toggle), `PreviewAddressSanitizerTests`, `PreviewsSectionTests`, `PreviewUrlSlashTests`, `TiffPreviewTests`, `BrowserNavigationPolicyTests`, `BrowserPopupPolicyTests`, `BrowserPreviewViewModelTests`, `BrowserPreviewViewTests`, `PreviewContainerTests`. Fakes: `FakePreviewWindowFactory`, `FakePreviewWindowHandle`, `FakePreviewSettingsService`, `FakePreviewContentFactory`, `FakePreviewRouter`, `ManualTimeProvider`.

**E2E** (`code/TessynDesktop.WPF.E2E.Tests`): `WpfPreviewWindowsToggleE2ETests`, `WpfPreviewWindowsRoutingE2ETests`, `WpfPreviewWindowsFocusE2ETests`, `WpfPreviewWindowsPolishE2ETests` (the title-bar file controls and the editable address), with `PreviewWindowsHarness`. Run only through `tools\e2e\Run-WpfE2E.ps1` (NR.6).

**The test seam.**
- `--test-window --test-monitors <n>` makes the app see *n* monitors (1 to 4; accepted only with `--test-window`). A one-screen machine can then enable the toggle.
- The harness pre-writes the `previews` and `previewWindows` sections of `wpf.json` in a temporary data root. The Settings list is virtualized, so a test writes two groups to make the added row appear.
- Under `--test-window` the windows are off-screen, not in the taskbar, never activated, and use the test focus guard. The open-window list is kept (so a restart can be tested). Placements are not saved.
- A web group is a local `127.0.0.1` group served by a test `HttpListener`.

**Not covered by an E2E test:** the tag chip, the link in a markdown preview and the link in a Focus card as click sources (TC.PW.16); content and drag checks (TC.PW.11, 21, 26, 31, 34); the browser bar (TC.PW.49); the real WebView2 popup wiring (the decision itself is unit-tested); Enter and Esc in the address box (a test window never takes the keyboard focus, so view tests raise the key and focus events instead, TC.PW.69).

**Manual only** (two real monitors, real sign-in): TC.PW.04, 23, 29, 39, 46, 48, 50, 53, 54, 62, and the DPI and focus checks. These were pending at the time of writing.

**No automated tests:** `BrowserSurface` (needs WebView2) and `Win32MonitorWatcher` (needs real timers and display events).

## 6. Gotchas for later work

- **The window factory resolves lazily.** The routing launcher is the registered `IUrlLauncher`, and windows need an `IUrlLauncher` too. Resolving it eagerly creates a DI cycle. The router must not depend on `IUrlLauncher`.
- **WebView2 child windows cover the resize border.** They are HWNDs and take the mouse over the 6 px border, so the window cannot be resized there. The page area is inset by the chrome's `ResizeBorderThickness` while the window is not maximized (FR.29.15, follow-up of 2026-11-18). Keep the inset and the thickness tied to one constant, and keep it consistent with the maximized margin (`FitMaximized`).
- **Test windows never take the keyboard focus.** Anything that needs typed keys (Enter in the address box) cannot be driven end to end; raise the routed key and focus events in a view test.
- **Toggle buttons need two-way bindings.** UI Automation's Toggle pattern flips `IsChecked` without running the command. The Monitor toggle, Pin and Auto-close therefore bind `IsChecked` two-way to settable view model properties (`IsPreviewWindowsOn`, `PinChecked`, `AutoCloseChecked`), and also keep their commands.
- **Never activate a preview window.** `ShowActivated=false`, `SW_SHOWNOACTIVATE` and `SWP_NOACTIVATE`. A minimized window must come back to its previous state, maximized included. Only a popup opened by a user click may take the focus.
- **The test focus guard.** Test windows once took the foreground every few seconds. Preview windows use the same guard as the main test window; keep it when adding window types (the browser data cleaner window has none; it is brief and off-screen).
- **Restored windows use the client of the first session pane.** Harmless today (one daemon), but it is an assumption if a second client appears.
- **Rule: no session shown is not a session change.** `OnSessionShown(null)` closes nothing.
- **The sign-in parameter list lives in `Constants.PreviewWindow`** (`SignInParameters`, `SignInFragmentMarkers`). Extend it there if a service puts a secret in the address under another name.
- **`BrowserPreviewView` cannot re-attach.** A new browser content needs a new view; a new page in an open window reuses the browser and calls `Navigate`.
- **Settings tab visibility.** The Previews tab is shown only when `IPreviewSettingsService` resolves; keep it registered.
- **Google may refuse to sign in** in an embedded browser (FR.27.9). The service's own sign-in works; "Open in system browser" is always there.
