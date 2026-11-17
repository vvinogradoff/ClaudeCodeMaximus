> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.14 — Scheduled Turns

The application allows any session to schedule a future turn against itself or another session, enabling "get back to me in 30 minutes" and "check this job every hour" workflows.

**FR.14.1 — In-process MCP server:** The application hosts an HTTP MCP server bound to `127.0.0.1` (loopback only). The port is persisted in `appsettings.json` (`AgentMcpPort`); 0 means a free port is chosen at startup. The server implements the MCP JSON-RPC 2.0 protocol and is started when the application launches (if `AgentToolsEnabled` is true in `appsettings.json`).

**FR.14.2 — Per-node identity:** Each Session node has a stable `NodeId` (GUID, never changes even after session detach/compact) and a `AgentToken` (random secret, regenerated only on explicit user action). Both are persisted in `appsettings.json` and backfilled for existing sessions on first load. `NodeId` is the durable handle used by all scheduling and orchestration tools.

**FR.14.3 — Agent tool injection:** When `AgentToolsEnabled` is true, every `claude` process spawn (user turns, scheduled turns, orchestrated turns) passes `--mcp-config <per-node-path>`. The config file points to the in-process MCP server with the node's `AgentToken` in the `X-CMX-Token` request header. The MCP server maps the token to the calling node, enabling self-referential scheduling.

**FR.14.4 — Scheduling tools:**
| Tool | Required args | Optional args | Effect |
|---|---|---|---|
| `schedule_wake` | `when` (object: `inSeconds`, `at`, or `cron`) | `prompt`, `note`, `target` (nodeId) | Create a schedule. When `target` is omitted, targets the caller's own node (self-wake). |
| `list_schedules` | — | `all` (bool) | Returns the caller's schedules (or all app schedules when `all=true`). |
| `cancel_schedule` | `scheduleId` | — | Removes a schedule. |

**FR.14.5 — Self-wake identity guarantee:** When `schedule_wake` is called without a `target`, the scheduled turn is always fired against the **same** session (same `NodeId`, resumed via its current `ClaudeSessionId`). A new session node is never created.

**FR.14.6 — Schedule kinds:**
- **Delay**: fire once after N seconds from now (`when.inSeconds`)
- **At**: fire once at a specific UTC datetime (`when.at`)
- **Cron**: fire repeatedly on a cron expression (`when.cron`)

**FR.14.7 — Missed fire policy:** If the application is closed when a schedule is due, on next launch the application fires the schedule once (default policy: `FireOnce`). The per-schedule policy is configurable.

**FR.14.8 — Schedule persistence:** Schedules are stored as a `List<ScheduleModel>` in `appsettings.json`. The scheduler re-arms timers from persisted schedules on startup.

**FR.14.9 — Per-node turn serialization:** At most one turn may run concurrently per `NodeId`. If a live user-initiated turn and a scheduled turn would overlap, the scheduled turn waits for the lock to be released. Exception: a Mailbox Delivery stays pending while its target session is busy instead of waiting on the lock, so results that keep arriving can be merged into it (FR.15.12).

**FR.14.10 — Scheduled turn visibility:** A scheduled turn's prompt is appended to the session file as a `USER` entry, and the response as an `ASSISTANT` entry. A `SYSTEM` entry `[Scheduled: <note>]` is prepended to the prompt so the trigger is visible in the session view. The session file watcher (`FR.3.1`, `SessionViewModel`) picks up the new entries and updates the output panel for any session that is currently open.

**FR.14.11 — Native scheduling redirect:** When `AgentToolsEnabled` is true **and** the session is in One-shot Mode (FR.19), the hidden instruction block (FR.11.2, FR.11.10) includes a directive redirecting Claude away from the CLI's built-in scheduling tools (`ScheduleWakeup`, `CronCreate`, `CronList`, `CronDelete`) toward the TessynDesktop MCP scheduling tools (`mcp__cmx__schedule_wake`, `mcp__cmx__list_schedules`, `mcp__cmx__cancel_schedule`). **Rationale:** in One-shot Mode the `claude` process is terminated after each turn, so schedules registered via the CLI's built-in tools live only within that process's lifetime and are silently lost. The MCP-served scheduling tools delegate to the host-side `SchedulerService`, which persists schedules to `appsettings.json` and re-arms them on app startup (FR.14.7, FR.14.8). When `AgentToolsEnabled` is false, or when the session is in Streaming Session Mode (FR.19.10), the redirect line is omitted. As of **FR.19.11**, One-shot Mode spawns also pass `--disallowed-tools` for the same four native tools when `AgentToolsEnabled` is true, making the redirect an enforced block rather than an advisory instruction.

**FR.14.12 — UI-initiated delayed send:** Right-clicking the Send button (SessionView) opens a "Schedule Send" popup prompting for a delay amount and unit (Minutes / Hours / Days) instead of sending the composed message immediately. Confirming registers a **Delay**-kind self-wake schedule (FR.14.5, FR.14.6) targeting the session's own `NodeId`, using the identical scheduling engine exposed to agents as the `schedule_wake` MCP tool (`ISchedulerService`/`IDaemonClient.AddSchedule`) — the eventual turn is fired by the same `SchedulerService.FireScheduleAsync` → `SessionTurnService.RunTurnAsync` path used for any other scheduled turn (FR.14.9, FR.14.10), so instruction-block building, context preamble, and cross-profile session adoption all apply unchanged with no additional code. The input box is cleared immediately (mirroring an ordinary send) and a one-off system message in the output panel confirms the scheduled fire time; this message is UI-only and is not written to the session file. The existing per-session schedule indicator (clock icon, shown next to Send/Stop whenever the session has an active schedule, with a tooltip and a cancel action) applies unchanged, since a UI-created schedule is indistinguishable from one an agent creates. Left-click Send behavior is unaffected. Right-clicking with an empty input box is a no-op — there is nothing to schedule.

---

---

## Amendments and additions (batch 2026-11-17)

**FR.14.13 — Picks on a schedule (A14).** A schedule may carry `profileId`, `modelId` and `effort`, the values the user picked when the send was scheduled.
- **Validation at add time.** The same resolver as `session.send` checks them: an unknown model, effort or profile is rejected at once (`-32602`) and nothing is stored.
- **Firing.** The turn runs with **exactly** the stored picks; a field that is absent is **not sent**, so the session's own resolution applies to it. Mailbox deliveries never carry picks. Schedules stored before the fields existed have none and behave as before.
- **A pick that no longer resolves** (profile deleted, model left the catalog): the turn **still runs** on the session's own settings and the supervisor gets a mailbox note; the message is not lost. *(Decision flagged for the Architect: the alternative is to refuse the turn, as for a pin to a missing profile, ISSUE.013.)*
