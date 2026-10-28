# TC.15 — Agent Orchestration

Related requirement: [FR.15](/docs/requirements.md#fr15--agent-orchestration-persistent-worker-sessions). Related use cases: [UC.15](/docs/usecases/UC.15-Agent-Orchestration.md).

This file currently covers only Mailbox Delivery coalescing (FR.15.12, ISSUE.012).

## Integration tests (xUnit, `TessynDesktop.Tests`)

### TC.15.1 — Mailbox coalescing (`SchedulerServiceTests`, fake `ISessionTurnService` with a controllable turn lock) — FR.15.3, FR.15.12, FR.14.9; UC.15.1
- `AddOrMergeMailboxDelivery_NoPendingDelivery_AddsDelayZeroMailboxSchedule` — the new schedule targets the supervisor, has `IsMailboxDelivery == true`, note `Worker 'W1' result`, and prompt `[Worker finished] W1` followed by the result text.
- `AddOrMergeMailboxDelivery_PendingDeliveryExists_AppendsResultAndUpdatesNote` — a second result for the same supervisor adds no schedule; the pending one gains a blank line, `[Worker finished] W2` and its text, and its note becomes `Worker results (2): W1, W2`.
- `AddOrMergeMailboxDelivery_DifferentSupervisors_NotMerged`.
- `AddOrMergeMailboxDelivery_ConcurrentCalls_NoResultLost` — many parallel calls for one supervisor end in deliveries that together carry every result exactly once.
- `Poll_DueMailboxDelivery_TargetBusy_StaysPendingAndRunsNoTurn` — while the target's turn lock is held, the delivery is neither removed nor fired.
- `Poll_DueMailboxDelivery_TargetIdle_FiresOneTurnWithAllMergedResults` — after the lock is released, the next poll runs exactly one turn whose prompt contains every merged result in arrival order.
- `Poll_ResultArrivingAfterDeliveryFired_StartsNewDelivery`.
- `Poll_OrdinaryDueSchedule_TargetBusy_StillFiresAndWaitsOnLock` — FR.14.9's behavior is unchanged for non-mailbox schedules.
- `ScheduleModel_IsMailboxDelivery_PersistsThroughSaveAndLoad`; `ScheduleModel_LegacyScheduleWithoutField_LoadsAsOrdinarySchedule`.
- `SendToSessionAsync_AsyncMode_UsesAddOrMergeMailboxDelivery` (`AgentMcpServer` test) — the async completion path hands its result to `AddOrMergeMailboxDelivery` instead of adding a schedule directly.

## Manual / E2E verification

1. **UC.15.1** — Have a supervisor send three short `async` tasks to workers while it is itself busy in a long turn (e.g. it runs a multi-minute command right after dispatching). Confirm that when its turn ends, a single `[Scheduled] Worker results (3): …` turn follows, containing all three results, instead of three separate delivery turns.
