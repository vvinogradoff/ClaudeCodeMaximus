> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.15 — Agent Orchestration (Persistent Worker Sessions)

A supervisor session can spawn and control worker sessions. All workers are **persistent, resumable** session nodes in the tree — never ephemeral subagents.

**FR.15.1 — Orchestration tools:**
| Tool | Required args | Optional args | Effect |
|---|---|---|---|
| `list_sessions` | — | `scope`, `nodeId`, `query`, `running`, `activeWithinHours`, `includeWorkers`, `tags`, `limit`, `offset` | Returns one page of sessions, newest first — by default the caller's project, with the caller's own workers nested under its row (FR.15.13). Each row: Short Node Id, name, age of its Last Activity, and flags (`running`, `fav`, `no-resume`, worker counts). |
| `spawn_session` | `name` | `workingDir`, `parentNodeId`, `prompt`, `model`, `group`, `outputFormat` | Creates a **new** session node, runs the first turn, returns `{nodeId, resultText}`. `model` is tier-enforced (FR.16). |
| `send_to_session` | `nodeId`, `prompt` | `mode` (wait/async), `outputFormat`, `migrate` (FR.15.14) | **Resumes** an existing session node, runs a turn. In `wait` mode returns the result synchronously. In `async` mode posts the result back to the supervisor when done, as a Mailbox Delivery (FR.15.3, FR.15.12). `outputFormat` is advisory markdown shaping guidance (FR.15.7). |
| `read_session` | `nodeId` | `lastN` | Returns the last N session file entries for the given node. |
| `stop_session` | `nodeId` | — | Cancels any running turn for the given node. |
| `set_session_model` | `nodeId`, `model` | — | Changes the model for an existing session (tier-enforced, FR.16.2). Effective from next turn. |
| `orchestrate_parallel` | `tasks` | `model`, `group`, `outputFormat`, `budgetTokens` | Barrier fan-out: spawns one session per task, waits for all, returns one markdown document (FR.15.8). |
| `orchestrate_pipeline` | `items`, `stages` | `model`, `group`, `outputFormat`, `budgetTokens` | Non-blocking item pipeline: one session per item through sequential stages, returns one markdown document (FR.15.8). |
| `workflow_phase` | `title` | — | Writes `[Phase: {title}]` to the supervisor session file (FR.15.9). |
| `workflow_log` | `message` | — | Writes `[Log: {message}]` to the supervisor session file (FR.15.9). |
| `get_budget` | — | — | Returns token usage and remaining budget for the caller's active orchestrations (FR.15.6). |

Every argument that takes a session id (`nodeId`, `target`, `parentNodeId`) accepts a full NodeId or a unique prefix of it (Short Node Id, FR.15.13).

**FR.15.2 — New-vs-resume choice:** The supervisor explicitly chooses between `spawn_session` (creates a new tree node) and `send_to_session` (resumes an existing node). `list_sessions` provides the data needed to make that choice: called with no arguments it lists the supervisor's own workers under its row (FR.15.13).

**FR.15.3 — Async mailbox:** When `send_to_session` is called with `mode=async`, on worker turn completion a follow-up turn is automatically triggered on the supervisor node — a **Mailbox Delivery**. It is implemented as a delay-0 schedule targeting the supervisor (`ScheduleModel.IsMailboxDelivery = true`), reusing the scheduler engine from FR.14; its prompt is `[Worker finished] <name>` followed by the worker's result text on the next line, and its note is `Worker '<name>' result`. Results that arrive while a delivery for the same supervisor is still pending are merged into it (FR.15.12).

**FR.15.4 — UI thread marshaling:** All tree mutations triggered by orchestration tools (node creation, model changes) are executed on the UI thread through the UI dispatcher abstraction (`IUiDispatcher`), never directly from the worker thread that handled the tool call.

**FR.15.5 — Guardrails (enforced):** To prevent runaway loops, the following limits are checked at runtime — they are not advisory:
- `Constants.Agent.MaxOrchestrationDepth` (default: 5) — maximum supervisor-worker nesting depth. Checked on `spawn_session`, `orchestrate_parallel`, and `orchestrate_pipeline`; a `McpToolException` is thrown when exceeded.
- `Constants.Agent.MaxConcurrentWorkers` (default: 10) — maximum simultaneous worker turns across the entire app. `orchestrate_parallel` and `orchestrate_pipeline` acquire slots from a global `SemaphoreSlim`; if all slots are taken the spawn waits until one frees.
- `Constants.Agent.MaxTurnsPerLoop` (default: 100) — maximum turns per cron schedule before auto-cancellation.

