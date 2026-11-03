# UC.15 — Agent Orchestration

Related requirement: [FR.15 — Agent Orchestration](/docs/requirements.md#fr15--agent-orchestration-persistent-worker-sessions)

This file currently covers the Mailbox Delivery coalescing added by ISSUE.012 and the session listing (FR.15.13); the rest of FR.15 predates the use-case convention.

## Actors

- **Supervisor Session** — a session that sends work to Worker Sessions with `send_to_session` in `async` mode.
- **Worker Session** — a session doing that work; its result is delivered back to the supervisor.
- **Scheduler** — `SchedulerService`, which fires Mailbox Deliveries (FR.15.3) as scheduled turns.
- **Any session** — a session that looks up other sessions with `list_sessions`.

## Preconditions

- `AgentToolsEnabled` is true; the Supervisor Session has sent prompts to several Worker Sessions with `mode=async`.

---

## UC.15.1 — Worker results that arrive while the supervisor is busy are delivered together

**Trigger:** Two or more Worker Sessions finish while the Supervisor Session is still in a turn (for example, absorbing an earlier worker's result) or before a pending delivery has fired.

1. The first result creates a pending Mailbox Delivery for the supervisor (FR.15.3).
2. Each further result is appended to that pending delivery instead of creating a new one; the delivery's note lists the workers in arrival order (`Worker results (<n>): <names>`).
3. While the supervisor's turn is in progress, the due delivery is held pending, not queued on the turn lock (FR.15.12), so it keeps absorbing results.
4. On the first scheduler poll after the supervisor becomes idle, the delivery fires as one turn whose prompt carries every result, each under its own `[Worker finished] <name>` line.

**Outcome:** N results that arrive close together cost the supervisor one turn instead of N, and in Telegram they appear as one answer instead of N (UC.21.12). Results that arrive after a delivery has fired start a new delivery.

---

## UC.15.2 — A supervisor finds its own workers again

**Trigger:** a Supervisor Session must decide whether to resume a worker or spawn a new one (often after its context was compacted or after a long pause) and no longer knows its workers' ids.

1. It calls `list_sessions` with no arguments.
2. Tessyn returns one page — a `You:` line, its project's header, the project's top-level sessions newest first, with the supervisor's own workers nested under its row (FR.15.13).
3. It picks a worker by name, age and flags (`running`, `no-resume`) and calls `send_to_session` with the printed short id — or, with more workers than fit on a page, pages with `offset` or narrows with `scope=children`, `query` or `running`.

**Outcome:** one small reply (typically 1–2 KB) instead of the whole tree; the worker is resumed without the user's help.

---

## UC.15.3 — A session finds another session by name

**Trigger:** a session needs to message or inspect a session it did not spawn (a peer orchestrator, or work in another project).

1. It calls `list_sessions` with `query=` part of the name.
2. Matches in its project are listed, workers included, and a worker not shown under its supervisor is flagged `worker of <id>`.
3. With no match the footer suggests `scope=all`.
4. With `scope=all` it gets the matches of every project, grouped by directory.

**Outcome:** the target's short id in one or two small calls.

---

## Out of scope for this use case set

- Waiting for all workers of a batch before delivering anything (a barrier) — use `orchestrate_parallel` for that (FR.15.8).
