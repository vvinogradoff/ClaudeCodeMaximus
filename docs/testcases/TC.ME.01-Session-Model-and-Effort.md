# TC.ME.01 — The session's model and effort

Related requirements: [FR.12.35](/docs/requirements/FR.12-Input-and-Composer.md), [FR.16.6](/docs/requirements/FR.16-Model-Tier-Governance.md). Use cases: [UC.ME.01](/docs/usecases/UC.ME.01-Session-Model-and-Effort.md).

## Integration tests (xUnit, `TessynDesktop.Tests`)

### TC.ME.01 — The rule (`SessionModelSettingsTests`) — FR.12.35; UC.ME.01, UC.ME.02, UC.ME.03
- `Model_PickWins_DefaultPickMeansNoFlag`.
- `Model_NoPick_SessionThenDirectoryThenApp`.
- `Model_SessionDefault_NoFlagAndNoFallback`.
- `Effort_PickWins_ElseSession_ElseNone`; `Effort_DefaultMeansNone`.
- `Session_IsWhatATurnWithoutPickUses_DefaultWhenNone`.
- `ResolveShown_UnlistedModelIsDefault_ListedMatchedCaseInsensitively`.

### TC.ME.02 — Sticky picks — FR.12.35; UC.ME.01, UC.ME.02
- `RecordPicks_DifferentPick_StoredAndSaved`; `RecordPicks_EqualToCurrent_NothingChanged`; `RecordPicks_DefaultModelOverInheritedModel_StoresDefault`; `RecordPicks_NoOptions_NothingChanged`; `RecordPicks_DefaultPickForAnUnlistedModel_ChangesNothing`; `RecordPicks_ComparesInCatalogForm_AndStoresIt` (`SessionModelSettingsTests`).
- `TurnSend_WithEffortPick_SessionKeepsIt_AndTheNextSendWithoutPickUsesIt`, `TurnSend_DefaultPick_StopsTheInheritedModel_AndStays`, `TurnSend_PickEqualToTheSessionsValue_PinsNothing` (`SessionModelSettingsWireTests`, loopback `turn.send`).
- `SessionSend_WithPick_SessionKeepsIt`, `SessionSend_PickEqualToTheSessionsValue_PinsNothing`, `SessionSend_DefaultForAnUnlistedModel_LeavesTheSessionsModel` (`session.send`); `AttentionReply_WithPick_SessionKeepsIt`, `AttentionReply_WithoutATurn_KeepsNoPick` (`attention.reply`).
- `SchedulesAdd_PicksNotStored` (`SchedulePicksTests`); `ExplicitOptions_AreNotKeptOnTheSession` (`SessionCompactRpcModuleTests`).
- `HeadlessTurn_NoPick_RunsWithSessionModelAndEffort`, `HeadlessTurn_SessionDefaultModel_NoFlagDespiteAnAppModel`, `HeadlessTurn_EffortPick_WinsOverTheSessionsEffort` (`SessionTurnServiceTests`); `EngineTurn_NoPick_RunsWithSessionModelAndEffort` (`SessionModelSettingsWireTests`); `NoOptions_UsesTheSessionsOwnModelAndEffort` (`SessionCompactRpcModuleTests`).

### TC.ME.03 — What the selectors show (`session.get`, `session.headers`) — FR.12.35; UC.ME.01, UC.ME.03
- `SessionGet_LastUsed_ReportsSessionModelAndEffort_NotTheTranscript`.
- `SessionGet_LastUsed_NoOwnModel_DirectoryOrAppModel`; `SessionGet_LastUsed_ModelNotInCatalog_Default`; `SessionGet_LastUsed_ModelInAnotherCase_ShownInCatalogForm`.
- `SessionHeaders_LastUsed_SameRule`.

### TC.ME.04 — The caller's tier (`AgentMcpServer` tests) — FR.16.6
- `CallerTier_SessionDefaultModel_GivesDefaultTier`; `CallerTier_FollowsSessionThenDirectoryThenApp` (`AgentMcpServerCreateSessionTests`).

## End to end (WPF, `TessynDesktop.WPF.E2E.Tests`)

### TC.ME.05 — The selectors show the session's own settings — FR.12.35; UC.ME.01
- Covered by TC.15.9 `Opening_the_created_session_shows_its_model_effort_and_streaming`: the session has a model and an effort of its own and no transcript, and opening it shows both.

## Manual

1. **UC.ME.01** — Pick Max on a session and send. Restart Tessyn and open the session: Effort shows Max. Send without touching the selector: the new ASSISTANT entry is stamped `effort="max"`.
2. **UC.ME.02** — On that session pick Default and send. Restart and open it: Effort shows Default, and the next entry is stamped `effort="default"`.
