# UC.HS.01 — WPF Client Hosting and Coexistence

Related requirements: FR.32.1–FR.32.14 (see `docs/requirements/`). Test cases: [TC.HS.01](/docs/testcases/TC.HS.01-Client-Hosting.md).

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

## UC.HS.07 — A rebuild without daemon changes shows no stale banner
**Actor:** the Architect. **Precondition:** WPF runs from `[Publish-WPF]` with its daemon from `host\`.
**Main flow:** 1. A WPF-only change (or a docs commit) is merged and built. 2. WPF updates and reconnects. 3. No "The daemon is older than the build." banner appears, because nothing in the daemon's source changed.
**Alternative flows:** **A1** — A change in `TessynDesktop.Core` is merged and built: the banner appears with Restart daemon. **A2** — The build was made with uncommitted Core edits: the banner appears. **A3** — git is missing or the running commit is unknown: the time rule decides (FR.32.7).
**Covers:** FR.32.9.

## UC.HS.08 — Run the daemon elevated while WPF runs normally
**Actor:** the Architect. **Precondition:** WPF runs normally (not as administrator) from `[Publish-WPF]`.
**Main flow:** 1. A session must run something that needs administrator rights, for example the WPF end-to-end runner. 2. In Settings → Startup the User presses **Restart daemon elevated** and confirms (the running sessions are named). 3. The daemon exits; Windows shows its administrator prompt; the User approves. 4. The host runs elevated, WPF reconnects, and Settings → Startup says "The daemon runs elevated (as administrator)." 5. The session's turns now run as administrator, and files dropped from Explorer into the prompt input are still accepted. 6. When done, the User presses **Restart daemon normal** and confirms; the daemon comes back normal.
**Alternative flows:**
- **A1** — *Start daemon elevated* is on and no daemon runs: WPF starts the host through the administrator prompt (FR.32.10).
- **A2** — The User declines the prompt: the host starts normally, a notice says so, the prompt does not come back by itself, and the banner offers Restart daemon elevated (FR.32.13).
- **A3** — *Start daemon elevated* is on and WPF finds a daemon that runs normally: the banner "The daemon runs normally, but Start daemon elevated is on." offers Restart daemon elevated; nothing restarts without the User (FR.32.12).
- **A4** — The stale banner's *Restart daemon* while the daemon runs elevated: it comes back elevated (FR.32.11).
- **A5** — The User exits the elevated host from its tray icon (FR.32.1, FR.32.14).
**Covers:** FR.32.10–FR.32.14.
