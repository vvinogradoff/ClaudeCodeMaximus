# UC.HS.01 — WPF Client Hosting and Coexistence

Related requirements: FR.32.1–FR.32.8 (see `docs/requirements/`). Test cases: [TC.HS.01](/docs/testcases/TC.HS.01-Client-Hosting.md).

## Actors
- **User** (the Architect). **WPF client**, **Avalonia app**, **Background host**.

---

## UC.HS.01 — Daily start with the host setting on
**Main flow:** 1. The User starts `[Publish-WPF]\TessynDesktop.WPF.exe`. 2. No daemon is found; the Startup setting is on: "Starting the Tessyn daemon in the background…". 3. The host starts, publishes `daemon.json`, WPF connects. 4. WPF closing leaves the host running.
**Alternative flows:** **A1** — The host does not publish in 45 s: the banner explains. **A2** — A daemon exists: no host is started.
**Covers:** FR.32.1, FR.32.5, FR.32.8.

## UC.HS.02 — Avalonia first, WPF second
**Main flow:** the User starts Avalonia (it hosts the daemon), then WPF; WPF attaches, starts no host, shows no stale banner.
**Covers:** FR.32.6, FR.32.7.

## UC.HS.03 — WPF first, Avalonia second
**Main flow:** 1. Setting off: WPF shows "Waiting for Tessyn to start…". 2. The User starts Avalonia; WPF connects in seconds. 3. With a background host already running, Avalonia asks it to exit, waits up to 40 s and starts; WPF reconnects and registers its sound again.
**Alternative flows:** **A1** — A second visible Avalonia window is refused. **A2** — The host does not leave: the message names the tray Exit.
**Covers:** FR.32.5, FR.32.6, FR.32.2.

## UC.HS.04 — Update and restart
**Main flow:** 1. A newer WPF build exists; at start WPF updates `[Publish-WPF]` and restarts. 2. The host is older than the build: the banner offers **Restart daemon**; the User confirms (running sessions named); the host exits and a new one starts.
**Alternative flows:** **A1** — A failed copy is logged at the next start.
**Covers:** FR.32.3, FR.32.7.

## UC.HS.05 — Import Avalonia settings
**Main flow:** on the first start against a daemon serving `avalonia-ui`, a dialog offers Import; the answer is remembered.
**Covers:** FR.32.4.

## UC.HS.06 — Exit the background host
**Main flow:** the User picks *Exit Tessyn daemon* in the tray; with running sessions a question appears; confirming terminates them and shuts down, even if the termination throws.
**Covers:** FR.32.1.
