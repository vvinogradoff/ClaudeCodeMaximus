> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). New area, drafted 2026-11-18. FR.27 was reserved for the "embedded browser pane" by the WPF programme plan (item 3.2); it now specifies the **embedded browser of the web preview windows**. Use cases: [UC.PW.01](/docs/usecases/UC.PW.01-Preview-Windows.md). Test cases: [TC.PW.01](/docs/testcases/TC.PW.01-Preview-Windows.md). Related: FR.29 (the windows that host it), FR.26.3.

### FR.27 — Embedded Browser

The embedded browser is a Chromium browser (WebView2) with a **persistent session**. It is used by the **web preview windows** of FR.29: the user signs in once to Slack, GitHub, Linear and so on, and every web preview is signed in. The docked browser pane of the WPF programme plan (item 3.2) is **not part of this requirement**; only the windows are built. WPF only.

**FR.27.1 — Profile.** One persistent WebView2 user data folder, `%AppData%\TessynDesktop\browser\`, with its own browser environment that is created on the first web preview. It is **not** in-private: cookies, storage and sign-ins persist across restarts and are shared by every web preview window. The transcript and the file preview pages keep their own in-private sandbox and never see this profile.

**FR.27.2 — Navigation.** The page navigates freely over `http` and `https` (sign-in redirects, single sign-on). Any other scheme (`slack://`, `mailto:`, `file:` and so on) is cancelled and logged; nothing is handed to the operating system.

**FR.27.3 — New windows.** `target=_blank`, `window.open` and sign-in popups open in a **popup window of the same profile**, so flows that use `window.opener` work. The popup closes when the page closes it. A popup is not a preview window: it has no group, no Pin and no Auto-close. A popup takes the keyboard focus only when the user's click in the page opened it; a popup a script opens on its own does not. One window has at most 5 popups open; further requests are dropped and logged.

**FR.27.4 — Controls.** A browser bar under the window's title bar (FR.29.7): **Back**, **Forward**, **Reload/Stop**, the address (read-only, selectable) and **Open in system browser**. The page title is the window title (FR.29.5). **Open in system browser** opens the current address in the default browser; it is never routed back to a preview window.

**FR.27.5 — Downloads.** A download uses WebView2's own download UI and goes to the user's Downloads folder. Nothing is opened automatically.

**FR.27.6 — Permissions.** Requests for notifications, camera, microphone, geolocation and the like are **denied without a prompt**.

**FR.27.7 — Isolation.** No host objects, no web messages, DevTools off, status bar off, password autosave off. Tessyn never injects script into these pages.

**FR.27.8 — Clear browser data.** The **Clear browser data** button in Settings → Previews (FR.29.2) deletes the profile's cookies, storage and cache, which signs the user out everywhere. It asks first.

**FR.27.9 — Known limit.** Google may refuse to sign in inside an embedded browser ("This browser or app may not be secure"). Use the service's own sign-in: Slack email code or password, GitHub password and 2FA, Linear email code. **Open in system browser** is always available (FR.27.4).

Status: Specified and approved 2026-11-18; built 2026-11-18 on branch `wpf/preview-windows` (unit tests); manual checks on two real monitors pending.
