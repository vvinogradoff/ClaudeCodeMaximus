# Tessyn WPF Programme — Plan

> **Draft for Architect review, 2026-11-04.** Working document, not committed. Author: Developer/Claude.
> Inputs: the seven intel reports in `.wpf-intel/` (2026-11-03, untracked scratch) and the Architect's
> decisions of 2026-11-03/04.
> Supersedes `refactoring-plan.md` Stage 8 (WPF as an extra client beside Avalonia) and the sequencing of
> the 2026-07-25 eviction plan (session `2026-07-25-0214-rpxokh`). Decisions D1–D5 of `refactoring-plan.md`
> and the 2026-07-25 `tessyn` MCP prefix still stand.
>
> **Revision 2026-11-14.** The Architect said go: "start sonnets for the implementation", commits allowed.
> Other sessions shipped work that overlaps this plan between 2026-11-04 and 2026-11-13:
> - perf diagnostics D1/D2: logging hygiene, the off-UI-thread sweep, always-on diagnostics;
> - FR.24 Session Tags;
> - FR.25 Tessyn Web, whose remote API changes Phase 1.
>
> Changes in this revision:
> - The Phase 0 table has a status column.
> - Phase 1 is rewritten from the gap analysis `.wpf-work/30-remote-api-gap.md`: no "TDCP v2" — extend the
>   existing remote API with capabilities (P13).
> - Proposed numbers moved: new FRs start at FR.26, and the slowdown issue is ISSUE.016.
>
> Worker reports for this phase are in `.wpf-work/` (untracked).

## 1. Goal

Replace the Avalonia client with a WPF client that stays fast with large sessions and survives its own
restarts, on top of a daemon that owns all state. Then add a multi-screen layout, output modes, an
embedded browser pane with clickable previews of GitHub, Linear and Slack links, and helper jobs on
local models.

Not in scope: a cross-platform UI; native GitHub/Linear/Slack API connectors (sessions keep using MCP
servers); Claude in Chrome; Tessyn.Web (stays in the backlog).

## 2. Decisions

| ID | Decision | Source |
|---|---|---|
| P1 | Avalonia is retired completely once WPF reaches parity. | Architect, 2026-11-03/04 |
| P2 | The WPF client talks to the daemon only over TDCP, from day one. Until the daemon moves out (Phase 4) it connects to the daemon already running inside the Avalonia app (`/tdcp`, `AgentMcpServer.cs:271`). Avalonia is never converted into a TDCP client. | Architect, 2026-11-04 |
| P3 | Transcript rendered in WebView2 (one instance per transcript pane, one shared environment); the rest of the shell is native WPF. **Confirmed 2026-11-14 by the prototype (1.10, `.wpf-work/40-transcript-proto.md`):**<br>• Switching: cold first paint 37 ms at 1,600 entries and 57 ms at 5,000; warm p95 10.5 ms; UI stall at a switch 2.5 ms.<br>• Scrolling 5,000 entries at a steady 60 fps; no stalls on a 500 KB entry or a 2,000-row table.<br>• The native list misses the budgets from 1,600 entries, freezes 1.5–6.8 s on huge entries, and cannot select across messages.<br>• Cost: about 180 MB more memory, stable.<br>Open product question: cross-message selection works inside the rendered window (about 1.5 screens each side). Whether it must reach further decides between pinning selected entries in the page or "copy range" from the raw text. | Architect, 2026-11-04; confirmed 2026-11-14 |
| P4 | All projects move to .NET 10 (LTS to 2028-11) now. .NET 9 support ends 2026-11-10. | Architect, 2026-11-04 |
| P5 | Framework-independent relief goes into the current app now, including the logging configuration. | Architect, 2026-11-04 |
| P6 | Settings persistence: split `appsettings.json` into several JSON files behind a debounced single writer. No database schema. | Architect, 2026-11-04 |
| P7 | The daemon owns shared and turn-affecting state; each client owns its layout, selection and presentation state. Live updates are pushed with sequence numbers and resume, replacing per-session file watching. (Answers the July questions C4 and C6.) | Architect, 2026-11-04 |
| P8 | Developer may create projects, solution entries, project references and `Program.cs` files as needed for this programme. | Architect, 2026-11-04 |
| P9 | "Browser activities" = an embedded browser pane (WebView2, persistent profile, one-time sign-in to Slack and others). Later, Tessyn's own MCP tools let a session drive it. Not Claude in Chrome. | Architect, 2026-11-04 |
| P10 | GitHub, Linear and Slack stay MCP servers of the sessions. Tessyn (a) tells every session to write full URLs, e.g. `https://github.com/tryhardstop/windows-app/pull/196` rather than `PR#196`, `https://linear.app/hardstop/issue/HSE-240` rather than `HSE-240`; (b) makes URLs in the transcript clickable and previews them in the browser pane. No API connectors, no tokens. | Architect, 2026-11-04 |
| P11 | Helper LLM jobs use a direct Ollama HTTP client in Core, with cloud fallback (Claude Assist) allowed. | Developer's call, delegated 2026-11-04 |
| P12 | *Proposed:* UI feature freeze on Avalonia from the start of Phase 2. New UI features go to WPF only; daemon features go to Core and reach both. Since 2026-11-04 the Avalonia UI gained the Focus view, the Tags tab, Recent groups and tree-search changes, all of which now join the parity list. | Proposed |
| P13 | No separate "TDCP v2". The WPF client uses the remote API that Tessyn Web introduced (one `TdcpDispatcher`, loopback `/tdcp` and relay transports), extended additively. `protocolVersion` changes only on a breaking change; new features are announced as `capabilities` in `client.hello`. The web contract stays at its 21 remote methods: additions are loopback-only unless an FR.25 amendment opens them. Drafts are daemon state. One local token until Phase 4, then per-client tokens. Entry ids are persisted per-file ordinals. | Developer, from the gap analysis, 2026-11-14 |

