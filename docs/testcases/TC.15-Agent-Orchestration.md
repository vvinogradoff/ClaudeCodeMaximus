# TC.15 — Agent Orchestration

Related requirement: [FR.15](/docs/requirements/FR.15-Agent-Orchestration.md#fr15--agent-orchestration-persistent-worker-sessions). Related use cases: [UC.15](/docs/usecases/UC.15-Agent-Orchestration.md).

This file covers Mailbox Delivery coalescing (FR.15.12, ISSUE.012), the session listing (FR.15.13) and `create_session` (FR.15.15).

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

### TC.15.2 — Listing scope, order and paging (`SessionListPageBuilderTests`, snapshots built in the test) — FR.15.13; UC.15.2
- `Project_Default_ListsTopLevelSessionsOfCallersDirectoryOnly`.
- `Project_Default_CallerWorkersNestedUnderCallerRow_OtherWorkersOnlyCounted` — another supervisor's row carries `WorkerCount`/`RunningWorkerCount`, its workers are not rows.
- `Project_CallerIsWorker_CallerRowListed`.
- `Project_CallerWorkerInOtherDirectory_NestedWithOtherDirectoryLabel`.
- `Order_NewestLastActivityFirst_TiesByNameThenNodeId_MissingFileLast`.
- `Nesting_SiblingsNewestFirst_IndentPerLevel`.
- `All_TopLevelRowsGroupedByDirectory_GroupsOrderedByNewestRow` — section counts cover the whole directory, before filters.
- `IncludeWorkers_EveryWorkerListedUnderItsSupervisor`.
- `Children_ListsDirectWorkersOfRoot_RootDefaultsToCaller_NoWorkerOfFlag`.
- `Subtree_ListsAllDescendants_NestedByLevel`.
- `Paging_OffsetAndLimit_CountNestedRows_TotalRowsReported`.
- `Paging_LimitClampedTo1And200_NegativeOffsetCountsAsZero`.
- `Paging_SectionOfFirstRowAlwaysIncluded`.
- `Paging_PageStartsInsideNestedBlock_OrphanedRowsFlaggedWorkerOf` — nested rows whose supervisor's row is on an earlier page carry `worker of <id>` (or `worker of you`).
- `WorkersHint_SetWhenAListedRowHasUnlistedWorkers`.

### TC.15.3 — Listing filters (`SessionListPageBuilderTests`) — FR.15.13; UC.15.3
- `Query_MatchesNameCaseInsensitively_IncludesWorkers`.
- `Query_MatchesGroupNameOnTreePath`.
- `Query_MatchesNodeIdPrefix`.
- `Query_WorkerWithoutListedSupervisor_FlaggedWorkerOf` — `WorkerOfShortId` set; for the caller's own worker `IsWorkerOfCaller` instead.
- `Running_TrueListsOnlyRunning_FalseListsOnlyIdle`.
- `ActiveWithinHours_ExcludesOlderSessionsAndMissingFiles`.
- `Query_MatchesSupervisorAndWorker_WorkerNestedUnderSupervisor`.
- `Subtree_MiddleNodeFilteredOut_GrandchildFlaggedWorkerOfMiddle`.
- `Query_SurroundingWhitespaceIgnored`.

### TC.15.4 — Listing text (`SessionListFormatterTests`, page models built in the test) — FR.15.13
- `YouLine_TopLevelForm`; `YouLine_WorkerForm`; `YouLine_SupervisorGone_OfPartDropped`.
- `SectionHeader_DirectoryForm`; `SectionHeader_WorkersForm`; `SectionHeader_PrintedWhenSectionChanges`.
- `Row_FlagsInSpecifiedOrder_OnlyWhenApplicable`.
- `Row_IndentTwoSpacesPerLevel`.
- `Row_NameCutTo60CharactersEndingInDots` — never splits a surrogate pair.
- `Age_Buckets` (theory) — `now`, `m`, `h`, `d`, `w`, `mo`, `y` boundaries; `Age_Buckets_MissingFile_Dash` — missing file → `-`.
- `Footer_RowsRangeAndNextPage`; `Footer_HintsOnlyWhenTheyApply`; `Footer_NoRows_NoSessionsMatch`; `Footer_OffsetPastEnd_NoRowsAtOffset`.
- `YouLineAndHeaders_NamesCutTo60Characters`; `SectionHeader_DriveRootPathKept`.
- `Output_AsciiNames_WholeOutputIsAscii`.
- `Names_NonAsciiPrintedAsIs_ControlCharactersBecomeSpaces` — e.g. `PH v4 — post` stays intact, a name with a line break stays on one row.

### TC.15.5 — Listing cost (`SessionRegistryListingTests`, fake file and status services) — FR.15.13, FR.21.8
- `QueryAgentSessions_NeverReadsSessionFileContents`.
- `QueryAgentSessions_LastActivityIsLastWriteTime`.
- `QueryAgentSessions_ResumabilityCheckedOnlyForPageRows_OneProjectsDirListPerCall`.
- `QueryAgentSessions_SettingsLockNotHeldDuringFileSystemWork` — while the fake last-write lookup, and separately the fake resumability check, is running, another thread acquires `SyncRoot` within a timeout.
- `BuildAgentSessionSummaries_LastActivityIsLastWriteTime_NoContentReads` (replaces the FR.21.8 test `BuildAgentSessionSummaries_IncludesIsFavoriteAndLastActivityUtc`).
- `BuildAgentSessionSummaries_SettingsLockNotHeldDuringFileSystemWork`.

### TC.15.6 — Short Node Ids — FR.15.13
- `ShortIds_AtLeast8Characters_ExtendedUntilUnique` (`SessionListPageBuilderTests`).
- `ResolveNodeId_FullId_CaseInsensitive`; `ResolveNodeId_UniquePrefix_Resolved`; `ResolveNodeId_AmbiguousPrefix_ReturnsUpTo5Matches`; `ResolveNodeId_PrefixShorterThan6_TooShort`; `ResolveNodeId_Unknown_NotFound` (`SessionRegistryListingTests`).
- `Tools_AcceptShortNodeIds` (`AgentMcpServerListSessionsTests`, theory) — `send_to_session` (`wait`), `read_session`, `stop_session` (cancels the resolved full NodeId), `set_session_model`, `schedule_wake` `target`, `spawn_session` `parentNodeId` and `list_sessions` `nodeId` each work with an 8-character prefix.
- `Tools_AmbiguousPrefix_ErrorListsMatches`; `Tools_PrefixTooShort_Error`; `Tools_IdArgumentsTrimmed` (`AgentMcpServerListSessionsTests`).
- `Tools_UnknownId_KeepsEachToolsNotFoundWording` (`AgentMcpServerListSessionsTests`) — every tool that takes a session id; `stop_session` instead passes the unknown id on and reports `No active turn for <id>`.

### TC.15.7 — `list_sessions` tool surface (`AgentMcpServerListSessionsTests`, real `SessionRegistry` over a temp settings dir) — FR.15.13, FR.15.11
- `ToolsList_ListSessions_DeclaresAllArguments` — `scope` enum, `nodeId`, `query`, `running`, `activeWithinHours`, `includeWorkers`, `limit`, `offset`.
- `ListSessions_NoArgs_YouLineHeaderRowsFooter`.
- `ListSessions_InvalidScope_Error` (theory: an unknown word, a number such as `2`, and a comma list such as `project,all`); `ListSessions_NonPositiveActiveWithinHours_Error`; `ListSessions_UnknownNodeId_Error`.
- `ListSessions_2000SessionTree_DefaultPageUnder5000Chars_FullPageUnder30000Chars`.
- `OrchestrationSkill_WorkerSection_NoLongerSendsWorkersToFullListing`.

### TC.15.8 — `create_session` (`AgentMcpServerCreateSessionTests`, real `SessionRegistry` over a temp settings dir, fake turn service) — FR.15.15, FR.16.6, FR.12.35; UC.15.4
- `ToolsList_CreateSession_DeclaresAllArguments` — `name` is required; `after`, `workingDir`, `group`, `model`, `effort` (enum), `profile`, `streaming`, `sound` and `prompt` are optional.
- `CreateSession_After_GoesRightAfterAnchorInItsGroup_TopLevelFields` — index = the anchor's + 1 in the anchor's group. `SupervisorNodeId` is null, `OrchestrationDepth` is 0, `NodeId` and `AgentToken` are set, and the working directory is the container's.
- `CreateSession_AfterWorker_GoesAfterItsTopLevelSupervisor`.
- `CreateSession_AfterShortId_Resolved`; `CreateSession_AfterUnknownOrAmbiguous_ErrorAndNothingCreated`.
- `CreateSession_NoPlacement_GoesRightAfterCaller`.
- `CreateSession_WorkingDir_GoesLastInDirectory`; `CreateSession_WorkingDirAndGroup_MissingGroupCreated_GoesLastInIt`; `CreateSession_GroupOnly_UsesCallersDirectory`.
- `CreateSession_AfterWithWorkingDirOrGroup_Error`; `CreateSession_WorkingDirOutsideProjects_ErrorListsRegistered`; `CreateSession_BlankName_Error`.
- `CreateSession_Settings_Stored` — `ModelId`, `Effort`, the `ProfileId` pin, `IsStreamingMode` and `PlayCompletionSound`. Defaults: streaming off, sound on, the caller's profile.
- `CreateSession_Profile_ByIdByNameOrDefault`; `CreateSession_UnknownModelEffortOrProfile_ErrorListsValidValues`; `CreateSession_LoggedOutProfile_Error`.
- `CreateSession_OneSessionAddedEvent_SettingsAlreadySet` — the event's session already carries the settings.
- `CreateSession_WorkerCaller_Refused`.
- `CreateSession_ModelAboveCallerTier_Refused` and `CreateSession_ModelAtCallerTier_Allowed` (FR.16.6).
- `CreateSession_RateCap_EleventhCallWithinAnHourRefused_RefusedCallsDoNotCount`.
- `CreateSession_Prompt_StartsOrchestratedTurnAndReturnsAtOnce`:
  - the fake turn service blocks, and the tool has already returned;
  - the turn runs with `TurnSource.Orchestrated` and without per-turn options, so the session's settings apply;
  - no Mailbox Delivery is added.
- `CreateSession_ResultText_NamesPlaceAndSettings`.
- `OrchestrationSkill_AndNoWorkflowTool_NameCreateSession`.

### TC.15.9 — `create_session` in the WPF client (`WpfCreateSessionE2ETests`, the built app against a loopback daemon) — FR.15.15, FR.12.35; UC.15.4
- `A_created_session_appears_right_under_its_anchor_and_the_selection_stays`:
  - the daemon creates a session, after a session in a group, with the registry call that `create_session` makes;
  - the tree shows it as the next row under the anchor;
  - the selected session doesn't change.
- `Opening_the_created_session_shows_its_model_effort_streaming_and_sound` — the Model selector shows its model and Effort shows Max; the Streaming and Sound toggles are on.

## Manual / E2E verification

1. **UC.15.1** — Have a supervisor send three short `async` tasks to workers while it is itself busy in a long turn (e.g. it runs a multi-minute command right after dispatching). Confirm that when its turn ends, a single `[Scheduled] Worker results (3): …` turn follows, containing all three results, instead of three separate delivery turns.
2. **UC.15.2** — In a supervisor with workers, call `list_sessions` with no arguments. Confirm the reply is a few KB, the `You:` line names the caller, its workers are nested under its row, and `send_to_session` works with a printed short id.
3. **UC.15.3** — Call `list_sessions` with `query=` part of a session name from another project: the footer suggests `scope=all`; with `scope=all` the session is found under its directory's header.
4. **UC.15.4** — From a session, ask for "an Opus session named X with Max effort, streaming and sound on, right under session Y". Confirm that X appears under Y at once and the selection stays. Opening X shows Opus, Max, streaming on and the bell on.
