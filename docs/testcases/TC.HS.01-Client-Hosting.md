# TC.HS.01 — WPF Client Hosting and Coexistence

Related requirements: FR.32.1–FR.32.15. Use cases: [UC.HS.01](/docs/usecases/UC.HS.01-Client-Hosting.md).

**Legend.** AUTOMATED = unit / integration (WPF.Tests, Core tests, real PowerShell for scripts); MANUAL = checked by hand in the real apps (they were not started in the work reports; the checklist is in `.wpf-work\471-side-by-side.md` section 7 and `458-wpf-daily-switch.md` section 3).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.HS.01 | One host mutex: a second host exits (the "second visible window is refused" check is WITHDRAWN 2026-11-17) | AUTOMATED | UC.HS.03 | FR.32.2 |
| TC.HS.02 | Launcher: never with a daemon found, never while the mutex is held, once per 60 s, copy off the lock | AUTOMATED | UC.HS.01 | FR.32.5 |
| TC.HS.03 | Setting off: outcome Disabled, banner "Waiting for Tessyn…", no timeout | AUTOMATED | UC.HS.03 | FR.32.5 |
| TC.HS.04 | Starting banner ends after 45 s with the detail; unreadable token text | AUTOMATED | UC.HS.01 | FR.32.8 |
| TC.HS.05 | Self-update script (real PowerShell, folder named `[Publish-WPF]`): copies, keeps `host\`, restarts; failed copy recorded and logged | AUTOMATED | UC.HS.04 | FR.32.3 |
| TC.HS.06 | Self-update and host start are off for `--test-window`, `--data-root`, `--daemon-file`, build output | AUTOMATED | UC.HS.04 | FR.32.3 |
| TC.HS.07 | Stale banner: Core-only change seen; no banner for a daemon running from elsewhere, unknown build, no-op launcher | AUTOMATED | UC.HS.04 | FR.32.7 |
| TC.HS.08 | **WITHDRAWN 2026-11-17.** Takeover of a background host by a visible window (outcomes Acquired, NotABackgroundHost, DidNotExit); dropped with the previous desktop client | WITHDRAWN | UC.HS.02 | FR.32.6 |
| TC.HS.09 | Reconnect to a new run after a daemon restart; sound registered again; sound never double | AUTOMATED | UC.HS.03 | FR.32.6 |
| TC.HS.10 | Host exit: confirmation, terminate, shutdown; throwing terminate still shuts down; one tray question | AUTOMATED | UC.HS.06 | FR.32.1 |
| TC.HS.11 | **WITHDRAWN 2026-11-17 (decision A47).** Import dialog (previous client's settings) once; answer remembered; no second dialog on reconnect; removed with the import | WITHDRAWN | UC.HS.05 | FR.32.4 |
| TC.HS.12 | WPF started before the daemon connects through a real `daemon.json` and token (E2E) | E2E | UC.HS.03 | FR.32.6 |
| TC.HS.13 | Real windowless host, tray icon, tray confirm and exit listener | MANUAL | UC.HS.06 | FR.32.1 |
| TC.HS.14 | An old `wpf.json` with the removed `imports` and `avaloniaImport` sections loads without error; other sections are kept | AUTOMATED | UC.HS.05 | FR.32.4 |
| TC.HS.15 | The built host exe, isolated (`--data-root`, `--daemon-file`, `--instance`): starts, publishes `daemon.json`, answers `tree.snapshot`, ends cleanly on the exit event and removes `daemon.json`; a start without `--background` runs as the host and logs a note; a second start with the same instance ends at once | E2E | UC.HS.06 | FR.32.1, FR.32.2 |
| TC.HS.16 | Same commit in the running daemon and the newest build: no banner | AUTOMATED | UC.HS.07 | FR.32.9 |
| TC.HS.17 | Different commits with no change in the daemon source paths (a WPF-only or docs commit, in a real temporary git repo): no banner | AUTOMATED | UC.HS.07 | FR.32.9 |
| TC.HS.18 | A change in Core, Protocol, the host project or a build file between the commits: the banner | AUTOMATED | UC.HS.07 | FR.32.9 |
| TC.HS.19 | Uncommitted changes in a daemon source path: the banner | AUTOMATED | UC.HS.07 | FR.32.9 |
| TC.HS.20 | The daemon source paths come from the host project references, transitively | AUTOMATED | UC.HS.07 | FR.32.9 |
| TC.HS.21 | Unknown commit, no git, a commit missing from the repo, or a git timeout: the time rule of FR.32.7 decides | AUTOMATED | UC.HS.07 | FR.32.9 |
| TC.HS.22 | The check runs off the UI thread and logs both commits and the decision | AUTOMATED | UC.HS.07 | FR.32.9 |
| TC.HS.23 | *Start daemon elevated*: default off, stored in `daemonHost`, an older settings file without it reads as off, the Settings close guard (FR.4.7) sees an unsaved change | AUTOMATED | UC.HS.08 | FR.32.10 |
| TC.HS.24 | Launcher: with the setting on the host is started through the administrator prompt (`runas`, no pipes), off it starts as before; the newest build is copied first in both ways | AUTOMATED | UC.HS.08 | FR.32.10 |
| TC.HS.25 | Declined prompt (`ERROR_CANCELLED`) and a failed elevated start: a normal start, the notice text, no second prompt from the connect loop until the next start; Restart daemon elevated prompts again | AUTOMATED | UC.HS.08 | FR.32.13 |
| TC.HS.26 | Restart daemon elevated and Restart daemon normal: the confirmation names the running sessions; Cancel changes nothing; exit, wait, start the chosen way, reconnect; the connect loop starts no host meanwhile; the client does not restart; works with *Start the background daemon* off | AUTOMATED | UC.HS.08 | FR.32.11 |
| TC.HS.27 | The stale banner's Restart daemon keeps the way the daemon runs (elevated comes back elevated, normal comes back normal) | AUTOMATED | UC.HS.08 | FR.32.11 |
| TC.HS.28 | `client.hello` `elevated`: the Startup status text for both values, nothing without it (older daemon) or without a connection; the banner shows only with the setting on and a normal daemon, and goes when either changes | AUTOMATED | UC.HS.08 | FR.32.12 |
| TC.HS.29 | Elevated host and normal WPF: the exit event accepts a signal from a normal process (its security descriptor allows the User's normal processes), and the build check reads the host folder without reading the process | AUTOMATED | UC.HS.08 | FR.32.14 |
| TC.HS.30 | Real elevation (the administrator prompt cannot be scripted): from a normal WPF, Restart daemon elevated shows the prompt; after approval a session's `whoami /groups` shows `High Mandatory Level`; files dropped from Explorer are accepted; the stale banner appears for an elevated host; the tray icon and *Exit Tessyn daemon* work; Restart daemon normal returns `Medium Mandatory Level`; declining the prompt gives the notice and a normal daemon | MANUAL | UC.HS.08 | FR.32.10–FR.32.14 |
| TC.HS.31 | An elevated WPF shows the banner "This window runs as administrator. …" for the whole run and a normal one never does (an elevation seam); the start is logged with the elevation | AUTOMATED | UC.HS.08 | FR.32.15 |
| TC.HS.32 | Real: WPF started from a shortcut with *Run as administrator* shows the banner; started normally, it does not | MANUAL | UC.HS.08 | FR.32.15 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.32.1 | TC.HS.10, 13, 15 |
| FR.32.2 | TC.HS.01 |
| FR.32.3 | TC.HS.05, 06 |
| FR.32.4 | TC.HS.11 (withdrawn), 14 |
| FR.32.5 | TC.HS.02, 03 |
| FR.32.6 | TC.HS.08 (withdrawn), 09, 12 |
| FR.32.7 | TC.HS.07 |
| FR.32.8 | TC.HS.04 |
| FR.32.9 | TC.HS.16–22 |
| FR.32.10 | TC.HS.23, 24, 30 |
| FR.32.11 | TC.HS.26, 27, 30 |
| FR.32.12 | TC.HS.28, 30 |
| FR.32.13 | TC.HS.25, 30 |
| FR.32.14 | TC.HS.29, 30 |
| FR.32.15 | TC.HS.31, 32 |
