# UC.15 — Agent Orchestration

Related requirement: [FR.15 — Agent Orchestration](/docs/requirements.md#fr15--agent-orchestration-persistent-worker-sessions)

This file currently covers only the Mailbox Delivery coalescing added by ISSUE.012; the rest of FR.15 predates the use-case convention.

## Actors

- **Supervisor Session** — a session that sends work to Worker Sessions with `send_to_session` in `async` mode.
- **Worker Session** — a session doing that work; its result is delivered back to the supervisor.
- **Scheduler** — `SchedulerService`, which fires Mailbox Deliveries (FR.15.3) as scheduled turns.

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

## Out of scope for this use case set

- Waiting for all workers of a batch before delivering anything (a barrier) — use `orchestrate_parallel` for that (FR.15.8).
