# NR — Non-Functional Requirements, additions (batch 2026-11-17)

> **Staging file, phase 1.** New IDs: **NR.6**, **NR.7**. Existing NR.1–NR.5 are unchanged.

**NR.6 — Running the WPF end-to-end tests.** (Reports 461, 464; the Architect decides whether `CLAUDE.md` "Cover all use cases with E2E tests" also gets these lines.)
- Run WPF E2E **only through `tools\e2e\Run-WpfE2E.ps1`** (for example `tools\e2e\Run-WpfE2E.ps1 --no-build`), never with a bare `dotnet test`. The runner allows **one run at a time** machine-wide, kills the whole process tree at the end and returns the exit code. The test windows are off-screen, yet WebView2 needs the interactive desktop, so a run can briefly take focus. A private desktop (`-Hidden`) does not work for WebView2 tests; the `tessyn-e2e` user mode is **not yet available**.
- **Gotchas.** In a non-interactive session UI Automation cannot see WebView2 page content (drive it over CDP, `CdpPage`). `IsWindowVisible` is always false (test `WS_VISIBLE`). WebView2 ignores the DevTools-port argument in an **elevated** process (never run CDP tests elevated with `-AsCurrentUser`). A test that simulates a host reply must not also forward the request to the real host. Every UI Automation name is **unique per window** ("Settings" is the input area's toggle; the title-bar button is "Open Settings").
- E2E windows must not take the foreground repeatedly (guard `a1d85c4`).

**NR.7 — Stretching rule for panels.** On any panel the **text area stretches** to take the free space and **buttons keep their places and sizes**. A long text must never push a button out of view. Avalonia is the visual reference for WPF. (The Architect's standing UI rule; applied in FR.12.31 and FR.1.25.)