Why P11: spawning the `claude` CLI per helper job costs a process start and a ~30-tool system prompt,
gives no control over `keep_alive`, context size or model residency, and runs into the GGUF chat-template
failures documented in `docs/business-analysis/Local-Models_Agentic-Coding-Findings.md`. Ollama's
`/api/chat` with a JSON schema in `format` is light and needs no new package (HttpClient and
System.Text.Json).

## 2b. Architect decisions of 2026-11-15 (Focus answers A2–A14)

| Item | Decision |
|---|---|
| A2 | Ship rollout 2 (the turn engine) now, with the spinner fix, in one restart. |
| A3 | ISSUE.013: all six proposed answers in §9 accepted. |
| A4 | Transcript selection: leave as is (about 1.5 screens either side of the viewport). |
| A5 | Search index upkeep approved, including the schema change. Do **not** cap indexed tool output. |
| A6 | Phase 4 daemon runs as a per-user background process started at logon, not as a Windows service. |
| A7 | Second-screen layout to be decided when we get there. It will show Linear tickets, GitHub PRs, photos, videos and the .md documents sessions use or produce. |
| A8 | Parity: **keep** the screensaver (used daily) and the Recent tab. **Drop** the JSONL view toggle, the macOS shortcuts, the unused `InputDialog` and the editable colour palette (revisit theming in WPF later). |
| A9 | New desktop UI (Focus references, attachments, previews) goes into **WPF only**. Avalonia is being sunset. |
| A10 | The documentation changes in §10 are approved; apply them now and leave the other session's FR.25 drafts untouched. |
| A11 | Web file access: option 2. Explicitly referenced files and attachments, plus files inside the session's working directory. No TOTP. |
| A12 | Attachments are stored per profile in app data. |
| A13 | https media previews auto-load from an allowlist; other hosts are click-to-load. |
| A14 | A queued send keeps the model, profile and effort that were selected when Send was pressed. |

## 3. Why the current UI slows down

Verified in code on 2026-11-03. Detail in `.wpf-intel/03-perf-session-switching.md`; filed as ISSUE.016
(ISSUE.015 went to search).

**Status 2026-11-14:**
- Causes 4 and 7 were fixed on 2026-11-10 by the perf session: D1/D2, commits `23c7388`, `abef664`,
  `5164dd0`, with always-on diagnostics.
- Live numbers since then, from the daily driver: about 1.9 GB private and a 1.3 GB heap with 0.5–0.7 GB
  fragmentation; gen-1 collections almost as frequent as gen-0; 276 s of total GC pause; 70–110 % CPU at
  idle.
- A 2.5 s UI hang came from a 749 ms GC pause. A session switch takes 255 ms, and 326–572 ms until idle.

| # | Cause | Where | Carries into WPF unless designed out |
|---|---|---|---|
| 1 | Transcript not virtualized: every message is built on every switch, and each one builds the subtrees for all roles | `code/TessynDesktop/Views/SessionView.axaml:124` | Yes; WPF's `ItemsControl` isn't virtualized either |
| 2 | Every opened session stays loaded forever, each with two file watchers that re-parse whole files | `code/TessynDesktop/ViewModels/MainWindowViewModel.cs:36` | Yes |
| 3 | The markdown control rebuilds completely when the text, search term, current match or font size changes | `code/TessynDesktop/Views/MarkdownView.cs:86` | Yes |
| 4 | Every 60 s a UI-thread timer checks ~2,500 sessions against ~39 profile folders and logs a line per session | `code/TessynDesktop/ViewModels/SessionTreeViewModel.cs:137`, `:928` | Yes |
| 5 | Every switch re-reads and re-parses the whole session file | `code/TessynDesktop/ViewModels/SessionViewModel.cs:559` | Yes |
| 6 | `appsettings.json` is 2.6 MB (1.76 MB of it the tree) and is serialized synchronously from ~35 UI call sites | `AppSettingsService` | Yes |
| 7 | Debug-level logging: ~1 GB/day, 6 GB in total, with some days truncated at the 1 GiB cap | `code/TessynDesktop/App.axaml.cs:101` | Yes |

NR.2 is sized for 500 sessions; the tree has ~2,700.

## 4. Target architecture

```
TessynDesktop.WPF (net10.0-windows)                        TessynDesktop.Core (daemon logic)
  shell: tree, command bar, input, dialogs   ── TDCP v2 ──►  hosted by the Avalonia app until Phase 4,
  transcript panes: WebView2 ("app" profile)   loopback HTTP   then by the TessynDaemon exe
  browser pane: WebView2 ("browser" profile)   + SSE push      /mcp for claude · /tdcp for clients
  references TessynDesktop.Client only                         scheduler · turn engine · persistence · index
```

