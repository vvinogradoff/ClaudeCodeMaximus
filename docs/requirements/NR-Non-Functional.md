> Area file of the requirements (index: [README](README.md)).

## Non-Functional Requirements

**NR.1 — Platform:** The desktop client is a Windows application (WPF). The daemon logic (sessions, turns, orchestration, remote API) stays free of UI types and becomes cross-platform when the daemon moves out of the desktop process (Phase 4 of the WPF programme, `docs/refactoring/wpf-programme-plan.md`). Platform-specific code (path resolution, process launching, app data directory) is isolated behind interfaces. New desktop UI is built in WPF only (decision A9, 2026-11-15); the Avalonia client receives no new UI features and is removed once WPF reaches parity.

**NR.2 — Performance:** The budgets below are measured with the Phase 0 benchmark (`docs/refactoring/wpf-programme-plan.md` section 6). The session tree holds about 2,700 sessions today.
- First paint after a session switch: cold at most 100 ms (small sessions), 400 ms (1,600 entries) and 600 ms (5,000 entries); warm at most 150 ms for every size.
- No degradation: the p95 of the last 50 switches is at most 1.2 times the p95 of the first 50, over a 200-switch round robin.
- Memory growth is at most 50 MB over 200 switches.
- UI-thread stall at most 50 ms in steady state and 250 ms during a switch; with 2,500 sessions idle, no stall over 16 ms.
- Built transcript visuals stay under 500 whatever the session length.
- Logs stay under 5 MB per hour at the default level.

**NR.3 — Durability:** Session file appends are flushed immediately. `appsettings.json` is written atomically (temp-write + rename) AND all reads/writes are serialized under a single process-wide lock (`IAppSettingsService.SyncRoot`), so concurrent Saves from multiple threads (orchestration workers, Streaming Session Mode, UI toggles) can no longer race and silently lose an update (see ISSUE.008).

**NR.4 — Architecture:** MVVM with WPF and CommunityToolkit.Mvvm. One ViewModel per significant view. Models and services have no UI dependencies. Clients (the WPF desktop client, Tessyn Web) reach the daemon only through the remote API (TDCP, `docs/refactoring/tdcp-spec.md`); they never call daemon services directly.

**NR.5 — Testability:** Session file I/O, tree persistence, and process management are behind interfaces; unit tests require no real `claude` process or real filesystem. One contract-test suite runs against every way a client reaches the daemon (in-process, loopback HTTP, relay) and the answers must match. UI end-to-end tests live in explicitly-run projects (`Tessyn.Web.E2E.Tests`, `TessynDesktop.WPF.E2E.Tests`) that start the app against a test daemon on a temporary data root.

---

---

## Additions (batch 2026-11-17)

**NR.6 — Running the WPF end-to-end tests.** (Reports 461, 464; the Architect decides whether `CLAUDE.md` "Cover all use cases with E2E tests" also gets these lines.)
- Run WPF E2E **only through `tools\e2e\Run-WpfE2E.ps1`** (for example `tools\e2e\Run-WpfE2E.ps1 --no-build`), never with a bare `dotnet test`. The runner allows **one run at a time** machine-wide, kills the whole process tree at the end and returns the exit code. The test windows are off-screen, yet WebView2 needs the interactive desktop, so a run can briefly take focus. A private desktop (`-Hidden`) does not work for WebView2 tests; the `tessyn-e2e` user mode is **not yet available**.
- **Gotchas.** In a non-interactive session UI Automation cannot see WebView2 page content (drive it over CDP, `CdpPage`). `IsWindowVisible` is always false (test `WS_VISIBLE`). WebView2 ignores the DevTools-port argument in an **elevated** process (never run CDP tests elevated with `-AsCurrentUser`). A test that simulates a host reply must not also forward the request to the real host. Every UI Automation name is **unique per window** ("Settings" is the input area's toggle; the title-bar button is "Open Settings").
- E2E windows must not take the foreground repeatedly (guard `a1d85c4`).

**NR.7 — Stretching rule for panels.** On any panel the **text area stretches** to take the free space and **buttons keep their places and sizes**. A long text must never push a button out of view. Avalonia is the visual reference for WPF. (The Architect's standing UI rule; applied in FR.12.31 and FR.1.25.)
