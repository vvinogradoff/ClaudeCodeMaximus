# TC.WEB.01 — Web client (Tessyn Web)

Related requirements: FR.25.01–FR.25.40, and the amended FR.1.8, FR.1.17, FR.3.3, FR.11.6, FR.19.11 in [requirements.md](/docs/requirements.md). Related use cases: [UC.WEB.01](/docs/usecases/UC.WEB.01-Web-Client.md) (UC.WEB.01–UC.WEB.24).

**Legend.** AUTOMATED test projects and the real test class are named in each case (synced with the built code on 2026-11-13; the classes live in the `web-client` branch worktrees until merged):
- **Core** — xUnit in `code/TessynDesktop.Tests/Services` (daemon side; the Remote API is tested at wire level through `TdcpDispatcher` on the relay transport, temp data roots, fake CLI). Several planned classes were merged into fewer real ones; each case names the real class.
- **Server** — xUnit in `code/Tessyn.Web.Tests` (`WebApplicationFactory`, a fake daemon that opens `/link/stream` with the test client).
- **SPA** — Vitest unit tests in `code/Tessyn.Web/ClientApp/src/lib` (`lib.test.ts`, `markdown.test.ts`; 26 tests).
- **PW** — Playwright in `code/Tessyn.Web/ClientApp` (chromium, 390×844): `e2e/flows.spec.ts` (3 flows, mock build via `vite preview`, 23 screenshots reviewed by a person), `e2e-real/real.spec.ts` (the real Tessyn.Web server, `npm run test:e2e:real`) and `e2e-fullstack/fullstack.spec.ts` (the whole stack, started by `FullStackTests`).
- **E2E** — xUnit in `code/Tessyn.Web.E2E.Tests` (`StackReadFlowTests`, `StackTurnFlowTests`, `FullStackTests`: the real Relay Connector against an in-memory Tessyn.Web and the real Core daemon with a fake `claude` process, temp data root; the full-stack fact needs `TESSYN_FULLSTACK=1`).
- **MANUAL** — done by the Architect on the iPhone against the deployed site `https://tessyn.vvinogradoff.com`, with the real TessynDesktop.

Notation: `<n>` = node id (32 hex digits).