| Project | TFM | Role | Phase |
|---|---|---|---|
| `TessynDesktop.Protocol` | net10.0 | DTOs, method names, event types, error codes. No dependencies. | 1 |
| `TessynDesktop.Client` | net10.0 | Async TDCP client: handshake, reconnect, resync, SSE, local caches | 1 |
| `TessynDesktop.Core` | net10.0-windows, plain net10.0 in Phase 4 | Daemon logic; references Protocol | 0 and 4 |
| `TessynDesktop.WPF` and `TessynDesktop.WPF.Tests` | net10.0-windows | The new client. References Client only, so the compiler enforces the boundary. | 2 |
| `TessynDaemon` | net10.0 | Daemon host exe | 4 |
| `TessynDesktop` (Avalonia) | — | Daily driver until Phase 3, daemon host until Phase 4, then deleted | — |

Rules:
- The WPF project never references Core, and no live model objects cross the boundary (DTOs only).
- The UI thread does no file I/O, JSON parsing, markdown parsing or settings serialization.
- Periodic work scales with visible and active sessions, never with the size of the tree.

## 5. Phases

Sizes are relative: S is days, M about a week, L several weeks of worker time. Calendar time depends
mostly on review and manual testing.

### Phase 0 — Relief and baseline (S–M)

Makes the daily driver faster. Only 0.8 needs a requirements change.

| ID | Item | Where | Done when | Status 2026-11-14 |
|---|---|---|---|---|
| 0.1 | Retarget Core, Tests and the app to `net10.0-windows10.0.17763.0`; update packages; remove the stale `bin/Debug/net9.0-*` output | csproj files. `SelfUpdateService` picks the newest `net*` folder, so the app self-updates onto it. | Build and all tests green; app runs on .NET 10 | **Done** on master: `8301818`, `db8913d` (Avalonia 11.3.23), `16df8eb` (packages; NuGet audit warnings cleared). 1,696 + 95 + 16 tests green. |
| 0.2 | Logging: Information by default, size cap, roll on size, retention, no per-session resumability line | `App.axaml.cs` | Logs in MB/day, not GB | **Done** by the perf session (D1). Old logs cleared on 2026-11-14 (6.6 GB → 2.5 MB). |
| 0.3 | Resumability sweep off the UI thread | `SessionTreeViewModel.cs` | No UI stall from the sweep | **Done** by the perf session (D2.1, `abef664`). |
| 0.4 | Bounded session cache: LRU of the active session plus 4. Running, streaming and unsent-input sessions are never evicted; evicted view models are disposed. | `MainWindowViewModel.cs:36` | Memory flat across 200 switches | **Done**, merged `91b8d0c`: LRU of the active session + 4 with complete disposal. The bigger root cause was found too: the autocomplete code index rescanned ~337k files and was never released. It is now incremental, capped and releasable (ISSUE.016). |
| 0.5 | A switch restores the last profile, model and effort from node metadata or a tail read, not a full parse | `SessionViewModel.cs:559` | No full-file read on switch | **Done**, merged `91b8d0c`: tail read with a widening fallback. |
| 0.6 | Robustness fixes suggested by `.wpf-intel/07`, each first reproduced by a test: scheduler and Telegram mutations under `SyncRoot`; turn-lock and running-flag ordering; stderr drained concurrently; unobserved task exceptions. *Dropped:* a job object that kills `claude` children with the host, because workers must survive app restarts. | Core | Tests green | **Done**, merged `3c43950`. Three hazards confirmed and fixed with tests that fail before the fix (settings lock, stderr deadlock, scheduler faults); one disproved (the running flag is reference-counted). The review fixes cover cron edge cases and stderr cancellation. |
| 0.7 | Switch-latency instrumentation; a synthetic benchmark data generator (never real data); the benchmark from §7 | test tooling | Baseline recorded in ISSUE.016 | Instrumentation **done** by the perf session (`OperationTimer` `session-switch`, UI hang watchdog, MEM lines). The generator comes with the transcript prototype (40). |
| 0.8 | Link fidelity: the instruction block tells every session to write full URLs for GitHub PRs, issues and commits, Linear issues and Slack messages | `code/TessynDesktop.Core/Services/InstructionBlockBuilder.cs` | FR.26 approved first; a test asserts the text | **Done**, `afc5f47`. The FR.26 text moves into requirements.md after the FR.25 review. |
| 0.9 | File ISSUE.016 with the causes in §3 and the live evidence | `docs/issues/` | — | **Done**: `docs/issues/ISSUE.016.idle-memory-and-cpu-code-index-and-session-cache.md`, merged `91b8d0c`. |
| 0.10 | Startup scans off the UI thread: `RefreshLastPromptTimes` and `RefreshGitOrigins` (5 of the 6 recorded hangs, ~5 s each) | `SessionTreeViewModel.cs`, new Core scanners | No startup hang | **Done**, merged `bfe4691`, with the review fixes: a late scan result never overwrites newer live values, and the Recent dropdown refreshes when the scan completes. |

Exit: green on .NET 10; baseline numbers recorded; no sweep stall; flat memory across switches; logs
around 50 MB/day or less.

### Phase 1 — Daemon backbone: the remote API becomes the client API (L)

Rewritten 2026-11-14 from `.wpf-work/30-remote-api-gap.md`. That report has the full inventory, the
evidence, the wire-completion table and the contract-break list.

