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