## Summary

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.WEB.01 | Dispatcher answers every input with a JSON-RPC response | AUTOMATED (Core) | UC.WEB.01 | FR.25.31 |
| TC.WEB.02 | Relay transport accepts only remote-callable methods | AUTOMATED (Core) | UC.WEB.18 | FR.25.06 |
| TC.WEB.03 | Event bus: publish, coalesce, never block or throw | AUTOMATED (Core) | UC.WEB.07 | FR.25.32 |
| TC.WEB.04 | Stable ids for Directory and Group nodes | AUTOMATED (Core) | UC.WEB.04 | FR.1.17, FR.25.07 |
| TC.WEB.05 | Parse rule: only real headers end an entry | AUTOMATED (Core) | UC.WEB.07 | FR.3.3 |
| TC.WEB.06 | Reference-counted running flag | AUTOMATED (Core) | UC.WEB.09 | FR.1.8, FR.25.19 |
| TC.WEB.07 | Session activity and state builder | AUTOMATED (Core) | UC.WEB.02 | FR.25.09 |
| TC.WEB.08 | Session epochs and `SessionFileRewritten` | AUTOMATED (Core) | UC.WEB.23 | FR.25.18, FR.25.34 |
| TC.WEB.09 | Notification publisher mapping | AUTOMATED (Core) | UC.WEB.07 | FR.25.32 |
| TC.WEB.10 | `client.hello` | AUTOMATED (Core) | UC.WEB.01 | FR.25.31 |
| TC.WEB.11 | `tree.snapshot` | AUTOMATED (Core) | UC.WEB.02 | FR.25.10, FR.25.07 |
| TC.WEB.12 | `tree.workers` | AUTOMATED (Core) | UC.WEB.02 | FR.25.10 |
| TC.WEB.13 | `tree.search` semantics | AUTOMATED (Core) | UC.WEB.03 | FR.25.11 |
| TC.WEB.14 | `tree.createSession` placement | AUTOMATED (Core) | UC.WEB.04 | FR.25.12 |
| TC.WEB.15 | `recent.list` and `recent.hide` | AUTOMATED (Core) | UC.WEB.05 | FR.25.14 |
| TC.WEB.16 | `tags.search` | AUTOMATED (Core) | UC.WEB.06 | FR.25.15 |
| TC.WEB.17 | `session.get` and `lastUsed` | AUTOMATED (Core) | UC.WEB.14 | FR.25.16, FR.25.24 |
| TC.WEB.18 | `session.entries` paging and truncation | AUTOMATED (Core) | UC.WEB.07 | FR.25.18 |
| TC.WEB.19 | `session.entry` and epoch mismatch | AUTOMATED (Core) | UC.WEB.23 | FR.25.18 |
| TC.WEB.20 | `session.send` accepted, queued, source | AUTOMATED (Core) | UC.WEB.08, UC.WEB.09 | FR.25.33 |
| TC.WEB.21 | Per-turn model, effort, profile | AUTOMATED (Core) | UC.WEB.08 | FR.25.33, FR.19.11 |
| TC.WEB.22 | Profile stamp on every headless USER entry | AUTOMATED (Core) | UC.WEB.14 | FR.25.33, FR.3.3 |
| TC.WEB.23 | `session.stop` | AUTOMATED (Core) | UC.WEB.10 | FR.25.20 |
| TC.WEB.24 | `session.compact` through the Core service | AUTOMATED (Core) | UC.WEB.16 | FR.25.26 |
| TC.WEB.25 | Compaction behaviour unchanged (FR.11.6) | AUTOMATED (Core) | UC.WEB.16 | FR.11.6, FR.25.26 |
| TC.WEB.26 | `session.setFavorite` | AUTOMATED (Core) | UC.WEB.14 | FR.25.23 |
| TC.WEB.27 | `session.setStreaming` confirm and force | AUTOMATED (Core) | UC.WEB.15 | FR.25.25 |
| TC.WEB.28 | `attention.list` | AUTOMATED (Core) | UC.WEB.11 | FR.25.21 |
| TC.WEB.29 | `attention.reply` (single and several) | AUTOMATED (Core) | UC.WEB.11, UC.WEB.12 | FR.25.22 |
| TC.WEB.30 | `attention.dismiss` | AUTOMATED (Core) | UC.WEB.13 | FR.25.22 |
| TC.WEB.31 | `catalog.get` | AUTOMATED (Core) | UC.WEB.14 | FR.25.23 |
| TC.WEB.32 | `usage.get` per profile | AUTOMATED (Core) | UC.WEB.17 | FR.25.27 |
| TC.WEB.33 | No secrets or paths on the wire | AUTOMATED (Core) | UC.WEB.02 | FR.25.07 |
| TC.WEB.34 | Connector reconnect and backoff | AUTOMATED (Core) | UC.WEB.18 | FR.25.36 |
| TC.WEB.35 | Connector heartbeat timeout | AUTOMATED (Core) | UC.WEB.18 | FR.25.36 |
| TC.WEB.36 | Connector dispatches only the allowlist | AUTOMATED (Core) | UC.WEB.18 | FR.25.06, FR.25.36 |
| TC.WEB.37 | Connector concurrency and event coalescing | AUTOMATED (Core) | UC.WEB.07 | FR.25.36 |
| TC.WEB.38 | Relay config store (DPAPI) | AUTOMATED (Core) | UC.WEB.22 | FR.25.36 |
| TC.WEB.39 | Connector start, stop, status without restart | AUTOMATED (Core) | UC.WEB.22 | FR.25.36, FR.25.37 |
| TC.WEB.40 | Login: success, wrong password, unknown email | AUTOMATED (Server) | UC.WEB.01 | FR.25.03 |
| TC.WEB.41 | Login throttling | AUTOMATED (Server) | UC.WEB.21 | FR.25.04 |
| TC.WEB.42 | Cookie required for `/api/rpc` and `/api/events` | AUTOMATED (Server) | UC.WEB.01, UC.WEB.20 | FR.25.03 |
| TC.WEB.43 | CSRF header on POSTs | AUTOMATED (Server) | UC.WEB.21 | FR.25.05 |
| TC.WEB.44 | Server allowlist | AUTOMATED (Server) | UC.WEB.18 | FR.25.06 |
| TC.WEB.45 | RPC relay through a fake daemon, id mapping | AUTOMATED (Server) | UC.WEB.02 | FR.25.35 |
| TC.WEB.46 | Offline `-32001` and timeout `-32002` | AUTOMATED (Server) | UC.WEB.18 | FR.25.29, FR.25.35 |
| TC.WEB.47 | Link token and supersede | AUTOMATED (Server) | UC.WEB.18, UC.WEB.22 | FR.25.06, FR.25.35 |
| TC.WEB.48 | Events fan-out, ids, first frame | AUTOMATED (Server) | UC.WEB.07 | FR.25.28 |
| TC.WEB.49 | `Last-Event-ID` replay and `resync` | AUTOMATED (Server) | UC.WEB.19 | FR.25.28, FR.25.29 |
| TC.WEB.50 | Security headers, `/healthz`, no-store | AUTOMATED (Server) | UC.WEB.01 | FR.25.05, FR.25.38 |
| TC.WEB.51 | Password hash format and verifier | AUTOMATED (Server) | UC.WEB.01 | FR.25.03 |
| TC.WEB.52 | CLI verbs `hash-password`, `new-link-token` | AUTOMATED (Server) | UC.WEB.22 | FR.25.03, FR.25.06, FR.25.38 |
| TC.WEB.53 | Startup validation and SPA fallback | AUTOMATED (Server) | UC.WEB.24 | FR.25.03, FR.25.30, FR.25.38 |
| TC.WEB.54 | Tree search projection | AUTOMATED (SPA) | UC.WEB.03 | FR.25.11 |
| TC.WEB.55 | Exact and Project toggle rules | AUTOMATED (SPA) | UC.WEB.03 | FR.25.11 |
| TC.WEB.56 | Event reducer | AUTOMATED (SPA) | UC.WEB.07 | FR.25.28 |
| TC.WEB.57 | Pending Bubble reconciliation | AUTOMATED (SPA) | UC.WEB.08, UC.WEB.09 | FR.25.19 |
| TC.WEB.58 | Recency bands | AUTOMATED (SPA) | UC.WEB.02 | FR.25.09 |
| TC.WEB.59 | Markdown sanitising | AUTOMATED (SPA) | UC.WEB.07 | FR.25.17 |
| TC.WEB.60 | Resync and epoch handling | AUTOMATED (SPA) | UC.WEB.19, UC.WEB.23 | FR.25.18, FR.25.29 |
| TC.WEB.61 | Flow: login, tree, open session | AUTOMATED (PW) | UC.WEB.01, UC.WEB.02 | FR.25.03, FR.25.08–FR.25.10 |
| TC.WEB.62 | Flow: search and ancestor expansion | AUTOMATED (PW) | UC.WEB.03 | FR.25.11 |
| TC.WEB.63 | Flow: create session | AUTOMATED (PW) | UC.WEB.04 | FR.25.12 |
| TC.WEB.64 | Flow: send, thinking, queue, stop | AUTOMATED (PW) | UC.WEB.07–UC.WEB.10 | FR.25.17–FR.25.20 |
| TC.WEB.65 | Flow: Focus reply, send answers, dismiss | AUTOMATED (PW) | UC.WEB.11–UC.WEB.13 | FR.25.21, FR.25.22 |
| TC.WEB.66 | Flow: settings sheet, locking, streaming confirm | AUTOMATED (PW) | UC.WEB.14, UC.WEB.15 | FR.25.23–FR.25.25 |
| TC.WEB.67 | Flow: Recent and Tags | AUTOMATED (PW) | UC.WEB.05, UC.WEB.06 | FR.25.14, FR.25.15 |
| TC.WEB.68 | Layout screenshots 390×844 and 1280×800, manifest | AUTOMATED (PW) | UC.WEB.24 | FR.25.08, FR.25.30 |
| TC.WEB.69 | End to end: login to Focus reply | AUTOMATED (E2E) | UC.WEB.01–UC.WEB.13 | FR.25.01–FR.25.22 |
| TC.WEB.70 | End to end: offline and reconnect | AUTOMATED (E2E) | UC.WEB.18 | FR.25.29, FR.25.35, FR.25.36 |
| TC.WEB.71 | End to end: running flag from a desktop turn and web turn on the desktop | AUTOMATED (E2E) | UC.WEB.07, UC.WEB.09 | FR.1.8, FR.25.19, FR.25.33 |
| TC.WEB.72 | Install on the home screen | MANUAL | UC.WEB.24 | FR.25.08, FR.25.30 |
| TC.WEB.73 | Portrait layout, keyboard, safe areas | MANUAL | UC.WEB.02, UC.WEB.08 | FR.25.08, FR.25.20 |
| TC.WEB.74 | Login on the phone and log out | MANUAL | UC.WEB.01, UC.WEB.20 | FR.25.03 |
| TC.WEB.75 | Follow a live turn on the phone | MANUAL | UC.WEB.07–UC.WEB.10 | FR.25.17–FR.25.20 |
| TC.WEB.76 | Focus on the phone | MANUAL | UC.WEB.11–UC.WEB.13 | FR.25.21, FR.25.22 |
| TC.WEB.77 | Create session on the phone, check the desktop tree | MANUAL | UC.WEB.04 | FR.25.12 |
| TC.WEB.78 | Settings, Compact and usage on the phone | MANUAL | UC.WEB.14–UC.WEB.17 | FR.25.23–FR.25.27 |
| TC.WEB.79 | Background and resume | MANUAL | UC.WEB.19 | FR.25.28, FR.25.29 |
| TC.WEB.80 | Desktop offline and back | MANUAL | UC.WEB.18 | FR.25.29, FR.25.36 |
| TC.WEB.81 | Desktop Settings "Web access" | MANUAL | UC.WEB.22 | FR.25.37 |
| TC.WEB.82 | Deployed site: TLS, headers, throttling | MANUAL | UC.WEB.21 | FR.25.04, FR.25.05, FR.25.38 |
| TC.WEB.83 | Search, Recent and Tags on the real tree | MANUAL | UC.WEB.03, UC.WEB.05, UC.WEB.06 | FR.25.11, FR.25.14, FR.25.15 |
| TC.WEB.84 | Remote tree performance on a synthetic 3,000-session tree (read counts) | AUTOMATED (Core) | UC.WEB.02 | FR.25.10, FR.25.40 |
| TC.WEB.85 | Cookie: `__Host-` attributes, idle and absolute lifetime | AUTOMATED (Server) | UC.WEB.01, UC.WEB.20 | FR.25.03 |
| TC.WEB.86 | Revocation: logout logs out every device, password change invalidates | AUTOMATED (Server) | UC.WEB.20 | FR.25.03 |
| TC.WEB.87 | Throttle is atomic and bounded (parallel guesses, IPv6 /64, sweep) | AUTOMATED (Server) | UC.WEB.21 | FR.25.04 |
| TC.WEB.88 | Trusted-device cookie and the global-limit exemption | AUTOMATED (Server) | UC.WEB.21 | FR.25.03, FR.25.04 |
| TC.WEB.89 | Request body limits | AUTOMATED (Server) | UC.WEB.21 | FR.25.05 |
| TC.WEB.90 | Public vs loopback `/healthz` | AUTOMATED (Server) | UC.WEB.01 | FR.25.05, FR.25.38 |
| TC.WEB.91 | CSP forbids form posts, base URI and plugins | AUTOMATED (Server) | UC.WEB.21 | FR.25.05 |
| TC.WEB.92 | Markdown hardening and tag URL policy | AUTOMATED (SPA) | UC.WEB.07 | FR.25.05, FR.25.17 |
| TC.WEB.93 | Logout failure handling and draft clearing | AUTOMATED (PW real) | UC.WEB.20 | FR.25.05 |
| TC.WEB.94 | Compaction outcome SYSTEM entries on the remote path | AUTOMATED (Core) | UC.WEB.16 | FR.25.26 |
| TC.WEB.95 | Real server: login, desktop offline, logout really ends the session | AUTOMATED (PW real) | UC.WEB.01, UC.WEB.20 | FR.25.03, FR.25.29 |
| TC.WEB.96 | Full stack: the SPA against the real relay, daemon and fake claude | AUTOMATED (E2E, `TESSYN_FULLSTACK=1`) | UC.WEB.01–UC.WEB.17 | FR.25.01–FR.25.27 |
| TC.WEB.97 | First positive login on the live site, device cookie, logout everywhere | MANUAL | UC.WEB.01, UC.WEB.20 | FR.25.03 |
| TC.WEB.98 | Changing the password logs out every device | MANUAL | UC.WEB.20 | FR.25.03 |
| TC.WEB.99 | Link token rotation (server hash + desktop Settings) | MANUAL | UC.WEB.22 | FR.25.06, FR.25.37 |
| TC.WEB.100 | Redeploy with `redeploy.sh`; service user, permissions, sandbox | MANUAL | UC.WEB.24 | FR.25.38 |
| TC.WEB.101 | Desktop: favourite, streaming and Recent hide from the phone refresh the desktop | MANUAL | UC.WEB.14, UC.WEB.15 | FR.25.25 |
| TC.WEB.102 | Desktop: new session from the web appears at the right place; agent spawn still appends | MANUAL | UC.WEB.04 | FR.25.12 |
| TC.WEB.103 | Desktop: interactive turn shows running on the web, back to idle (also two queued sends) | MANUAL | UC.WEB.09 | FR.1.8, FR.25.19 |
| TC.WEB.104 | Desktop: scheduled or Telegram turn shows the profile name on its USER bubble; Focus Reply and Send answers unchanged | MANUAL | UC.WEB.11, UC.WEB.12 | FR.25.33 |
| TC.WEB.105 | Desktop: auto-compact (with and without a prompt, forced failure) unchanged | MANUAL | UC.WEB.16 | FR.11.6, FR.25.26 |

