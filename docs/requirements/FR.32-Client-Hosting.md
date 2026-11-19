# FR.32 — WPF Client Hosting: daily use, background daemon, coexistence (batch 2026-11-17)

> **New requirement area** (reports 458, 458b, 458c, 471, 471b; decisions A15, A23 Q6–Q8). FR.27–FR.30 are reserved by the WPF programme plan, so this area takes **FR.32**. Use cases: [UC.HS.01](/docs/usecases/UC.HS.01-Client-Hosting.md). Test cases: [TC.HS.01](/docs/testcases/TC.HS.01-Client-Hosting.md). Related: FR.8 (self-update), NR.1, NR.4.
>
> **Sunset note (2026-11-17).** WPF is the only desktop client. The project `code/TessynDesktop` remains only as the **daemon host** (`TessynDesktop.exe --background`), which WPF launches (FR.32.5). Its visible window is retired, so the side-by-side rules that involved that window are **withdrawn** (FR.32.6) and the rest are restated for the host alone.

**FR.32.1 — Background host.** The daemon can run without a window: `TessynDesktop.exe --background` (a start **without** `--background` behaves the same, so an old shortcut just starts the host; the host logs a note) hosts the MCP server, the scheduler, the relay, Telegram and the sessions, with no taskbar entry. A **tray icon** with *Exit Tessyn daemon* is the only way to reach it. Exit asks "N sessions are active, terminate?" when sessions run, then terminates them and shuts down; the shutdown always runs, even when the termination fails (the error is logged).

**FR.32.2 — One host per user.** A mutex (`Local\TessynDesktop.DaemonHost`) allows one daemon host. A second host exits at once. (The sentence "a second visible window shows 'already running' and exits" is **withdrawn**: the visible window is retired.)