Each `SessionNodeModel` persists `OrchestrationDepth` (int, 0 = top-level user session) and `SupervisorNodeId` (string?, null for user sessions). These are set when a session is created by an orchestration tool and used to enforce the depth limit on further spawning from that session.

A global kill-switch (`TerminateAllSessions`) cancels all active turn locks and calls `ClaudeProcessManager.TerminateAll()`.

**FR.15.6 — Orchestration Budget:** Fan-out tools (`orchestrate_parallel`, `orchestrate_pipeline`) accept an optional `budgetTokens` (int) argument. When set, an `OrchestrationBudget` is registered in memory for the duration of the orchestration, keyed by `(supervisorNodeId, orchestrationId)`. Each worker turn accrues `input_tokens + output_tokens` against the budget. Spawning a new worker when the budget is already exhausted returns a `McpToolException`. The `get_budget` tool returns current usage and remaining tokens for the caller's active orchestrations.

**FR.15.7 — Output Format Guidance (markdown-centric, not schema-validated):** `spawn_session`, `send_to_session`, `orchestrate_parallel`, and `orchestrate_pipeline` accept an optional `outputFormat` argument — a free-text/markdown description of the desired response shape (e.g. a section outline). The host appends it to the relevant prompt (the first prompt for `spawn_session`; the prompt for `send_to_session`; every worker's prompt for `orchestrate_parallel`; the **last stage's** prompt only for `orchestrate_pipeline`) as advisory guidance. It is never validated or retried — a worker's result is consumed by another session, which reads markdown far better than JSON, so there is no host-side parsing step to enforce. Convention: when a worker's output is genuinely large or structured (data files, generated code, a big table), it should write the artifact to a file in its working directory and return the path instead of inlining the content (see glossary "Worker Artifact File").

**FR.15.8 — Fan-Out Primitives:**

`orchestrate_parallel` — barrier fan-out:
| Arg | Required | Description |
|---|---|---|
| `tasks` | yes | JSON array of `{name, prompt}` objects |
| `model` | no | Override model for all workers (tier-enforced, FR.16) |
| `group` | no | Group name for worker sessions in the tree |
| `outputFormat` | no | Advisory markdown shape guidance, applied to every worker's prompt (FR.15.7) |
| `budgetTokens` | no | Token ceiling for this orchestration (FR.15.6) |

Creates one worker session per task. All sessions run concurrently, bounded by `MaxConcurrentWorkers`. Returns **one markdown document** — a `## {name}` section per worker with its result and a trailing `_tokens: in=… out=…_` line — when ALL workers complete. Depth enforcement applies (FR.15.5).

`orchestrate_pipeline` — non-blocking item fan-out through sequential stages:
| Arg | Required | Description |
|---|---|---|
| `items` | yes | JSON array of string items |
| `stages` | yes | JSON array of `{prompt}` objects. Stages run sequentially within one session per item. Use `{{item}}` in the first stage prompt to reference the item. |
| `model` | no | Override model for all worker sessions (tier-enforced, FR.16) |
| `group` | no | Group name for worker sessions |
| `outputFormat` | no | Advisory markdown shape guidance, applied to the LAST stage's prompt only |
| `budgetTokens` | no | Token ceiling |

Creates one session per item. Within each session the stages are sent as sequential turns (the session accumulates full context). All item-sessions run concurrently. Returns **one markdown document** — a `## {item}` section per item with its result and a trailing `_tokens: in=… out=…_` line — when all are complete.

**FR.15.9 — Progress Tracking:**
- `workflow_phase(title)` — writes a `SYSTEM` entry `[Phase: {title}]` to the calling supervisor's session file. Visible in the session view. Returns immediately.
- `workflow_log(message)` — writes a `SYSTEM` entry `[Log: {message}]` to the calling supervisor's session file. Returns immediately.

**FR.15.10 — Per-Session Model Control:**
- `set_session_model(nodeId, model)` — changes the persisted `ModelId` on an existing session node (tier-enforced per FR.16.2; the caller's tier must be ≥ the requested model's tier). Takes effect on the next turn for that session. The supervisor can use this to switch models on running worker sessions mid-orchestration.