## FR → TC coverage

| FR | Test cases |
|---|---|
| FR.1.8 (amended) | TC.WEB.06, TC.WEB.71 |
| FR.1.17 | TC.WEB.04, TC.WEB.14 |
| FR.3.3 (amended) | TC.WEB.05, TC.WEB.22 |
| FR.11.6 (amended) | TC.WEB.24, TC.WEB.25 |
| FR.19.11 (amended) | TC.WEB.21, TC.WEB.22 |
| FR.25.01 | TC.WEB.69, TC.WEB.80 |
| FR.25.02 | TC.WEB.69 |
| FR.25.03 | TC.WEB.40, TC.WEB.42, TC.WEB.51, TC.WEB.52, TC.WEB.53, TC.WEB.61, TC.WEB.74, TC.WEB.85, TC.WEB.86, TC.WEB.88, TC.WEB.95, TC.WEB.97, TC.WEB.98 |
| FR.25.04 | TC.WEB.41, TC.WEB.82, TC.WEB.87, TC.WEB.88 |
| FR.25.05 | TC.WEB.43, TC.WEB.50, TC.WEB.82, TC.WEB.89, TC.WEB.90, TC.WEB.91, TC.WEB.92, TC.WEB.93 |
| FR.25.06 | TC.WEB.02, TC.WEB.36, TC.WEB.44, TC.WEB.47, TC.WEB.52, TC.WEB.99 |
| FR.25.07 | TC.WEB.04, TC.WEB.11, TC.WEB.33 |
| FR.25.08 | TC.WEB.61, TC.WEB.68, TC.WEB.72, TC.WEB.73 |
| FR.25.09 | TC.WEB.07, TC.WEB.58, TC.WEB.61 |
| FR.25.10 | TC.WEB.11, TC.WEB.12, TC.WEB.61, TC.WEB.84 |
| FR.25.11 | TC.WEB.13, TC.WEB.54, TC.WEB.55, TC.WEB.62, TC.WEB.83 |
| FR.25.12 | TC.WEB.14, TC.WEB.63, TC.WEB.77, TC.WEB.102 |
| FR.25.13 | TC.WEB.63, TC.WEB.61 |
| FR.25.14 | TC.WEB.15, TC.WEB.67, TC.WEB.83 |
| FR.25.15 | TC.WEB.16, TC.WEB.67, TC.WEB.83 |
| FR.25.16 | TC.WEB.17, TC.WEB.64 |
| FR.25.17 | TC.WEB.59, TC.WEB.64, TC.WEB.75, TC.WEB.92 |
| FR.25.18 | TC.WEB.08, TC.WEB.18, TC.WEB.19, TC.WEB.60 |
| FR.25.19 | TC.WEB.06, TC.WEB.57, TC.WEB.64, TC.WEB.71, TC.WEB.75, TC.WEB.103 |
| FR.25.20 | TC.WEB.23, TC.WEB.64, TC.WEB.73, TC.WEB.75 |
| FR.25.21 | TC.WEB.28, TC.WEB.65, TC.WEB.76 |
| FR.25.22 | TC.WEB.29, TC.WEB.30, TC.WEB.65, TC.WEB.76 |
| FR.25.23 | TC.WEB.26, TC.WEB.31, TC.WEB.66, TC.WEB.78 |
| FR.25.24 | TC.WEB.17, TC.WEB.21, TC.WEB.66, TC.WEB.78 |
| FR.25.25 | TC.WEB.27, TC.WEB.66, TC.WEB.101 |
| FR.25.26 | TC.WEB.24, TC.WEB.25, TC.WEB.78, TC.WEB.94, TC.WEB.105 |
| FR.25.27 | TC.WEB.32, TC.WEB.78 |
| FR.25.28 | TC.WEB.48, TC.WEB.49, TC.WEB.56, TC.WEB.79 |
| FR.25.29 | TC.WEB.46, TC.WEB.49, TC.WEB.60, TC.WEB.70, TC.WEB.79, TC.WEB.80 |
| FR.25.30 | TC.WEB.53, TC.WEB.68, TC.WEB.72 |
| FR.25.31 | TC.WEB.01, TC.WEB.10 |
| FR.25.32 | TC.WEB.03, TC.WEB.09 |
| FR.25.33 | TC.WEB.20, TC.WEB.21, TC.WEB.22, TC.WEB.71, TC.WEB.104 |
| FR.25.34 | TC.WEB.08, TC.WEB.24 |
| FR.25.35 | TC.WEB.45, TC.WEB.46, TC.WEB.47, TC.WEB.70 |
| FR.25.36 | TC.WEB.34–TC.WEB.39, TC.WEB.70, TC.WEB.80 |
| FR.25.37 | TC.WEB.39, TC.WEB.81, TC.WEB.99 |
| FR.25.38 | TC.WEB.50, TC.WEB.52, TC.WEB.53, TC.WEB.82, TC.WEB.90, TC.WEB.100 |
| FR.25.39 | no test (a list of exclusions); TC.WEB.61 and TC.WEB.63 check that Add directory and context menus are absent |
| FR.25.40 | TC.WEB.84 (performance); the other items are known limits and follow-ups with no test |

---

## Core tests (daemon side)

