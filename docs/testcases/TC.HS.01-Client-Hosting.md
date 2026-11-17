# TC.HS.01 — WPF Client Hosting and Coexistence

Related requirements: FR.32.1–FR.32.8. Use cases: [UC.HS.01](/docs/usecases/UC.HS.01-Client-Hosting.md).

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