Tessyn Web (FR.25) already gave the daemon what the client needs as a base: one `TdcpDispatcher` behind
both the loopback `/tdcp` and the relay, tail-first paged `session.entries`, `session.send/stop/compact`,
Focus, catalog, usage and an event bus.

The WPF client still lacks:
- the new events on the local stream (loopback SSE carries only the two legacy events);
- resume after a gap;
- stable entry ids;
- turn streaming;
- settings, drafts and profile management;
- global search and import;
- tree mutation.

Avalonia keeps working throughout. **[web+]** marks changes that also help Tessyn Web.

| Step | Work package | Content | Status |
|---|---|---|---|
| 1 | P1.A Local push parity | Loopback SSE carries the same bus events as the relay, with SSE ids; legacy frames only on request | **Done**, merged `48671f7`, with review fixes A1, A3, A4, A6, A7. |
| 2 | P1.B Event log with resume | Monotonic `eventSeq`, bounded ring, `Last-Event-ID` replay or `resync` **[web+]** | **Done**, merged `48671f7`, with review fixes A1, A3, A4, A6, A7. |
| 3 | P1.C Hello and discovery (was 2.2) | `client.hello` capabilities; `%AppData%\TessynDesktop\daemon.json`; the token moves out of `appsettings.json` into a DPAPI file; constant-time compare; requests with an `Origin` header rejected; 401 on a bad token | **Done**, merged `48671f7`, with review fixes A1, A3, A4, A6, A7. |
| 4 | P1.D Contract tests | One suite over in-process, loopback and relay transports; golden shapes of the 21 remote methods | **Done**, merged `48671f7`, with review fixes A1, A3, A4, A6, A7. |
| 5 | P1.E Content store v2 | Per-session sidecar index; persisted `entryId`s that survive restarts; byte-range paging; `SessionEntryReader` backed by it; JSONL-to-`.txt` sync owned by the daemon alone (ends the ISSUE.009/014 class) **[web+]** | **Done**, merged `4d60cf0`, with review fixes B1–B3. Master after the merges: 1,880 + 95 + 16 tests green. One known timing flake is being fixed (worker 150). |
| 6 | P1.F Entry ids and seq on the wire | `entryId`, `fileId` and `seq` fields; `afterSeq`/`beforeEntryId` paging; `entries.appended` with inline entries, published beside `session.appended` **[web+]** | **Done**, merged `7a783f1`, with review fixes. `entries.appended` stays local-only (not relayed to phones); `session.get` falls back to the full parse. |
| 7 | P1.G O(1) session header | `session.get` from the sidecar; batch `session.headers` | **Done**, merged `7a783f1`: `session.get` reads its header from the content store; loopback `session.headers`. |
| 8 | P1.H Selection is client state | WPF never calls `tree.getSelected/setSelected`; a client-owned layout file | Docs only |
| 9 | P1.I Turn engine in Core | Move `TurnController`'s path into Core; `turn.started/delta/tool/status/usage` events; `turn.send`, with `session.send` kept as an alias **[web+]** | **Merged to master as rollout 2** (merge `fd21b58`, 2026-11-15, A2), after master was merged into the branch (`b2ccba7`). Build 0 errors; 2,459 + 95 + 16 + 146 + 3 tests green. Waiting for the Architect's restart and the Appendix B smoke checklist; the rollback hashes are in B.2. |
| 10 | P1.J Complete the wire | Loopback-only unless stated: tree mutation; global search and import; profiles (including ISSUE.013 `profile.switch`); versioned settings sections; schedules as DTOs; drafts; `attention.listAll` **[web+]**; `daemon.health` | After 3–7 |
| 11 | P1.K Persistence split (P6, P7) | Tree, schedules and Telegram maps out of `appsettings.json`; a debounced single writer; `settings.changed` events | After 10 |
| — | 1.8 ISSUE.013 daemon side · 1.9 index upkeep · 1.10 transcript prototype | Unchanged, in parallel. 1.10 is in progress (worker 40). | |

The 1.8–1.10 details from the 2026-11-04 version still apply:
- **1.8:** profiles pinned by `ProfileId`; no silent fallback to Default; complete migration on a switch; shared project memory; a restart of a live streaming process on a profile change; a clear spawn error.
- **1.9:** orphan purge, `optimize`, a vacuum policy, indexed `external_id`, a cap on huge tool output, a throttled startup reindex. The index schema change needs your approval (§11 Q2).
- **1.10:** WebView2 against a native virtualized list, on real and synthetic sessions.

Exit:
- The contract suite is green on all transports.
- A scripted client can page the largest session, stream a turn, and resync after a daemon restart.
- Tessyn Web is unaffected.
- Nothing changes for you in Avalonia.

### Rollout plan (decided 2026-11-14)

- **Rollout 1** is master as of `2be3407`: Phase 0, P1.A–G, P2.A and the WPF MVP. The Architect restarts TessynDesktop, checks the daily driver, and tries the WPF client with `run-wpf.ps1`.
- **Rollout 2** is P1.I alone, the turn engine under the Avalonia send path. Nobody could run it live, because a second Avalonia instance cannot be isolated from the live one. It ships after a final review, with the smoke checklist in `.wpf-work/180-p1i-turn-engine.md` §8, and a one-commit revert if anything misbehaves.
- **Behaviour change in rollout 2** (supervisor decision, the Architect may veto): a queued send uses the model, effort and profile selected when Send was pressed, not whatever is selected when the turn starts.