**TC.WEB.01 — Dispatcher answers every input with a JSON-RPC response.** Class `TdcpDispatcherTests`. Send: malformed JSON, a non-object, a missing `method`, an unknown method, a method with params of the wrong type, a handler that throws `TdcpException`, a handler that throws `InvalidOperationException`. Expected: a full JSON-RPC response each time — `-32700`, `-32600`, `-32600`, `-32601`, `-32602`, `-32000` (message is the exception's), `-32603` (generic message, the original is only logged); the dispatcher never throws. The existing TDCP methods keep working on the loopback transport.

**TC.WEB.02 — Relay transport accepts only remote-callable methods.** Classes `TdcpDispatcherTests` (and E2E flow `F12a`/`F12b` in `StackReadFlowTests`). Call a loopback-only method (e.g. `tree.list`) with `TdcpTransport.Relay`: `-32601`. The same call on `Loopback` works. Each of the 21 methods of FR.25.31 is accepted on `Relay`.

**TC.WEB.03 — Event bus.** Class `RemoteEventBusTests`. `Publish` raises `Published` with the camelCase params. `PublishCoalesced` with the same key within the window raises one event, and the payload factory runs once, after the window, so the payload shows the latest state. A throwing subscriber or factory does not throw into the publisher; a slow subscriber does not block `Publish`.

**TC.WEB.04 — Stable ids for Directory and Group nodes.** Classes `SessionRegistryRemoteTests` (and `SessionRegistryTests`). A tree loaded from `appsettings.json` without ids: every Directory and Group node gets a 32-hex id and the settings are saved once; a second load assigns nothing and saves nothing. Nodes created by Add directory, Add group, Add group to group and the agent session creation have ids. Ids are unique and unchanged after reload. Registry lookup by id returns the node (and null for an unknown id).

**TC.WEB.05 — Parse rule: only real headers end an entry.** Class `SessionFileServiceRemoteTests`. A body line starting with `[Tool: Read] x`, `[Scheduled] …`, `[Interrupted by user]`, `[link](http://x)`, `[2026-13-45T99:00:00Z] USER` (invalid timestamp) and `[2026-11-12T10:00:00Z] FOO` (unknown role) stays in the entry body. A real `[2026-11-12T10:00:00Z] ASSISTANT model="x" effort="high"` line splits the entry. A SYSTEM entry `[Attention A3 (ask)] title` followed by body lines keeps its full text. The existing `JsonlWriteBackPlanner` tests still pass.

**TC.WEB.06 — Reference-counted running flag.** Classes `SessionRegistryRemoteTests` (the reference counting), `SessionTurnServiceTests` (the headless turn). `SetSessionRunning(true)` twice then `false` once: still running. A second `false`: not running. A third `false` stays at 0. The change event fires only on 0→1 and 1→0. `TurnController` (or its seam) reports true when its busy count goes 0→1 and false when it returns to 0, so a desktop-interactive turn sets the flag.

**TC.WEB.07 — Session activity and state builder.** Classes `SessionActivityServiceTests`, `SessionStateBuilderTests`. Activity is the latest of last prompt, last response and last non-blank draft edit; the profile comes from the last USER entry; a blank draft does not set `hasDraft`; the cache is invalidated when the file's length or write time changes; the tail parse yields the same prompt time as a full parse. `SessionStateDto`: running from the registry, `streamingAlive` from the streaming manager, `attention` omitted when the all-open count is 0 and carrying the top kind (`Decided` included) otherwise, favourite flag, null fields omitted in JSON.

**TC.WEB.08 — Session epochs and `SessionFileRewritten`.** Classes `RemoteTrackersTests` (the epoch tracker), `SessionFileServiceRemoteTests` (`SessionFileRewritten`). The epoch starts at 0; `RewriteSessionFile`, `WriteSessionFile` and the compaction repair each raise `SessionFileRewritten { FileName }` and add 1; an append does not; two files have independent counters.

**TC.WEB.09 — Notification publisher mapping.** Class `RemoteNotificationPublisherTests`. Each `IDaemonClient` event produces its notification of FR.25.32 with the right params: `tree.changed { version }` (version up on structural changes only, debounced), `session.changed { state }`, `session.appended`/`session.reset { nodeId, epoch }`, `turn.completed { nodeId, cancelled, error? }` (node id resolved from the file name), `attention.changed { nodeId, summary }`, `tags.changed`, `index.changed` (debounced 2 s). A burst of 100 appends yields at most a few events.

**TC.WEB.10 — `client.hello`.** Classes `TdcpDispatcherTests` (`client.hello` through `ClientRpcModule`). Returns `protocolVersion: 1`, the daemon version, the machine name, a `runId` that is the same within a run and differs between two service instances, and the current UTC time.

**TC.WEB.11 — `tree.snapshot`.** Class `TreeRemoteRpcModuleTests`. Directories, groups and top-level sessions in model order; a worker is not listed and is counted in its supervisor's `workerCount`; an orphan worker is listed at top level; `isExpanded` comes from the model; `sinceVersion` equal to the current version returns `unchanged: true` without the tree; the version rises on structural changes and not on a running-state change; nothing in the JSON is a path, token or file name (see TC.WEB.33); the snapshot is built under the registry lock (a concurrent mutation test does not throw).

**TC.WEB.12 — `tree.workers`.** Class `TreeRemoteRpcModuleTests`. Returns the workers of a supervisor in model order with `supervisorId` set; unknown id → `-32000 Session not found`; a session without workers → an empty list.

**TC.WEB.13 — `tree.search` semantics.** Classes `TreeRemoteRpcModuleTests`, `TreeSearchRankerTests`. Blank query → no matches. `exact: null`: `https://…` and a 7-character query switch exact on (`autoExact`); an 8-character query does not. `exact: true` keeps strict matches only. A match of a worker makes its top-level supervisor a match with the worker in `workerIds`; the supervisor's key is the best of its own and its workers'. Ranking: strict, then loose, then last prompt time. `scopeNodeId` limits to the project slug of that session's working directory. A failing index uses the file scan and sets `usedFallback`. Typed punctuation and e-mail addresses never raise an error.

**TC.WEB.14 — `tree.createSession` placement.** Classes `TreeRemoteRpcModuleTests`, `SessionRegistryCreateAfterTests`. Anchor session `S2` in `[S1, S2, S3]` → `[S1, S2, new, S3]`. Anchor group → index 0 of the group's sessions; anchor directory → index 0 of the directory's sessions. A worker anchor resolves to its top-level supervisor. Name is trimmed; empty or whitespace → `-32602`. Unknown anchor → `-32000`. One `Save()` per call. `TreeChanged` of kind `SessionAdded` carries the parent and index. The result has `directoryId` and, for a group, `groupId`. The new session has the defaults of a desktop-created session.

**TC.WEB.15 — `recent.list` and `recent.hide`.** Classes `RecentRemoteRpcModuleTests`, `RecentSessionVisibilityRulesTests`. A favourite session, one with an open `Decided` item and one with a schedule are listed even when hidden or older than 24 hours (`pinned: true`, `canHide: false`). A session active 23 hours ago is listed; one active 25 hours ago is not; a hidden one is not; workers are not listed. Order: last activity descending, no-activity last. `recent.hide` sets `RecentIgnoredAt` and returns `hidden: true`; for a pinned session it returns `hidden: false` and changes nothing.

**TC.WEB.16 — `tags.search`.** Class `TagsRemoteRpcModuleTests`. Same cases as TC.TG.29–36 through the RPC: scope by the selected session's directory, matching by label or key with every part, chip order (selected first, then kind, last activity, label), a selected tag with no sessions kept as a stub, session list = OR of selected tags with the worker's tags rolled up to its supervisor, `emptyReason` and `sessionsEmptyReason` values, only URLs allowed by `SessionTagUrlPolicy`.

**TC.WEB.17 — `session.get` and `lastUsed`.** Classes `SessionRpcModuleTests`, `LastUsedSettingsResolverTests`. The result has the location (`directoryLabel`, `treePath`), `supervisorId` for a worker, tags (declared and name-derived), the attention summary (open count without `Decided`, all-open count), `isStreamingMode`, `entryCount` and `epoch`. `lastUsed`: model and effort from the last ASSISTANT entry that has them; profile from the last USER entry's `profile=`, a display name mapped to its `ProfileId`; an unknown name or one starting with `[Sys]` → `default`; a session with no such entries → all `default`.

**TC.WEB.18 — `session.entries` paging and truncation.** Classes `SessionRpcModuleTests`, `SessionEntryReaderTests`. No index → the last `limit`; `beforeIndex` → the `limit` entries before it; `afterIndex` → entries after it, ascending; default limit 50, maximum 200 (larger values are cut). Empty entries keep their index. An entry over 32,768 characters is cut with `truncated: true` and `fullLength`. The result carries `total` and `epoch`. A cached parse is reused while (length, write time) are unchanged and rebuilt when they change. Role names are `user`, `assistant`, `system`, `compaction`; `profileName`, `modelId`, `effort` come from the header metadata.

**TC.WEB.19 — `session.entry` and epoch mismatch.** Classes `SessionRpcModuleTests`, `SessionEntryReaderTests`. Full content of the entry at an index; after the file was rewritten (epoch +1) a call with the old epoch fails with `-32000 "Session history changed"`; an index beyond the end → `-32602`.

**TC.WEB.20 — `session.send` accepted, queued, source.** Class `SessionRpcModuleTests`. Returns at once with `accepted: true, queued: false` and starts the turn through `RunTurnAsync` with `TurnSource.Web` (fake CLI). While a turn runs (or the running flag is set by a desktop turn) a second send returns `queued: true` and starts after the first. Empty text → `-32602`; unknown session → `-32000`. A turn failure becomes a SYSTEM entry and `turn.completed { error }`. The headless path redirects into an alive streaming process (FR.19.13).

**TC.WEB.21 — Per-turn model, effort, profile.** Class `SessionTurnServiceTests`. `modelId: "default"` → no `--model` flag; a model id → `--model <id>`; absent → the existing resolution. `effort: "high"` → `--effort high` and the first ASSISTANT entry carries `effort="high"`; `default` or absent → no flag. `profileId` → the config directory of that profile for the CLI and its display name for the stamp; `default` → the default profile. Options are ignored (not an error) when the turn is redirected into an alive process.

**TC.WEB.22 — Profile stamp on every headless USER entry.** Class `SessionTurnServiceTests`. The USER entry of a Telegram, scheduled, orchestrated and web turn carries `profile="<display name>"` of the profile actually used (the `[Sys] <email>` label for the default profile). `lastUsed` (TC.WEB.17) then returns that profile.

**TC.WEB.23 — `session.stop`.** Class `SessionRpcModuleTests`. With a Persistent Session Process alive: it is stopped and `stopped: true`. With a running one-shot turn: the turn is cancelled. Nothing running → `stopped: false`. `turn.completed { cancelled: true }` is published.

**TC.WEB.24 — `session.compact` through the Core service.** Class `SessionCompactRpcModuleTests`. Returns `accepted: true`; `queued: true` while another turn holds the lock. The running flag is set during the run and cleared after. Success rewrites the file and raises `SessionFileRewritten` (epoch +1); the model, effort and profile options are applied to the compaction call.

**TC.WEB.25 — Compaction behaviour unchanged.** Class `SessionCompactionTests`. The existing FR.11.6 cases run against the Core service: transcript embedded and no `--resume`; `[Nothing to compact]` guard with no CLI call; failure safety (an error event or an empty result leaves file and JSONL untouched, with the `[Compaction failed — …]` / `[Compaction returned no content …]` SYSTEM entry); success detaches the JSONL session. The desktop's Auto-Compact and the RPC use the same service instance type.

**TC.WEB.26 — `session.setFavorite`.** Class `SessionRpcModuleTests`. Sets and clears `IsFavorite` under the registry lock with one save; `SessionSettingsChanged` is raised; `session.changed` follows; unknown session → `-32000`.

**TC.WEB.27 — `session.setStreaming` confirm and force.** Class `SessionRpcModuleTests`. Turning on: saved, `needsConfirm: false`. Turning off with no process: saved. Turning off with a live process and no `force`: nothing changes, `needsConfirm: true`. With `force`: the process is stopped and the setting is off. `SessionSettingsChanged` is raised on every change.

**TC.WEB.28 — `attention.list`.** Class `AttentionRpcModuleTests`. Items in `AttentionOrdering` order (open by kind then number, then closed newest first); each item has id (`A12`), number, kind, title, body, options, recommendation, status, answer, resolution note and times; the summary's `openCount` excludes `Decided`, `allOpenCount` includes it.

**TC.WEB.29 — `attention.reply` (single and several).** Class `AttentionRpcModuleTests`. One answer: the item is marked answered **before** the turn starts, the prompt is built with the title resolved by the daemon, the turn starts with `TurnSource.Web`, and the result is `answeredIds: [id]`. Several answers: one prompt, one turn. An already closed or unknown item is in `skippedIds`; if all are skipped no turn starts and `turnStarted` is false. The output equals the desktop's `FocusViewModel.SendCards` for the same input (shared service).

**TC.WEB.30 — `attention.dismiss`.** Class `AttentionRpcModuleTests`. An open item becomes Dismissed, the SYSTEM entry `[Attention A12 dismissed by the user]` is written, no turn starts, `dismissed: true`; a closed item → `dismissed: false`; `attention.changed` is published.

**TC.WEB.31 — `catalog.get`.** Classes `CatalogRpcModuleTests` (file `CatalogAndUsageTests.cs`). Models: `default` first, then the cached models with id, display name and provider; efforts exactly `default, max, high, medium, low`; profiles: `default` first (display name `[Sys] <email>` or `Default`), then the configured profiles with id and display name — never a config directory.

**TC.WEB.32 — `usage.get` per profile.** Classes the usage tests in `CatalogAndUsageTests.cs`. Returns the 5-hour and 7-day windows (`utilization`, `resetsAtUtc`, `isStale`) for the requested profile; the second call within 5 minutes uses the cache; two profiles have separate caches; the desktop's active profile is not changed; `state` is `pending`, `unauthorized` or `unavailable` in the failure cases; `usage.changed { profileId }` is published when new data arrives.

**TC.WEB.33 — No secrets or paths on the wire.** Classes `TreeRemoteRpcModuleTests` (no `AgentToken`, file name or path in `tree.snapshot`) and E2E flow `F02` in `StackReadFlowTests`. Serialise the result of every remote method for a fixture tree whose sessions have an `AgentToken`, `ClaudeSessionId`, file names, a profile config directory and a Windows path in the directory node: no serialised result contains any of those strings or a `\` path separator; ids are 32 lowercase hex digits; the JSON is camelCase with no nulls.

**TC.WEB.34 — Connector reconnect and backoff.** Classes `RelayConnectorServiceTests` (against a loopback link server on `HttpListener`; delays injectable). When the relay refuses or drops the stream the Connector retries after 1, 2, 5, 10 and then 30 seconds (the delays are injectable); after 60 seconds of a good connection the backoff resets to 1 second.

**TC.WEB.35 — Connector heartbeat timeout.** Class `RelayConnectorServiceTests`. A stream that sends nothing for 45 seconds (injectable) is treated as dead and reconnected; pings keep it alive.

**TC.WEB.36 — Connector dispatches only the allowlist.** Class `RelayConnectorServiceTests`. A request frame for a remote-callable method is answered with the result; one for a loopback-only method is answered with `-32601` and no handler runs.

**TC.WEB.37 — Connector concurrency and event coalescing.** Class `RelayConnectorServiceTests`. With 20 slow requests at once at most 8 run at the same time and all 20 are answered; replies go to `/link/reply` as a JSON array; ten notifications within 250 ms arrive in one `/link/events` POST; notifications produced while the link is down are dropped.

**TC.WEB.38 — Relay config store (DPAPI).** Class `RelayConfigStoreTests`. Write then read returns the same `{ enabled, url, token }`; the file bytes contain neither the token nor the URL; a missing file, a file with garbage and a file encrypted for another scope all read as disabled without an exception. (Windows only.)

**TC.WEB.39 — Connector start, stop, status without restart.** Class `RelayConnectorServiceTests`. Applying an enabled config starts the Connector and the status goes Connecting → Connected; applying a disabled config stops it (the stream is closed) and the status is Disabled; changing the URL or token restarts it; a wrong token gives Failed with a short reason and no exception; the app start path never throws when the config is missing or broken.

## Server tests (`Tessyn.Web.Tests`)

**TC.WEB.40 — Login: success, wrong password, unknown email.** Class `AuthTests`. Correct credentials → 200 `{ email }` and a `.Tessyn.Auth` cookie with HttpOnly, Secure, SameSite=Strict; wrong password and unknown email → 401 with the same body; `GET /api/auth/me` → 200 with the cookie, 401 without; logout → 204 and the cookie no longer works.

**TC.WEB.41 — Login throttling.** Classes `LoginThrottleTests`, `AuthTests`, `AuthHardeningTests`. Five failures from one IP in 15 minutes → the sixth gets 429 with `Retry-After`, also with the right password; another IP is not affected until 20 failures in an hour server-wide, then all get 429; after the window (injected clock) login works again. The `X-Forwarded-For` header is honoured only from 127.0.0.1.

**TC.WEB.42 — Cookie required for `/api/rpc` and `/api/events`.** Class `AuthTests`. Without the cookie both return 401 (no redirect); with it they work.

**TC.WEB.43 — CSRF header on POSTs.** Classes `AuthTests` (`Posts_without_the_client_header_are_400`), `AuthHardeningTests` (login and logout without the header). `POST /api/rpc`, `/api/auth/login` and `/api/auth/logout` without `X-Tessyn-Client: web` → 400; with it → normal. No CORS headers are sent, and a request with a foreign `Origin` gets none.

**TC.WEB.44 — Server allowlist.** Classes `RpcRelayTests` (`Allowlist_is_exactly_the_21_contract_methods`, `Non_allowlisted_methods_get_method_not_found_and_never_reach_the_daemon`). Each of the 21 methods is forwarded; `tree.list`, `turn.run`, an unknown name and a missing method give `-32601` and nothing reaches the fake daemon.

**TC.WEB.45 — RPC relay through a fake daemon, id mapping.** Class `RpcRelayTests`. The fake daemon connects to `/link/stream` with the token; a browser `POST /api/rpc` with id `"abc"` arrives at the daemon as a request with a server id `r-<n>`; the fake posts the reply to `/link/reply`; the browser gets the result with id `"abc"`. Two concurrent requests are matched correctly even if replies come in reverse order; a reply batch with several responses works.

**TC.WEB.46 — Offline `-32001` and timeout `-32002`.** Class `RpcRelayTests`. No link → `-32001`. A request the fake never answers fails with `-32002` after the configured timeout (shortened in tests); the entries/snapshot/search methods use the longer limit; late replies after the timeout are dropped without error.

**TC.WEB.47 — Link token and supersede.** Classes `LinkEndpointTests`, `RpcRelayTests` (supersede). A wrong or missing token on `/link/stream`, `/link/reply`, `/link/events` → 401; a correct one works; the comparison is on the SHA-256 (the config holds only the hash). A second link connection ends the first stream and fails its pending requests with `-32001`; a stale reply from the old link is ignored.

**TC.WEB.48 — Events fan-out, ids, first frame.** Class `EventsTests`. The first frame of `/api/events` is `link.status` with the current state; events posted to `/link/events` reach every connected browser with increasing `id:`; link connect and disconnect publish `link.status`; pings arrive; a client disconnect ends the writer.

**TC.WEB.49 — `Last-Event-ID` replay and `resync`.** Class `EventsTests`. Reconnecting with the last seen id replays only newer events; an id older than the buffer (more than 1,000 events or 10 minutes) gets `event: resync`; an unknown future id gets `resync`.

**TC.WEB.50 — Security headers, `/healthz`, no-store.** Class `HostingTests`. The CSP, `X-Content-Type-Options`, `Referrer-Policy` of FR.25.05 on responses; `Cache-Control: no-store` on `/api/*`; `GET /healthz` without a cookie → `{ ok: true, linkOnline }` and nothing else.

**TC.WEB.51 — Password hash format and verifier.** Class `PasswordHasherTests`. A produced hash matches `pbkdf2-sha256$600000$<salt>$<hash>` (16-byte salt, 32-byte hash); verify accepts the right password and rejects others, a different salt gives a different hash for the same password, a malformed or unknown-scheme stored value is rejected without an exception.

**TC.WEB.52 — CLI verbs.** Classes `CliTests`, `ProcessTests`. `hash-password` reads stdin and prints one hash line (exit 0); empty input → non-zero. `new-link-token` prints a 43-character base64url token and its SHA-256 hex; two runs differ; the hash matches what the server accepts (TC.WEB.47).

**TC.WEB.53 — Startup validation and SPA fallback.** Classes `OptionsValidatorTests`, `ProcessTests` (`Starting_without_credentials_exits_non_zero_with_a_clear_message`), `HostingTests` (SPA fallback). A missing password hash or link token hash → a clear error and a non-zero exit. A GET of an unknown non-API path returns `index.html`; `/api/unknown` does not; with no `wwwroot/index.html` the built-in "SPA not built" page is served (no crash).

## SPA unit tests (Vitest, `ClientApp`)

**TC.WEB.54 — Tree search projection.** File `src/lib/lib.test.ts` (search projection: `shows only matches and ancestors`, `orders directories, groups and sessions by rank`, `nests matching workers and reports unloaded ones`, `ignores matches that are not in the tree`). Given a tree and a match list, only matches, their ancestors and nested matching workers are visible; ancestors are expanded regardless of stored state and the stored state is not modified; ordering follows rank (a container by its best descendant); an empty match list shows nothing; clearing restores the normal projection.

**TC.WEB.55 — Exact and Project toggle rules.** File `src/lib/lib.test.ts` (`auto-on for https:// and short queries`, `unticking holds until the text changes`, `manual choice persists for non-auto queries`, `scope toggles default ON when a session is selected and OFF when none`). Exact auto-on for `https://…` and for length < 8, off otherwise; a manual untick holds until the text changes; the manual choice persists otherwise. Project: on when a session becomes selected, off and disabled when none; the request carries `scopeNodeId` only when on.

**TC.WEB.56 — Event reducer.** File `src/lib/lib.test.ts` (`overlays state and tracks runningSince for the open session`, `updates other sessions only in the overlay`, `plans session.appended / session.reset handling`). `session.changed` updates the row state; `session.appended` schedules fetching after the last index; `session.reset` or a new epoch clears entries; `attention.changed` updates the badge; `tree.changed` triggers a snapshot with `sinceVersion`; `link.status` toggles the offline banner; unknown events are ignored.

**TC.WEB.57 — Pending Bubble reconciliation.** File `src/lib/lib.test.ts` (`removes a pending message once a matching USER entry arrives`, `keeps non-matching and older-than-base entries`, `one entry fulfils one pending message`, `compares trimmed text with normalised newlines`). A pending message is replaced by the first new USER entry with the same text; two identical pending messages are replaced one by one; a failed send removes the bubble and records an error; a `queued` bubble shows the label until replaced.

**TC.WEB.58 — Recency bands.** File `src/lib/lib.test.ts` (`maps age to bands`). Boundaries at 15, 60, 240 and 720 minutes against the server time; the dotted border at 24 hours; no activity → no background.

**TC.WEB.59 — Markdown sanitising.** File `src/lib/markdown.test.ts` (scripts and handlers, `javascript:` links, `noopener noreferrer`, tables and copy button, and the hardening of FR.25.05: `forbids forms, controls, styles, frames and style attributes`). Script tags, event-handler attributes and `javascript:` links are removed; links get `target="_blank"` and `rel="noopener noreferrer"`; tables and fenced code render.

**TC.WEB.60 — Resync and epoch handling.** File `src/lib/lib.test.ts` (`append adds only newer entries and ignores another epoch`, `replace marks earlier entries`, `prepend adds older entries`); the resync flow itself is covered by the Playwright offline flow (TC.WEB.61) and E2E `F13`. On `resync` or `link.status` online after offline the client refetches tree, session, entries after the last index, Focus items and usage; a different epoch reloads the last 50; a visibility change after more than 30 seconds of silence reopens the stream and resyncs; a short absence does nothing.

## Playwright flows (`ClientApp`, mock daemon, 390×844)

As built, TC.WEB.61–68 are covered by three flows in `e2e/flows.spec.ts` (`login → tree → search → create → send → focus → settings → recent → tags`, `offline banner, compact, and logout`, `main screen at 1280x800`; 23 screenshots in `ClientApp/screenshots/`). The mock daemon is in `src/mock`; login with any email/password, password `wrong` gives 401 and `throttle` gives 429. The real-server and full-stack Playwright runs are TC.WEB.95 and TC.WEB.96.

**TC.WEB.61 — Flow: login, tree, open session.** Log in with the mock credentials; the login error shows for a wrong password; the panel is collapsed on the main screen; ☰ opens it; the Tree tab shows directories, groups, rows with status block, attention pill, ✏, ★, profile and time; a supervisor expands to its workers; there is no Add directory button and a long-press or right-click on a row opens no menu; tapping a session opens it and collapses the panel. Screenshot saved.

**TC.WEB.62 — Flow: search and ancestor expansion.** Type a query that matches a session in a collapsed group; the group and directory show expanded with only the match; the Exact toggle follows the auto rule; the Project toggle is disabled with no selection; clearing restores the collapsed tree. Screenshot saved.

**TC.WEB.63 — Flow: create session.** The button is disabled with nothing selected; with a session selected the dialog asks for a name (required); the new session appears right after the anchor, opens and the message box has focus; with a group selected it appears first in the group; with a directory selected, first in the directory. Screenshot saved.

**TC.WEB.64 — Flow: send, thinking, queue, stop.** Send a message: a dimmed bubble, then the USER entry; the thinking row with a timer; the mock appends entries and the view follows; scrolling up shows the **↓ New messages** pill; a second send while running shows **queued**; **Stop** ends the run; Load earlier and Show full message work; hidden empty entries are not shown. Screenshot saved.

**TC.WEB.65 — Flow: Focus reply, send answers, dismiss.** The Focus badge shows the open count and top-kind colour; cards show chip, id, title, body, options and recommendation; an option fills the box; **Reply** closes the card; **Send answers (2)** sends both; **Dismiss** and **Got it** per kind; Decisions and Closed are collapsed and Closed re-collapses on re-selecting Focus; typed text survives a refresh. Screenshot saved.

**TC.WEB.66 — Flow: settings sheet, locking, streaming confirm.** The sheet shows Favourite, Model, Profile, Effort, Streaming; the initial values come from `lastUsed`; with `streamingAlive` the three selectors are disabled; turning Streaming off with a live process asks for confirmation and **Yes** retries with `force`; the chosen values are in the next send request. No profile actions are present. Screenshot saved.

**TC.WEB.67 — Flow: Recent and Tags.** Recent shows the flat list with ✕ on non-pinned rows and none on pinned; ✕ removes the row. Tags: chips, search, selection kept across text changes, grouped session list with collapsible headers, all five empty texts. Screenshots saved.

**TC.WEB.68 — Layout screenshots and manifest.** One screenshot of the main screen at 390×844 (deviceScaleFactor 2) and one at 1280×800; no horizontal page scroll at 390 px; input font size ≥ 16 px; the manifest has the name, `standalone`, the icons; the service worker does not cache `/api/*`; the production bundle has no mock data (a string check on the build output). The supervisor reviews the screenshots.

## End-to-end tests (`Tessyn.Web.E2E.Tests`)

**TC.WEB.69 — End to end: login to Focus reply.** Classes `StackReadFlowTests` (flows F01, F02, F03, F08, F09, F10, F12, F13), `StackTurnFlowTests` (F04, F05, F06, F07, F11, catalog and usage) and `FullStackTests` (`Spa_Against_Real_Server_Connector_And_Core`, the UI run). The real Relay Connector connects to an in-memory Tessyn.Web over the test server handler (the full-stack fact: a real Tessyn.Web child process on Kestrel); a temp data root has a small tree. Steps through the browser API: log in; `tree.snapshot`; `tree.createSession` after a session; `session.send` (fake CLI answers); `session.entries` shows the USER and ASSISTANT entries; a session posts an `ask` through the attention service; `attention.list` shows it; `attention.reply` marks it answered and starts a turn; events reach `/api/events` in order.

**TC.WEB.70 — End to end: offline and reconnect.** Class `StackReadFlowTests` (`F13_LinkDrop_Offline_Then_Back_Online`, `F12c_BadLinkToken_ConnectorStateError`). Stop the Connector: browsers get `link.status` offline and requests fail with `-32001`; start it: `link.status` online, requests work, the Connector reconnected by itself within the backoff.

**TC.WEB.71 — End to end: running flag from a desktop turn, web turn on the desktop.** Class `StackTurnFlowTests` (`F04`, `F05`: the web turn raises the running flag, `session.appended`, `turn.completed`); **the desktop-interactive half stays MANUAL (TC.WEB.103): the UI project is not referenced by the test projects**. Original design: a turn started through the desktop's interactive path (test seam of `TurnController`) makes `session.get` show `running: true` and the web send `queued: true`; when it ends the flag clears. A web turn is visible through the daemon's events the same way a Telegram turn is (running flag, entries appended).

## Hardening, performance and added automated tests (added 2026-11-13)

These cases came from the security review (`96-review-security`), the core review and the E2E work package. Classes are real.

**TC.WEB.84 — Remote tree performance.** Class `RemoteTreePerformanceTests`. On a synthetic tree (20 directories, 231 groups, 3,087 sessions, 598 top-level, real session files, real `SessionActivityService`): `tree.snapshot` cold < 3 s and warm < 500 ms (measured 181 ms / 43 ms, 226 KB), `recent.list` warm 58 ms, `tree.search` with every session matching 96 ms, `tags.search` 144 ms. After the core fix the test asserts **file-read counts** (3,300 rows = 3,300 stats, no draft reads, no full-file parse) and keeps wall-clock time only as a catastrophe ceiling, because the budgets failed under a fully loaded machine (cold 4.3 s). The snapshot JSON contains no `AgentToken`, file name or path.

**TC.WEB.85 — Cookie attributes and lifetimes.** Class `AuthHardeningTests`. The login cookie is `__Host-Tessyn.Auth` with HttpOnly, Secure, SameSite=Strict, Path=/ and no Domain. With `ManualTimeProvider`: continuous use for 28 days stays valid, day 31 is rejected (absolute lifetime, `Auth:AbsoluteDays`, configurable: `AbsoluteDays=3` works); 15 idle days end the session (`Auth:CookieDays`).

**TC.WEB.86 — Revocation.** Class `AuthHardeningTests`. A cookie copied before logout is dead after it; logout logs out **every** device (the epoch bump); an anonymous logout logs nobody out; logout without `X-Tessyn-Client` is 400 and logs nobody out; a login survives a server restart (shared keys and epoch) but not a password change; a logout invalidates cookies after a restart.

**TC.WEB.87 — Throttle is atomic and bounded.** Classes `AuthHardeningTests`, `LoginThrottleTests`. 40 parallel wrong guesses give exactly 5 × 401 and 35 × 429; a 200-way parallel race on the throttle allows exactly 5; a successful login does not use up the budget; per-IP window and expiry; blocked requests create no entry and empty buckets are swept; an IPv6 address is keyed by its /64 and an IPv4-mapped IPv6 address maps to IPv4.

**TC.WEB.88 — Trusted device.** Class `AuthHardeningTests`. After a successful login the `__Host-Tessyn.Device` cookie lets that device log in while the global limit (20/hour) is exhausted; a forged cookie, or one issued before a password change, gives no exemption; the per-IP limit still binds a trusted device.

**TC.WEB.89 — Request body limits.** Class `AuthHardeningTests`. Login 4 KB (a 5 KB body gives 413), `/api/rpc` 1 MB, `/link/reply` and `/link/events` 4 MB, each tested under and over the limit, with a per-path table and chunked bodies; Kestrel global limit 4 MB.

**TC.WEB.90 — Public vs loopback `/healthz`.** Class `HostingTests`. A public (non-loopback) caller gets only `{ "ok": true }`, also with a spoofed `X-Forwarded-For: 127.0.0.1`; a loopback IPv4 or IPv6 caller also gets `linkOnline`.

**TC.WEB.91 — CSP and headers.** Class `HostingTests`. The CSP contains `form-action 'self'`, `base-uri 'none'` and `object-src 'none'` next to the earlier directives; `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy: no-referrer`; `Cache-Control: no-store` on `/api/*`.

**TC.WEB.92 — Markdown hardening and tag URL policy.** Files `src/lib/markdown.test.ts` and `src/lib/lib.test.ts`. DOMPurify output has no `form`, `input`, `textarea`, `select`, `button`, `style`, `iframe`, no `style` or `formaction` attribute; the copy button is added after sanitising; `safeHttpsUrl` accepts only `https:` URLs.

**TC.WEB.93 — Logout failure handling and draft clearing.** Playwright `e2e-real/real.spec.ts` plus `lib.test.ts`. Logout sends the `X-Tessyn-Client` header; a failed logout shows an error toast and the user stays signed in; a successful logout clears `tessyn.draft.*` and `tessyn.reply.*` from local storage.

**TC.WEB.94 — Compaction outcome SYSTEM entries.** Class `SessionCompactRpcModuleTests`. `Nothing to compact` and `Failed` append a SYSTEM line (append only) and publish `turn.completed`; success also publishes it; a failing rewrite or an empty answer leaves the file and the Claude session id untouched; `queued` is true when the session was running; effort not in the list or an unknown `profileId` gives `-32602`.

**TC.WEB.95 — Real server: login, desktop offline, logout.** Playwright `e2e-real/real.spec.ts` (`npm run test:e2e:real`; `start-server.mjs` creates a temp config with a generated password hash and link-token hash and starts the real Tessyn.Web on port 5399 serving the production build). Log in through the UI, see **Daemon offline** in the sessions panel, log out from the menu; `/api/auth/me` is 401 before login, 200 after login and 401 after logout. Regression for review finding H1 (login and logout without the CSRF header).

**TC.WEB.96 — Full stack.** `FullStackTests.Spa_Against_Real_Server_Connector_And_Core` with `e2e-fullstack/fullstack.spec.ts` (`TESSYN_FULLSTACK=1`, about 1 minute): login with the green link dot → Tree with the seeded names → workers → search `zebrafinch` (real FTS index) → create a session → send a prompt (user bubble with the profile stamp, fake assistant reply) → usage strip `5h: 42%`, `7d: 17%` → Focus reply and dismiss → favourite then Recent → Tags (PR chip) → Compact (summary and compaction divider). The spec fails on any page or console error after sign-in. 12 screenshots `fullstack-01…12`.

---

## Manual tests (iPhone, deployed site)

**TC.WEB.72 — Install on the home screen.** In Safari open the site, Share → Add to Home Screen. The icon is correct; the app opens full-screen without browser bars; the status bar colour fits; a second launch shows the shell at once.

**TC.WEB.73 — Portrait layout, keyboard, safe areas.** In portrait: the top bar and the usage strip clear the notch and the home indicator; the message box grows with text up to about 40% of the screen; opening the keyboard keeps the box visible; tapping an input does not zoom the page; the sessions panel covers the full width and its tabs are reachable with one thumb.

**TC.WEB.74 — Login on the phone and log out.** Log in with the real credentials; close and reopen the app: still logged in; log out: the login page shows and Back does not reveal data.

**TC.WEB.75 — Follow a live turn on the phone.** Start a long turn from the desktop and watch on the phone: the thinking row and timer, entries appearing, auto-follow and the New messages pill. Send a message from the phone while it runs: **queued**, then it starts. Stop a turn from the phone.

**TC.WEB.76 — Focus on the phone.** Have a session post an `ask` with options and a recommendation, a `todo` and a `decided` item. Badge colours are right; Reply, Send answers, Dismiss and Got it work; the desktop's Focus view shows the same state.

**TC.WEB.77 — Create session on the phone, check the desktop tree.** Create a session after a selected session, as first in a group and as first in a directory; the desktop tree shows each at the same place at once.

**TC.WEB.78 — Settings, Compact and usage on the phone.** Change favourite (the desktop shows ★), model, effort and profile; the next turn's reply shows `[model, effort]`; the usage strip follows the profile; Compact a small test session and check that the transcript starts with the summary.

**TC.WEB.79 — Background and resume.** Open a session, switch away for 2 minutes while a turn finishes on the desktop, return: entries, tree and badges are current within a few seconds without a manual reload.

**TC.WEB.80 — Desktop offline and back.** Close TessynDesktop: the phone shows **Desktop offline** within a minute and Send is disabled. Start it: the phone goes online by itself and resyncs.

**TC.WEB.81 — Desktop Settings "Web access".** Enter URL and token, Save: the status goes to Connected without restarting the app; untick and Save: the phone shows offline; a wrong token shows Failed with a reason; the token is not shown again after saving. Restart TessynDesktop: the Connector starts by itself.

**TC.WEB.82 — Deployed site: TLS, headers, throttling.** `curl -I https://tessyn.vvinogradoff.com` shows a valid certificate, HSTS, CSP and `Referrer-Policy`; `http://` redirects to `https://`; `/healthz` shows `linkOnline`; six wrong logins from the phone's network give the 429 message and, after waiting, a normal login works; the unit survives `systemctl restart tessyn-web` with the phone still logged in.

**TC.WEB.83 — Search, Recent and Tags on the real tree.** On the real tree (about 2,700 sessions): search answers in a few seconds and shows the ancestors expanded; Recent matches the desktop's dropdown; Tags matches the desktop's Tags tab for the same selection.

## Manual tests added after the build (2026-11-13)

Status: **open**, together with TC.WEB.72–TC.WEB.83, which make up the phone pass (no real-device run has been done; the live positive login was not done by the workers, who had no password).

**TC.WEB.97 — First positive login on the live site, device cookie, logout everywhere.** On the phone log in at `https://tessyn.vvinogradoff.com` with the real credentials: the cookie is accepted, the app opens. Open the site on a second browser and log in there too. Log out on the second: the first device is logged out as well (security stamp), and its next request shows the login page. Log in again. Then close and reopen the home-screen app after a few days: still signed in (14-day idle limit).

**TC.WEB.98 — Changing the password logs out every device.** Follow section 4 of `docs/features/web-client.md`: new hash, restart. Every device is signed out; the old password gets 401; the new works; the previously trusted device still needs the new password (its device cookie is bound to the old password stamp).

**TC.WEB.99 — Link token rotation.** Follow section 5 of the guide: new token and hash on the server; the desktop shows **Error: Link token rejected**; paste the new token in Settings → Web access and Save: **Connected since …**. Optionally replace the DPAPI file `web-relay.dat` with a provisioned one and restart TessynDesktop.

**TC.WEB.100 — Redeploy and server layout.** Run `publish.ps1` and `redeploy.sh`. The script ends with `healthz (loopback): {"ok":true,"linkOnline":…}`. On `safe`: the process runs as `tessyn-web`; `/var/www/tessyn` is `masteruser:tessyn-web` 750, files 640; `/var/lib/tessyn-web` 700; `www-data` cannot read the keys or the config; `systemd-analyze security tessyn-web` shows about 1.8; `nginx -t` passes and the other sites (vvinogradoff.com, grafana.datalabs.store) still answer.

**TC.WEB.101 — Desktop refresh from the phone.** Change Favourite and Streaming on the phone: the desktop tree row star, the command-bar star, the 📡 toggle and the Stop button follow without a restart. Hide a session in the web Recent tab: the desktop's Recent dropdown drops it without a restart (`recent.hide` raises `SessionSettingsChanged`; `SessionNodeViewModel.RefreshSettingsFromModel`).

**TC.WEB.102 — Desktop tree insert position.** Create a session from the web after a session in a directory that has groups and workers, as first in a group, and as first in a directory: the desktop row appears right after the anchor's row (after its supervisor when the anchor is a worker), first in the group or directory. An agent `spawn_session` still appends and nests as before.

**TC.WEB.103 — Desktop-interactive turn and the running flag.** Send an interactive prompt in the desktop session view: the row shows running on the web and returns to idle at the end; repeat with two queued sends (the flag stays on until both finish). Not unit-tested: the UI project is not referenced by the test projects.

**TC.WEB.104 — Desktop paths that were touched.** A scheduled or Telegram turn shows the profile name on its USER bubble; the desktop Focus view's **Reply** and **Send answers** behave as before (they now share `AttentionReplyComposer`).

**TC.WEB.105 — Desktop compaction unchanged.** Auto-compact with a prompt, with an empty prompt (it now takes the turn lock and shows the running flag) and with a forced failure: the same SYSTEM messages, the file replaced only on success.
