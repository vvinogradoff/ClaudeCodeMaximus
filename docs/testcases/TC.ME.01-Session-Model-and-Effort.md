# TC.ME.01 — The session's model and effort

Related requirements: [FR.12.35](/docs/requirements/FR.12-Input-and-Composer.md), [FR.16.6](/docs/requirements/FR.16-Model-Tier-Governance.md). Use cases: [UC.ME.01](/docs/usecases/UC.ME.01-Session-Model-and-Effort.md).

## Integration tests (xUnit, `TessynDesktop.Tests`)

### TC.ME.01 — The rule (`SessionModelSettingsTests`) — FR.12.35; UC.ME.01, UC.ME.02, UC.ME.03
- `Model_PickWins_DefaultPickMeansNoFlag`.
- `Model_NoPick_SessionThenDirectoryThenApp`.
- `Model_SessionDefault_NoFlagAndNoFallback`.
- `Effort_PickWins_ElseSession_ElseNone`; `Effort_DefaultMeansNone`.
- `Session_IsWhatATurnWithoutPickUses_DefaultWhenNone`.

### TC.ME.02 — Sticky picks — FR.12.35; UC.ME.01, UC.ME.02
- `RecordPicks_DifferentPick_StoredAndSaved`; `RecordPicks_EqualToCurrent_NothingChanged`; `RecordPicks_DefaultModelOverInheritedModel_StoresDefault`; `RecordPicks_NoOptions_NothingChanged` (`SessionModelSettingsTests`).
- `TurnSend_WithEffortPick_SessionKeepsIt` (`TurnRpcModule` contract test); `SessionSend_WithPick_SessionKeepsIt` (`session.send`); `AttentionReply_WithPick_SessionKeepsIt` (`attention.reply`).
- `SchedulesAdd_PicksNotStored`; `SessionCompact_OptionsNotStored`.
- `HeadlessTurn_NoPick_RunsWithSessionModelAndEffort` (`SessionTurnServiceTests`); `EngineTurn_NoPick_RunsWithSessionModelAndEffort` (`TurnSettingsResolverTests`); `Compaction_NoPick_UsesSessionModelAndEffort` (`SessionCompactRpcModule` test).

### TC.ME.03 — What the selectors show (`session.get`, `session.headers`) — FR.12.35; UC.ME.01, UC.ME.03
- `SessionGet_LastUsed_ReportsSessionModelAndEffort_NotTheTranscript`.
- `SessionGet_LastUsed_NoOwnModel_DirectoryOrAppModel`; `SessionGet_LastUsed_ModelNotInCatalog_Default`.
- `SessionHeaders_LastUsed_SameRule`.

### TC.ME.04 — The caller's tier (`AgentMcpServer` tests) — FR.16.6
- `CallerTier_SessionDefaultModel_GivesDefaultTier`; `CallerTier_FollowsSessionThenDirectoryThenApp`.

## End to end (WPF, `TessynDesktop.WPF.E2E.Tests`)

### TC.ME.05 — The selectors show the session's own settings — FR.12.35; UC.ME.01
- Covered by TC.15.9 `Opening_the_created_session_shows_its_model_effort_streaming_and_sound`: the session has a model and an effort of its own and no transcript, and opening it shows both.

## Manual

1. **UC.ME.01** — Pick Max on a session and send. Restart Tessyn and open the session: Effort shows Max. Send without touching the selector: the new ASSISTANT entry is stamped `effort="max"`.
2. **UC.ME.02** — On that session pick Default and send. Restart and open it: Effort shows Default, and the next entry is stamped `effort="default"`.