### Phase 2 — WPF MVP beside Avalonia (M–L)

**Status 2026-11-14 (later):**
- **P2.A merged** (`03a3061`): `TessynDesktop.Protocol` and `TessynDesktop.Client`, after a review with fixes; master is green with 1,920 + 95 + 16 tests.
- **WPF MVP merged** (`11fe62b`): shell, virtualized tree, WebView2 transcript, send/stop, client state, `run-wpf.ps1`, `--daemon-file` / `--data-root`. Tests: 1,920 + 95 + 16 + 140 green. The live run waits for the Architect to restart TessynDesktop onto this master. Follow-ups: F7 (reuse tree nodes by id), F8 (find covers loaded entries only).
- Before that merge: the WPF shell (2.1, 2.4–2.8) stage A was done: 123 view-model tests, and the transcript security design was reviewed as sound. The review fixes (the app did not exit; duplicate-append races) and stage B (the real client, live run) are in progress (worker 140).
- P1.F/G (worker 170) and P1.I turn engine (worker 180) are in progress.

Earlier status:
- P2.A was in progress (worker 130): `TessynDesktop.Protocol` (the remote message types and wire constants moved out of Core) and `TessynDesktop.Client` (async `ITessynClient`, discovery through `daemon.json` and the DPAPI token, resumable SSE).
- The WPF shell (2.1, 2.4) starts as soon as the client interface is committed.

Starts when 1.1, 1.2, 1.4 and the 1.6 skeleton land. The first visible result is a read-only viewer;
sending and streaming follow 1.3.

| ID | Item |
|---|---|
| 2.1 | Projects `TessynDesktop.WPF` and `TessynDesktop.WPF.Tests`; Generic Host, MS DI, Serilog; CommunityToolkit.Mvvm; built-in Fluent theme plus own styles; per-monitor DPI v2; single instance |
| 2.2 | Daemon discovery through `daemon.json` (built in P1.C); WPF shows a connection banner and reconnects with resume |
| 2.3 | Own run folder (e.g. `[Publish-WPF]`) and self-update from the `TessynDesktop.WPF` build output; WPF takes over `[Publish]` in Phase 4 |
| 2.4 | Shell: custom title bar, virtualized session tree with lazy children and search (including FR.1.10 and FR.1.11), status bar |
| 2.5 | Transcript pane per the 1.10 result: paged loading, pushed appends, markdown and code highlighting, find, copy, scroll anchoring by entry id, an LRU of loaded sessions |
| 2.6 | Prompt input, Send and Stop, queueing, streaming via `turn.event`, drafts, model, effort and profile selectors |
| 2.7 | Instrumentation from day one: switch timings, a UI-thread watchdog that logs a stack when blocked over 250 ms, a diagnostics overlay |
| 2.8 | URLs in the transcript are clickable (system browser until 3.2) |

Exit: daily work is possible in WPF while Avalonia runs; the §6 budgets are met for the L and XL tiers.

### Phase 3 — Parity, browser pane, interactive links (L)

| ID | Item |
|---|---|
| 3.1 | Parity checklist from `.wpf-intel/04` §3, plus later additions (FR.23 Focus view, FR.1.10, FR.1.11). Suggested order: command bar and instruction toolbar; settings; profiles and auth, with the ISSUE.013 dialogs (R1, R5, R6); import picker and global search; delayed send and schedules; orchestration views; MCP registry and OAuth; Telegram settings; toasts (new exe identity) and sounds; tree drag-and-drop, rename and move; key bindings; theme colours; local tools popup |
| 3.2 | Embedded browser pane (P9): WebView2 with its own persistent profile under `%AppData%\TessynDesktop\browser\`, separate from the transcript panes' profile; address bar, back and forward, open externally; docks beside the transcript or opens on the second screen |
| 3.3 | Interactive links (P10): GitHub PR, issue and commit links, Linear issue links and Slack permalinks render as chips (e.g. `windows-app#196`, `HSE-240`). A click previews the page in the browser pane; a modifier-click opens the system browser. No API calls: the pane's own signed-in web sessions render the page. |
| 3.4 | You switch to WPF for daily use; Avalonia keeps running only as the daemon host |

Exit: parity checklist done; Avalonia no longer used directly.

### Phase 4 — Daemon out of process, Avalonia removed (M)

| ID | Item |
|---|---|
| 4.1 | `TessynDaemon` host exe: the lifecycle moves out of `App.axaml.cs` (MCP and TDCP servers, scheduler, Telegram bridge, reindex, skill provisioning, attention); single instance; writes `daemon.json`; Kestrel optional |
| 4.2 | Hosting, decided in §11 Q3. Recommended: a per-user background process started at logon rather than a Windows service, because a LocalSystem service cannot read the user's DPAPI-protected tokens or per-user profiles and cannot raise toasts |
| 4.3 | Daemon self-update through an external helper (MHSystems pattern). WPF offers "Update daemon" and asks for confirmation only when sessions are active. |
| 4.4 | Core to plain `net10.0`: the UWP toast package reference, winmm sound, DPAPI stores and the PowerShell updater move behind interfaces with Windows implementations |
| 4.5 | Delete the Avalonia project; WPF takes over `[Publish]`; rename `cmx` to `tessyn` in lockstep with the Claude Code allowlist and the per-profile skill folders |