**FR.15.11 — CMX Orchestration Skill:** A skill named `cmx-orchestration` documents how to act as a supervisor (choosing between the orchestration tools, `outputFormat` shaping, the file-artifact convention) and how to behave when spawned as a worker (return markdown, honor `outputFormat`, don't over-nest, never use CLI-native scheduling). `SkillProvisioningService.ProvisionAll()` writes its `SKILL.md` (content: `Constants.Agent.OrchestrationSkillMarkdown`) into every registered profile's `skills/cmx-orchestration/` folder at startup — idempotent, rewriting only when content changes, mirroring `AgentMcpServer.EnsureConfigFileAsync`'s write pattern. Gated on `AgentToolsEnabled`; the Default profile (no dedicated config dir) is skipped. Only the skill's one-line frontmatter `description` is always visible to a session; the body loads on demand. A one-line pointer to the skill is folded into `Constants.Instructions.NoWorkflowTool`, so it is surfaced at session start under the same `IsAgentToolsEnabled && IsNewSession` gate as the rest of that instruction (FR.14.11) — not repeated on every turn. Its supervisor section also states (ISSUE.012): worker results and wake-ups arrive as scheduled turns the user is not watching, and nothing tells the user that such a turn finished — call `notify_user` once, and only when the user must decide or act (FR.21.24) [**amended by FR.23.10:** the supervisor posts the ask once with `post_attention` (`notify: true` when the user must act soon) and does not restate it in later delivery turns, and workers call neither `notify_user` nor `post_attention`]; keep delivery turns short and don't narrate each worker's arrival; don't arm polling or "fallback" self-wakes to collect async results — they arrive by themselves, merged when they queue up (FR.15.12) — and use a self-wake only for a real deadline or a long-interval check on a worker that may be stuck. Its worker section tells a session it is a worker when its prompt is a scoped task sent by another session, and that `list_sessions` confirms this on its `You:` line (FR.15.13); it no longer suggests a full listing for that check.

**FR.15.12 — Mailbox coalescing:** A supervisor whose workers finish close together gets one delivery turn, not one turn per worker (ISSUE.012).
- **Merge:** the async path of `send_to_session` hands each result to `SchedulerService.AddOrMergeMailboxDelivery(supervisorNodeId, workerName, resultText)`. Under the scheduler's lock it appends the result to the supervisor's pending Mailbox Delivery if there is one (a blank line, then `[Worker finished] <name>` and the result text), otherwise it adds a new delivery (FR.15.3). A merged delivery's note becomes `Worker results (<n>): <name1>, <name2>, …`, in arrival order.
- **Hold while busy:** when a Mailbox Delivery is due but its target session has a turn in progress (its per-node turn lock, FR.14.9, is held), the scheduler leaves it pending and re-checks it on the next poll instead of queueing on the lock. Every result that arrives during a long supervisor turn therefore lands in the same delivery, which fires on the first poll after the supervisor becomes idle. Other schedules keep FR.14.9's wait-for-the-lock behavior.
- **Persistence:** `ScheduleModel.IsMailboxDelivery` (bool, default `false`) is persisted with the schedule (FR.14.8); schedules persisted before this change load as ordinary schedules.
- **Tool description:** `send_to_session`'s `mode` description states that `async` results arriving while the caller is busy are delivered together in one turn.

**FR.15.13 — Session listing: scope, filters, paging and cost:** `list_sessions` returns one bounded page of sessions, newest first, instead of the whole tree. Split out of ISSUE.013 (R8): by 2026-11-03 the unfiltered listing had reached 341,807 characters for 2,686 sessions — past the CLI's tool-output limit — and every call parsed all session files (51.5 MB) while holding the settings lock.

- **Arguments** (all optional):

  | Arg | Default | Meaning |
  |---|---|---|
  | `scope` | `project` | `project` — sessions under the caller's Directory Node; `all` — every Directory Node; `children` — the direct workers of `nodeId`; `subtree` — every descendant of `nodeId` |
  | `nodeId` | the caller | Root session for `children`/`subtree` (full or Short Node Id); ignored by `project`/`all` |
  | `query` | — | Case-insensitive substring of the session name or of a group name on its tree path, or a prefix of its NodeId; surrounding whitespace is ignored |
  | `running` | — | `true` — only sessions with a turn in progress; `false` — only idle ones |
  | `activeWithinHours` | — | Only sessions whose Last Activity is within the last N hours (N > 0) |
  | `includeWorkers` | `false` | `project`/`all` only: list every Worker Session as a row instead of only counting it on its supervisor's row |
  | `tags` | — | Array of at most 30 tags in the FR.24.4 input forms (URL, `HSE-230`, `owner/repo#215`, `owner/repo`); only sessions whose top-level session (itself, or for a Worker Session the top-level supervisor it rolls up to, FR.24.12) carries ANY of them are rows (OR). Combines with every other argument; an empty array (or only blank entries) means no filter. An entry that is no tag form is a tool error: `Not a tag: '<entry>' - not a Linear issue, GitHub pull request or repository.` (FR.24.15) |
  | `limit` | 30 | Rows per page, clamped to 1–200 |
  | `offset` | 0 | Rows to skip; a negative value counts as 0 |

  An unknown `scope`, an unresolvable `nodeId`, or a non-positive `activeWithinHours` is a tool error.
- **Which sessions are rows:** in `children`/`subtree` the candidates are the root's direct workers / all its descendants. In `project`/`all` they are the scope's top-level sessions, the caller and the caller's direct workers (wherever they live), and — when `includeWorkers` is true or `query` is set — every Worker Session in the scope. `query`, `running` and `activeWithinHours` then test each candidate on its own.
- **Order:** newest Last Activity first, ties by name and then NodeId; sessions whose file is missing come last. A row whose supervisor's row is also listed goes directly under it, indented one level per nesting step, siblings newest first — so by default the caller's own workers sit under the caller's row. In `all` scope the remaining top-level rows are grouped by Directory Node, groups ordered by their newest row.
- **Paging:** `offset`/`limit` apply to the ordered rows, nested rows included.
- **Last Activity:** the session file's last-write time (UTC). The listing never reads session-file contents.
- **Output:** plain text. The listing's own words, flags and separators are ASCII; session, group and directory names are printed as they are, except that control characters (line breaks included) become spaces, so every row stays on one line.
  - First line: `You: <id> "<name>" - top-level session in <label>`, or for a worker `You: <id> "<name>" - worker (depth <n>) of <id> "<name>" in <label>` (the `of …` part is dropped when the supervisor is no longer in the tree).
  - Section headers: `<label> (<path>) - <t> top-level sessions, <w> workers` per Directory Node in `project`/`all` (counts cover the whole directory, before filters); `Workers of <id> "<name>" - <d> direct, <s> in subtree` in `children`/`subtree`. The header of a page's first section is always printed.
  - Rows: `<indent><id>  <name>  <age>`, then only the flags that apply, in this order:
    - `running`;
    - `fav` — a Favorite Session (FR.9.7);
    - `no-resume` — no resumable Claude session (no `ClaudeSessionId`, or its JSONL is in no profile, FR.11.13): the next turn starts a fresh Claude session seeded from the transcript;
    - `<n> workers` or `<n> workers (<k> running)` (`1 worker` when there is one) — its direct workers anywhere in the tree;
    - `worker of <id>`, or `worker of you` — a worker not placed under its supervisor's row on this page (the supervisor is not listed, or the page starts inside its block) whose supervisor is not the scope root;
    - `[<label>]` — the row lives in another Directory Node than its section;
    - `(you)` — the caller's own row.

    Indent is two spaces per level; `<id>` is the Short Node Id; names are cut to 60 characters ending in `...` wherever the listing prints them (rows, the `You:` line, headers); directory paths drop a trailing separator unless they are a drive root; `<age>` is the Last Activity relative to now — `now` (under a minute), `<n>m`, `<n>h` (under a day), `<n>d` (under 14 days), `<n>w` (under 8 weeks), `<n>mo` (under a year), `<n>y` — or `-` when the session file is missing.
  - Footer: `Rows <a>-<b> of <n>.`, plus `Next page: offset=<b>.` when rows remain, then the hints that apply: `Other projects: scope=all.` (`project` scope), `Workers: scope=children nodeId=<id>.` (a listed row has workers that are not shown), `Narrow: query=, running=, activeWithinHours=.`. With no rows: `No sessions match.` and the same hints — or, when `offset` is past the end of a non-empty result, `No rows at offset=<o>; <n> in total.`
  - **Bound:** at most 200 rows and 60-character names per page keep even the largest page well inside the CLI's tool-output limit; the default page is typically 1–2 KB.
- **Short Node Ids:** the `You:` line, headers and rows print the shortest prefix of a NodeId — at least 8 characters — that no other NodeId in the tree shares. Every tool argument that takes a session id (`nodeId` of `send_to_session`, `read_session`, `stop_session`, `set_session_model` and `list_sessions`; `target` of `schedule_wake`; `parentNodeId` of `spawn_session`) accepts the full NodeId or any prefix of at least 6 characters that matches exactly one session, case-insensitively, ignoring surrounding whitespace. A shorter prefix is an error; an ambiguous one is an error listing up to 5 matches (full NodeId and name). An id matching no session is a not-found error, except for `stop_session`, which passes it on unchanged so the running turn of a session removed from the tree can still be cancelled. Tool results keep echoing full NodeIds.
- **Cost:** the settings lock (`IAppSettingsService.SyncRoot`) is held only while session fields are copied; last-write times and resumability are read after it is released. Resumability is checked only for the rows on the returned page, against one list of profile `projects` directories resolved per call. `BuildAgentSessionSummaries` (Telegram `/sessions` and `/open`, FR.21.8) follows the same lock and Last Activity rules and still returns every session.
- **Tool description:** states the defaults (the caller's project; the caller and its own workers; newest first; 30 rows per page), the flags, and that Short Node Ids are accepted wherever a session id is.

**FR.15.14 — Profiles in the MCP tools (ISSUE.013, decisions A3 and A18):** The profile rules are in FR.12.27 and FR.12.28. This item is what the tools show and accept. Reports: `.wpf-work/320-issue013-daemon.md`, `320b-issue013-fixes.md`, `320c-issue013-n1n2.md`.
- `send_to_session` takes an optional `migrate` (boolean). The result text ends with `[profile=<name>]` (in `async` mode: `... on profile=<name>`). A removed or renamed pin, or a stored profile with no `.credentials.json` (logged out), is an error that names the profile and says: call `send_to_session` with `migrate=true` (allowed only for your own descendants) or sign the profile in again. The turn does not run.
- `migrate: true` is allowed only when the target is a descendant of the caller (any depth, by the SupervisorNodeId chain). It moves exactly the explicit target, never its descendants, to the caller's profile, then runs the turn. The result adds `[migrated to profile=<name>; the transcript still names N path(s) under the old config dir, e.g. ...]`. On a running target the old profile's login check is skipped, the queued target is the stated profile and the result adds `[migration queued: the session is running; it moves to profile=<name> when its turn ends]`. A failed file move returns an error and the turn does not run.
- A legacy label that matches nothing runs on Default and the result says so (FR.12.27).
- `list_sessions` rows carry `profile=<name>` right after the age (`default` for an unpinned or `[Sys]` session, `<name> (missing)` for a pin to a removed profile) and `switching-to=<name>` while a switch is queued. Both fields use the 60-character name cap. The values come from the settings only and no disk is read, so an unpinned row says `default` even when the next turn would take the profile from the transcript history.
- `spawn_session` pins the new child by `ProfileId` from its supervisor (a supervisor on Default with no record leaves the child unpinned). It never registers a directory. A `workingDir` outside every project gives: `'<dir>' is not under a project in the Tessyn tree. Registered: <a>, <b>, ... (+N more). Add it in Tessyn or use parentNodeId.` (the first 12 are listed). The match respects path separators and ignores case and trailing separators: `C:\a\bc` is not under `C:\a\b`.

---
## Amendments and additions (2026-11-18)

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.15.1 Orchestration tools | FR.15.15 | New tool `create_session`: a session creates a top-level session for the user, not a worker. |
| FR.15.5 Guardrails | FR.15.15 | `create_session` has its own limits (top-level callers only, a rate cap); the depth limit does not apply to it. |
| FR.15.11 CMX Orchestration Skill | FR.15.15 | The skill and the session-start tool list name `create_session` and say when to use it. |

### New requirements

**FR.15.15 — Create a top-level session (`create_session`).** A session can create a **top-level session** that belongs to the user, for example when the user asks it to open a new session with given settings at a given place in the tree. Unlike `spawn_session`, the new session is **not a worker**:
- It has no `SupervisorNodeId` and its `OrchestrationDepth` is 0.
- It is listed like a session the user created (tree, Recent tab, Tags tab).
- It gets its own Telegram topic and its own Focus items.
- The caller gets no result from it.

Use cases: [UC.15.4](/docs/usecases/UC.15-Agent-Orchestration.md). Test cases: [TC.15.8, TC.15.9](/docs/testcases/TC.15-Agent-Orchestration.md).

| Arg | Required | Meaning |
|---|---|---|
| `name` | yes | Display name, trimmed; must not be blank. |
| `after` | no | A session id (full NodeId or Short Node Id, FR.15.13). The new session goes **right after** that session in its directory or group. A worker stands for its top-level supervisor, as in `tree.createSession` (FR.1.30). |
| `workingDir` | no | A registered project directory or a path below one (the rule and error of `spawn_session`, FR.15.14). The new session goes **last** in that directory (FR.1.30). |
| `group` | no | A group directly in that directory, or in the caller's directory when there is no `workingDir`. Matched by name, case-insensitively, and created when missing. The new session goes last in the group. |
| `model` | no | A model id from the catalog (`catalog.get`), or `default`. Stored as the session's model (FR.12.35). Tier-checked (FR.16.6). |
| `effort` | no | `default`, `max`, `high`, `medium` or `low`. Stored as the session's effort (FR.12.35). |
| `profile` | no | `default`, or a stored profile's `ProfileId` or display name (case-insensitive). The session is pinned to it (FR.12.27). Without it, the session gets the caller's profile, inherited as `spawn_session` does (FR.15.14). |
| `streaming` | no | Streaming Session Mode (FR.19). Default off. |
| `sound` | no | Focus Item Sound (FR.22). Default on. |
| `prompt` | no | The first prompt. |

- **Placement.** `after` can't be combined with `workingDir` or `group`. With none of the three, the new session goes right after the caller.
- **One step.** The registry creates the node, any missing group and all the settings under one lock, with one save and one `SessionAdded` event, so no client sees a half-configured session.
- **No selection.** Clients show the session where the daemon put it. They neither select it nor open it.
- **First prompt.**
  - Its turn starts in the background as a headless turn with `TurnSource.Orchestrated`, so the USER entry carries `source="orchestrated"` (FR.2.6). The tool returns at once.
  - The result stays in the new session: nothing goes back to the caller and there is no Mailbox Delivery.
  - The turn runs one-shot even with `streaming` on (FR.19.11); the Persistent Session Process starts with the user's first send.
  - Because the session is top-level, its first turn gets the top-level instruction block (tool bans, `notify_user`, Focus; FR.14.11).
- **Result** (plain text): `Session '<name>' created (nodeId=<id>) in <place>.`, where `<place>` is `<directory label>[ / <group path>], right after '<anchor name>'` or `<directory label>[ / <group path>], last`. A second line follows: `model=<id>, effort=<effort>, profile=<name>, streaming=on|off, sound=on|off.` (`default` for an unset model or effort). A third line reads `First turn started.` or `No prompt run.`
- **Errors** (a tool error; nothing is created):
  - the caller is a worker: `create_session is for top-level sessions: a worker reports to its supervisor instead.`;
  - a blank `name`;
  - `after` together with `workingDir` or `group`: `Use either 'after' or 'workingDir'/'group', not both.`;
  - an `after` that is not found, too short or ambiguous (FR.15.13 wording);
  - a `workingDir` outside every project (the FR.15.14 message);
  - an unknown model, effort or profile; the message lists the valid values (for models the first 12, then `(+N more)`);
  - a stored profile without credentials (logged out), naming the profile;
  - a tier violation (FR.16.6);
  - the rate cap: `Session creation limit reached: at most 10 sessions per hour. Try again later.`
- **Guardrails.**
  - Only a top-level session may call the tool.
  - At most `Constants.Agent.MaxCreatedSessionsPerHour` (10) calls succeed per rolling hour across the app. The count is kept in memory, so a daemon restart resets it, and a refused call doesn't count.
  - The depth limit of FR.15.5 can't apply, because every created session is top-level. The cap is what stops a chain of sessions that create sessions.
- **Later contact.** The caller can use `send_to_session` with the returned id, as for any session.
- **Tool description and guidance.**
  - The description says that the new session belongs to the user, that it is not a worker, and that `spawn_session` is the tool for workers.
  - `Constants.Instructions.NoWorkflowTool` lists the tool.
  - The `cmx-orchestration` skill (FR.15.11) says to use it only when the user asks for a new session.