**FR.32.3 — Run folder and self-update.** The WPF client runs from `[Publish-WPF]`, never from a `bin\Debug|Release` folder (A15). At start it compares its files with the newest build under `SourceCodesLocation`; if the build is newer, a hidden script waits for the process to end, copies the build (no delete, so `host\` survives; with retries; the result is recorded and a failed copy is logged at the next start) and restarts WPF with `--no-self-update`. Self-update and host start are **off** with `--test-window`, `--data-root` or `--daemon-file` (the *real environment* test), and when WPF runs from a build output.

**FR.32.4 — WITHDRAWN 2026-11-17 (decision A47). One-time import of the previous client's settings (Q8).** The import, its confirmation dialog and the daemon's `avalonia-ui` section (FR.20.10 addition) are removed together with the previous desktop client. The `imports` and `avaloniaImport` sections of an old `wpf.json` are no longer read; they stay in the file as plain data and cause no error.

**FR.32.5 — Starting the host (setting).** Settings tab **Startup**: *Start the background daemon when none is running*, **default on** (since the sunset, because WPF is the only client). On: when discovery finds no daemon, WPF starts `[Publish-WPF]\host\TessynDesktop.exe --background`; never when a daemon is found, never while the host mutex is held, not more than once per 60 s. Off: WPF shows **"Waiting for Tessyn to start…"** and retries without a timeout, so it connects as soon as a daemon is up, for example a host started by hand with `--background`. A change counts at once.

**FR.32.6 — Attaching and reconnecting.** WPF attaches to the daemon found through `daemon.json` and starts no host when one is found. When the daemon restarts (for example after **Restart daemon**, FR.32.7), WPF reconnects by itself and follows the new run (it registers its sound again). The sound is played by WPF only, acknowledged, never twice.

> **Withdrawn 2026-11-17 (dropped with the previous desktop client):** the takeover of a background host by a visible window of that client (the named event `Local\TessynDesktop.DaemonHost.Exit`, the 40 s wait, the messages "already running in another window" and "was asked to exit but is still running"), and the rule that a visible window is never taken over.

**FR.32.7 — Stale daemon banner.** When the daemon runs from this run folder's `host\` and its newest `TessynDesktop*.dll` is more than 2 s older than the newest build, the client shows **"The daemon is older than the build."** with **Restart daemon** (confirmation naming the running sessions; the host terminates its sessions and shuts down; WPF starts the new one). No banner for a daemon that does not run from this folder's `host\`, for an unknown build, or when the launcher is the no-op one.

**FR.32.8 — Starting banner.** While a host starts: "Starting the Tessyn daemon in the background…". After 45 s without `daemon.json` the detail says "the host started but did not publish daemon.json; are the agent tools switched off…". An unreadable token says so.

## Amendments and additions (2026-11-18)

| Existing | Amended by | Change |
|---|---|---|
| FR.32.7 Stale daemon banner | FR.32.9 | The banner shows only when the **daemon's source** changed between the running daemon and the newest build, not on every rebuild. |

**FR.32.9 — Stale daemon banner only for daemon changes.** (Amends FR.32.7.) Every assembly carries the git commit it was built from in its informational version (`1.0.0+<commit>`), so **any** commit, even a docs-only one, rebuilds the daemon assemblies with new bytes and new times. FR.32.7's time rule therefore fires after builds that changed nothing in the daemon. The check becomes:
1. **Commits.** Read the commit of the running daemon (from its `TessynDesktop.dll` in `host\`) and of the newest build (from the same assembly in the build folder). If they are equal and git reports no uncommitted changes in the daemon's source paths (step 2), no banner; with uncommitted changes there, the FR.32.7 time rule decides (the bytes may differ although the commit is the same).
2. **Daemon source.** Otherwise, in the git repository at `SourceCodesLocation`, compare the two commits over the **daemon's source paths**: the daemon host project and every project it references, transitively (today `code/TessynDesktop`, `code/TessynDesktop.Core`, `code/TessynDesktop.Protocol`), plus the build files that affect them (`Directory.Build.*`, `Directory.Packages.props`, `global.json`, `NuGet.config` under `code/`, when present). The project list is derived from the project references, not hard-coded. **No difference: no banner.** A difference: the banner, as in FR.32.7.
3. **Uncommitted changes.** When the newest build was made from a working tree with uncommitted changes in the daemon's source paths (git reports them now), the daemon counts as changed.
4. **Fallback.** When either commit is unknown, git is not available, a commit is not in the repository, or git fails or takes longer than 5 s, the FR.32.7 time rule decides, as before.
- **Off the UI thread.** The check runs in the background at connect and after each self-update check (FR.32.3); it never blocks the window. The result is logged (both commits, the decision and its reason).
- **Restart daemon** and its confirmation are unchanged.

## Additions (2026-11-19): the daemon elevated, WPF normal

> **Why.** When WPF itself runs as administrator, Windows refuses drag-and-drop from Explorer into it (a normal program may not drop into an elevated one), and every session runs elevated whether it needs to or not. Some work does need it: the WPF end-to-end runner `tools\e2e\Run-WpfE2E.ps1` needs an elevated caller (NR.6), and so do administrator tasks a session is asked to do. So **WPF always runs normally**, and the **daemon host** runs elevated only when the User chooses so. Windows elevates a program only through its administrator prompt (UAC), and a program started that way cannot have its input and output piped to a normal one. The host is started without pipes, so it can be elevated as a whole; everything it starts (each session's `claude` process and its tools) runs elevated with it. Elevating single sessions is not part of this. Use case: [UC.HS.08](/docs/usecases/UC.HS.01-Client-Hosting.md). Test cases: TC.HS.23–TC.HS.30.

**FR.32.10 — Start daemon elevated (setting).** Settings tab **Startup**, below *Start the background daemon when none is running* (FR.32.5): checkbox **Start daemon elevated**, **default off**, stored with that setting (client settings section `daemonHost`).
- **On:** when WPF starts the host (FR.32.5), it starts it through the Windows administrator prompt, so the host and everything it starts run as administrator.
- **Off:** the host starts normally, as before.
- The setting only decides how WPF starts a host. WPF never restarts a running daemon because of it (FR.32.6; for a daemon that runs normally see the banner of FR.32.12). A change counts at the next start.
- WPF itself never runs elevated because of this setting.

**FR.32.11 — Restart daemon elevated, Restart daemon normal.** Settings tab **Startup** has two buttons: **Restart daemon elevated** and **Restart daemon normal**.
- Each asks first, as *Restart daemon* does (FR.32.7): the confirmation names the running sessions, which are terminated. Cancel changes nothing.
- Confirmed: WPF asks the daemon to exit, waits for it, starts the host from `host\` the chosen way (refreshed from the newest build first, as on every start; elevated through the administrator prompt) and reconnects (FR.32.6). While the restart runs, WPF does not start a host by itself (FR.32.5), so no normal host gets in between. WPF itself does not restart.
- The buttons work whatever *Start the background daemon when none is running* says and whichever way the daemon runs now (a normal WPF can end an elevated daemon, FR.32.14).
- The daemon keeps the chosen way until it ends. *Start daemon elevated* decides only how WPF starts the next host by itself.
- *Restart daemon* on the stale banner (FR.32.7) restarts the daemon the way it runs now: an elevated daemon comes back elevated, through the administrator prompt.

**FR.32.12 — Which way the daemon runs.** The daemon reports whether it runs elevated (`client.hello`, member `elevated`, tdcp-spec 10.A10).
- Settings tab **Startup** shows **"The daemon runs elevated (as administrator)."** or **"The daemon runs normally."**; nothing while no daemon is connected or when the daemon does not report it (an older host).
- When *Start daemon elevated* is on and WPF is connected to a daemon that runs normally (one started before the setting was turned on, started by hand, or started after a declined prompt), a banner says **"The daemon runs normally, but Start daemon elevated is on."** with **Restart daemon elevated** (FR.32.11). WPF never restarts it without the User. The banner goes when the daemon runs elevated or the setting is turned off.

**FR.32.13 — A declined or failed administrator prompt.** When the User declines the administrator prompt, or the elevated start fails:
- WPF starts the host normally, so Tessyn stays usable, and says so: **"The daemon runs normally: the administrator prompt was declined."** or **"The daemon runs normally: the elevated start failed (<reason>)."**
- WPF does not show the prompt again by itself until its next start; only *Restart daemon elevated* shows it earlier. A declined prompt never comes back every minute (the once-per-60 s rule of FR.32.5 would otherwise repeat it).
- The banner of FR.32.12 then offers *Restart daemon elevated*.

**FR.32.14 — A normal WPF with an elevated daemon.** Everything WPF does with the daemon works when the daemon runs elevated and WPF does not:
- Discovery, the token and TDCP (FR.32.6) already work: `daemon.json` stays readable, and a daemon process that cannot be inspected counts as running.
- The host guard (FR.32.2) already counts an elevated host as running.
- **Exit request.** WPF can ask an elevated host to exit (FR.32.7, FR.32.11). Today a normal process may not signal the exit event of an elevated host, so *Restart daemon* answers "this daemon cannot be restarted from here"; the host must let the User's normal processes signal it.
- **Build check.** The stale check (FR.32.7, FR.32.9) finds the folder of an elevated host; today it cannot read the elevated process and shows no banner.
- **Tray icon.** The host's tray icon and *Exit Tessyn daemon* (FR.32.1) work when the host runs elevated.
- **Data root.** Files the elevated daemon writes under `%AppData%\TessynDesktop` and in the profile folders stay usable by a later normal daemon: they keep the folder's inherited permissions, and no special permissions are set.
- **Drag-and-drop.** With WPF normal, files dropped from Explorer into the prompt input are accepted (FR.31.2).

> **What elevated means.** While the daemon runs elevated, every turn runs as administrator: typed turns, scheduled turns, orchestration workers, Telegram and Tessyn Web turns. Through Tessyn Web, whoever can sign in can then have a session run administrator commands on this PC.