Exit: closing, restarting or updating the UI never interrupts a session.

### Phase 5 — New capabilities (rolling, after Phase 2)

| ID | Item |
|---|---|
| 5.1 | Multi-screen: one process, several windows. A layout coordinator reacts to `WM_DISPLAYCHANGE` with debounced monitor snapshots, clamps placement to the monitors present, collapses to one window when a screen disappears and restores when it returns. Selection and layout are per window. Which panes go where is your design (§11 Q4). |
| 5.2 | Output modes: raw (from the JSONL, tool calls visible), condensed (deterministic folding, no LLM), summary, action items and questions (helper LLM). A daemon-side view service with paging and a sidecar cache; a "pending" state in the UI. Action items and questions can feed the Focus view (FR.23). |
| 5.3 | Helper LLM (P11): an Ollama `/api/chat` client with JSON-schema output and control of `keep_alive` and context size; residency rules so the coding model isn't evicted; a priority job queue with cancellation. Fallback order: local, then Claude Assist (Haiku), then deterministic. This lifts FR.12's "Ollama excluded from Claude Assist". |
| 5.4 | Agent-driven browser pane: Tessyn MCP tools (navigate, page snapshot, click, type, screenshot) built on WebView2's in-process DevTools Protocol API and routed from the daemon to the connected client. No remote-debugging port, because any local process could then drive a browser signed in to Slack and GitHub. Supersedes `browser-control-cdp-plan.md` and the `--chrome` toggle. |

## 6. Budgets (proposed NR text)

From `.wpf-intel/03` §6.4 and §7.3, measured with the Phase 0 benchmark on this machine:
- First paint after a switch: cold at most 100 ms (S tier), 400 ms (L, 1,600 entries) and 600 ms (XL,
  5,000 entries); warm at most 150 ms for every tier.
- No degradation: p95 of the last 50 switches at most 1.2× the p95 of the first 50, over a 200-switch
  round robin.
- Memory growth at most 50 MB over 200 switches.
- UI-thread stall at most 50 ms in steady state and 250 ms during a switch; idle with 2,500 sessions,
  no stall over 16 ms.
- Built transcript visuals bounded (under 500) whatever the session length.
- Logs under 5 MB/hour at the default level.

## 7. Testing

- Contract tests: one suite, two implementations (in-process and wire). New in Phase 1.
- WPF view-model tests (xUnit) against a fake client.
- End-to-end tests with FlaUI (UIA3) against the WPF app and a test daemon on a temporary data root with a
  fake `claude` executable. Needs a data-root override at startup (covered by P8).
- Benchmark: synthetic data generator, the 10-step procedure from `.wpf-intel/03` §7, BenchmarkDotNet for
  Core paths.
- CLAUDE.md's description of self-managing E2E tests is corrected to match what exists.

## 8. How the work is run

