# UC.HS.01 — WPF Client Hosting and Coexistence

Related requirements: FR.32.1–FR.32.8 (see `docs/requirements/`). Test cases: [TC.HS.01](/docs/testcases/TC.HS.01-Client-Hosting.md).

## Actors
- **User** (the Architect). **WPF client**, **Background host** (the daemon host, `TessynDesktop.exe --background`).

---

## UC.HS.01 — Daily start with the host setting on
**Main flow:** 1. The User starts `[Publish-WPF]\TessynDesktop.WPF.exe`. 2. No daemon is found; the Startup setting is on: "Starting the Tessyn daemon in the background…". 3. The host starts, publishes `daemon.json`, WPF connects. 4. WPF closing leaves the host running.
**Alternative flows:** **A1** — The host does not publish in 45 s: the banner explains. **A2** — A daemon exists: no host is started.
**Covers:** FR.32.1, FR.32.5, FR.32.8.

## UC.HS.02 — WITHDRAWN 2026-11-17 (dropped with the previous desktop client)
Was: the previous client hosting the daemon in its window while WPF attached. The part that still holds is in UC.HS.01 A2: when a daemon exists, WPF attaches and starts no host.
**Covers:** FR.32.6 (withdrawn part).

## UC.HS.03 — WPF first, daemon later
**Main flow:** 1. Setting off: WPF shows "Waiting for Tessyn to start…". 2. The User starts a host by hand (`TessynDesktop.exe --background`); WPF connects in seconds. 3. After a daemon restart WPF reconnects by itself and registers its sound again.
**Alternative flows:** **A1** — A second host exits at once (FR.32.2). **(Withdrawn 2026-11-17: the former A1 "a second visible window is refused", A2 "the host does not leave", and the takeover of a background host by a visible window.)**
**Covers:** FR.32.5, FR.32.6, FR.32.2.

## UC.HS.04 — Update and restart
**Main flow:** 1. A newer WPF build exists; at start WPF updates `[Publish-WPF]` and restarts. 2. The host is older than the build: the banner offers **Restart daemon**; the User confirms (running sessions named); the host exits and a new one starts.
**Alternative flows:** **A1** — A failed copy is logged at the next start.
**Covers:** FR.32.3, FR.32.7.

## UC.HS.05 — WITHDRAWN 2026-11-17 (decision A47). Import the previous client's settings
**Main flow (withdrawn):** on the first start against a daemon serving the previous client's settings section, a dialog offered Import; the answer was remembered. The import is removed with the previous desktop client; an old `wpf.json` with its sections still loads without error (TC.HS.13).
**Covers:** FR.32.4 (withdrawn).

## UC.HS.06 — Exit the background host
**Main flow:** the User picks *Exit Tessyn daemon* in the tray; with running sessions a question appears; confirming terminates them and shuts down, even if the termination throws.
**Covers:** FR.32.1.