- I supervise; Sonnet workers code through cmx. One build owner at a time; the others write to
  `.staging\<task>\`. No git worktrees inside the repo, because the self-updater scans it recursively.
- Every stage ends with build and all tests green, is reviewed by a separate worker, and is committed with
  its FR/TR ids.
- Docs first: requirement drafts go to you, and code follows your approval.
- Avalonia stays your daily driver until Phase 3, so anything touching it is tested before the app
  restarts into it.

## 9. ISSUE.013 — proposed answers to its open questions

| Question | Proposed answer |
|---|---|
| Why did the orchestrator expect `send_to_session` to an old child to fail? | Reproduce it as a test in 1.8 before changing anything |
| Shared project memory: default or opt-in? Which wins on conflicts? | Shared per project by default, opt-out per project. On conflict keep both files and union the index (R4 as written). |
| Pin profiles by `ProfileId` instead of the display name? | Yes |
| May `spawn_session` register a new project directory? | No; the error lists the registered directories (R9) |
| Cascade automatically when the caller is the supervisor? | A switch you make offers the cascade with confirmation (R1). An MCP caller can move only its own descendants, and only explicitly (`migrate: true`, R2). |
| Same-account profiles: skip R3? | No. Always move; it is cheap and keeps one rule. |

## 10. Documentation changes to approve

| Document | Change |
|---|---|
| NR.1 | Cross-platform becomes: Windows desktop client (WPF); daemon logic stays UI-free and becomes cross-platform in Phase 4 |
| NR.2 | Replace "responsive for up to 500 sessions" with the §6 budgets (the tree has ~2,700 sessions) |
| NR.3 | Debounced single-writer persistence split (1.7) |
| NR.4 | MVVM with WPF and CommunityToolkit.Mvvm; clients reach the daemon only through TDCP |
| NR.5 | Add contract tests and UI automation |
| FR.2.3.2, FR.9.1, FR.9.3, FR.10, FR.12.22, FR.15.4 | Remove Avalonia class and resource names |
| FR.12 (Claude Assist) | Helper jobs may use local models first (5.3) |
| New FR.26–FR.30 (provisional; FR.24 is Session Tags, FR.25 Tessyn Web) | FR.26 link fidelity and interactive links (Appendix A); FR.27 embedded browser pane; FR.28 output modes; FR.29 multi-screen layout; FR.30 helper models |
| Glossary | The "In-Process Agent MCP" and "Telegram Bridge" entries name Avalonia. New terms: Remote API capabilities, Event Log, Entry Id and Seq, Browser Pane, Link Chip, Output Mode, Helper Model, Layout Coordinator. |
| `refactoring-plan.md`, `tdcp-spec.md` | Stage 8 marked as superseded by this plan; a v2 section added |
| CLAUDE.md | .NET 10 build path; E2E description; WPF run folder; later the `cmx` rename |
| ISSUE.016 (new) | UI slows down with large sessions: the evidence and the fixes (being written by worker 20) |

## 11. Open questions

| # | Question | Recommendation |
|---|---|---|
| Q1 | Which store is canonical for session content? | Keep Tessyn's `.txt`: it is the one continuous record of a session across compactions, detaches and profile moves, whereas a session's JSONL is split across several Claude session ids. Add entry ids and a sidecar index. JSONL stays Claude's resume artifact and the source for raw mode. |
| Q2 | Search index scope and schema | Keep the machine-wide scope, since Global Search (FR.13.15) depends on it, but cap indexed tool output per entry and add maintenance (1.9). Approve the index schema change. |
| Q3 | How should the daemon be hosted? | A per-user process at logon (4.2) |
| Q4 | What goes on the second screen? | Your design. A sensible default is the browser pane and the Focus view on screen 2. |
| Q5 | Which features are dropped at parity? | Candidates from `.wpf-intel/02`: screensaver, JSONL toggle (replaced by raw mode), Recent tab, macOS shortcuts, the unused `InputDialog`. Keep or drop the editable colour palette. |
| Q6 | Avalonia UI feature freeze from Phase 2 (P12)? | Yes |

## 12. Risks

| Risk | Mitigation |
|---|---|
| Moving the turn engine (1.3) touches the daily driver's most critical path | Safety-net tests first; Avalonia reaches the new engine through an adapter behind a switch until it is proven |
| WebView2: runtime dependency, airspace (WPF content can't overlap it), re-hosting between windows | Prototype gate 1.10. WebView2 ships with Windows 11. Each window owns its own panes, so nothing moves between windows. |
| The Fluent theme is still experimental in .NET 10 | Own styles for app surfaces; WPF-UI only if gaps remain |
| Phase 1 is large before anything shows up in WPF | A read-only WPF viewer as soon as 1.2 and 1.4 land |
| Two clients on one daemon during the transition | Client-side selection and layout (1.5); WPF never writes Avalonia's UI state |
| Avalonia keeps gaining features during the programme (FR.23 landed on 2026-11-04) | The P12 freeze |
| Profile UI built before ISSUE.013 is settled | The §9 answers; 1.8 lands before the profile work in 3.1 |

## Appendix A — FR.26 (needed for Phase 0 item 0.8)

Renumbered on 2026-11-14 from the provisional FR.24, because FR.24 became Session Tags and FR.25 Tessyn Web.
Treated as approved with the Architect's go for Phase 0. It moves into `requirements.md` once the FR.25
drafts in that file have been reviewed, so the two don't get mixed.

FR.26 complements FR.24 Session Tags: FR.24 covers the chips under the session header that come from
`tag_session`, and FR.26 covers links inside the transcript text.

**FR.26 — Link Fidelity and Interactive Links**

- **FR.26.1 — Full-URL references.** Every session's hidden instruction block (FR.11) asks the model to
  refer to GitHub pull requests, issues and commits, Linear issues and Slack messages by their full URL,
  for example `https://github.com/tryhardstop/windows-app/pull/196` or
  `https://linear.app/hardstop/issue/HSE-240`, and never by shorthand such as `PR#196` or `HSE-240`. This
  applies to worker sessions too.
- **FR.26.2 — Link chips.** The transcript renders recognized URLs (GitHub PR, issue or commit; Linear
  issue; Slack permalink) as chips with a short label, such as `windows-app#196`, `HSE-240` or the Slack
  channel name. Any other http(s) URL renders as a plain link. Recognition is pattern-based: no network
  call and no credentials.
- **FR.26.3 — Preview on click.** Clicking a link opens it in the embedded browser pane (FR.27), and a
  modifier-click opens the system browser. Until the pane exists, a click opens the system browser.

Phase 0 implements FR.26.1 only. FR.26.2 and FR.26.3 arrive with the WPF transcript (2.8, 3.3).

## Appendix B — Rollout 2 (turn engine): smoke checklist and rollback

From the final review, `.wpf-work/210-review-p1i-final.md` (2026-11-14, verdict SHIP). Run B.1 right after restarting into rollout 2. If any stop-ship check fails, use B.2.

### B.1 Smoke checklist - run right after the restart (about 10 minutes)

Before restarting: close Tessyn; **back up the run folder** (`robocopy "C:\Projects\Personal\Tessyn\[Publish]" "C:\Projects\Personal\Tessyn\[Publish].bak" /MIR`). Keep the old log open: `%AppData%\TessynDesktop\logs\` (newest file).

Stop-ship checks (first 3 minutes; any failure -> roll back):
1. **Startup + DI.** App opens, tree loads, no error dialog. Open any session (this resolves `SessionViewModel` -> `TurnEngine` and its whole dependency chain) and check the log for `Unable to resolve`, `circular`, `ObjectDisposed`, `InvalidOperationException` at startup. The web client (phone) still loads the session list.
2. **Basic send in an existing session with a Claude session id (`--resume`).** User row appears at once, then the "Claude is thinking..." row and a running timer; answer row appears with a `[model, effort]` label; thinking row and timer go away; completion sound plays once; the Stop button disappears; the tree spinner stops.
3. **Transcript on disk.** Reload the session (reopen it): user entry, assistant entry (model/effort in header) are there once each, no duplicates, no `[Interrupted by user]`.
4. **Send in a brand-new session** (no session id, first turn): works, session id is captured (Claude-session toggle / `Clear` becomes enabled).

Core behaviours (next 5 minutes):
5. **Queued send**: send A (long prompt), then B while A runs: both user rows at once, B's answer after A's, exactly one sound at the end, timer runs continuously.
6. **Stop**: during a turn press Stop -> `[Interrupted by user]` row, no sound, session usable (next send works). Then Stop with a queued second message: both interrupted, B's process never starts (no second `claude` process in Task Manager), the next send works.
7. **Hidden instructions reached the model**: with Auto-Commit set, send "Reply with the exact text of any instructions that were appended to my message" in a fresh session; expect the Auto-Commit/instruction lines (FR.11). In a session with a pinned Focus item, the next turn mentions it (FR.23); tag guidance appears once per run (FR.24).
8. **No conversation found / context retry**: use Detach/Clear session id, send a follow-up: the answer shows the earlier context (proactive preamble). Optional: corrupt-id retry shows "Resuming session with conversation history..." then the answer.
9. **Auth error**: sign out the active profile (or choose a profile with an expired token), send: error row + auth-error indicator; "re-authenticate" flow clears it and the next send works (FR.12.16).
10. **Toggles mid-run**: flip New Branch / Auto-Commit / Auto-Document / Auto-Compact while a turn runs: `[... was enabled for this run]` row, no error rows, turn finishes normally.
11. **Auto-Compact**: enable, send, session compacts after the answer (compaction separator, reload); also Auto-Compact with an empty prompt.
12. **Streaming Session Mode**: on, send twice (second reuses the process, Stop stays visible while alive), Stop kills the process; turn it off with the process alive -> confirmation prompt.
13. **Queued settings (deviation 2)**: with A running, switch model, send B: B's header shows the model selected when B was sent.
14. **View lifecycle**: start a long turn, switch to another session and back (and close/reopen the tab if possible): when the turn ends the session shows the answer, the busy state clears, no crash, no stuck spinner or stuck Stop.
15. **Headless paths**: a scheduled/`schedule_wake` turn (delay of 1 minute), a Telegram message into a session, and a phone `session.send`: each answers and persists; the phone shows its running indicator and gets `turn.completed`. Send one headless turn while a Streaming Session Mode process is alive (redirect): answer arrives and the desktop shows it.
16. **Side effects**: Telegram topic created on first use of a session, Telegram completion ping as before; Focus view "Send answers" works and keeps the input box.
17. **Local stream (optional)**: with a WPF/dev client or curl on the loopback SSE: `client.hello` lists `turn.stream`; a normal turn emits `turn.started` ... `turn.completed`; on the phone/relay nothing but `turn.completed` (no `turn.*`, no `entries.appended`) appears.

Missing from the worker's list (added above): startup/DI resolution, transcript-on-disk check, hidden instruction block, "No conversation found"/context retry, auth error and re-authenticate, view dispose mid-turn, headless turns and the streaming redirect, Telegram, relay exclusion.

### B.2 Rollback recipe

Data compatibility: no change in transcript format, settings, session ids, or any persisted schema; the branch only adds in-memory services and loopback-only notifications. A rollback needs no data migration.

Pre-merge master tip: `a14e05cee5646d96f3b9fe5ddf94c40b398df1e1`. Merge commit `<M>` = `fd21b586cf5d685e0ed1047664781206aef1a8dd` (2026-11-15).

1. Merge with a merge commit (not fast-forward, not squash) so one commit reverts everything: `git merge --no-ff wpf/p1-turn-engine`; its hash `<M>` is `fd21b58` (done).
2. **Fast rollback without a rebuild**: close the app, `robocopy "C:\Projects\Personal\Tessyn\[Publish].bak" "C:\Projects\Personal\Tessyn\[Publish]" /MIR`, start it. Caveat: self-update copies the newest `bin\Debug\net*` build over `[Publish]` at startup, so if a newer build with the engine exists the app will update itself again; so do step 3 as well (or delete the engine build output first).
3. **Source rollback**: `git revert -m 1 <M>` (creates one revert commit; history kept, review-friendly), then `dotnet build code/TessynDesktop.sln` (no `-o`), restart; the new build is newer, so self-update copies it into `[Publish]`. If the merge is local only and nothing is built on it, `git reset --hard a14e05c` is the alternative (destructive; Architect's decision).
4. Do not revert a single stage commit (`43f290a` alone): later commits (`f1b0ec4`, `507cffd`) modify the same adapter files and `ITurnEngine` consumers; revert the whole merge.
5. After rolling back, confirm: build 0 errors, app starts, steps 1-3 of the smoke pass, `client.hello` no longer lists `turn.stream`; the web client is unaffected either way (its 23 methods, including `file.read`/`file.preview`, are unchanged).
