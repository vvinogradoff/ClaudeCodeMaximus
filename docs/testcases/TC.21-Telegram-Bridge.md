# TC.21 — Telegram Notification Bridge

Related requirement: [FR.21](/docs/requirements/FR.21-Telegram-Bridge.md#fr21--telegram-notification-bridge). Related use cases: [UC.21](/docs/usecases/UC.21-Telegram-Bridge.md).

All Telegram Bot API calls are abstracted behind `ITelegramClient`, so these test cases run against a fake implementation (no real network/bot) except where noted as manual verification.

## Integration tests (xUnit, `TessynDesktop.Tests`)

### TC.21.1 — `TelegramTokenStoreTests`
- Round-trips a bot token through DPAPI save/load (`SaveThenLoad_RoundTrips`).
- `Load_NoStoredToken_ReturnsNull`.
- `Delete_RemovesToken_SubsequentLoadReturnsNull`.
- Mirrors `McpTokenStoreTests`' structure (temp directory per test instance, cleaned up on dispose).

### TC.21.2 — `TelegramClientTests`
- `SendMessageAsync_IncludesChatIdAndThreadId_WhenThreadIdProvided` — verifies the outgoing JSON body via a fake `HttpMessageHandler`.
- `SendMessageAsync_OmitsThreadId_WhenNull` — general/non-topic message.
- `SendMessageAsync_DisableNotificationTrue_SendsDisableNotificationTrue` — the JSON body carries `"disable_notification": true` (FR.21.21).
- `SendMessageAsync_DisableNotificationFalse_SendsDisableNotificationFalse` — the JSON body carries `"disable_notification": false` (FR.21.21).
- `CreateForumTopicAsync_ParsesMessageThreadIdFromResponse`.
- `GetUpdatesAsync_ParsesMultipleUpdates_IncludingOptionalMessageThreadId`.

### TC.21.3 — `TelegramBridgeServiceTests`
- `EnsureTopicForSessionAsync_FirstCall_CreatesTopicAndPersistsMapping`.
- `EnsureTopicForSessionAsync_SecondCall_DoesNotCreateTopicAgain` (idempotent — covers UC.21.1's "created lazily, once").
- `NotifyAsync_SessionWithNoTopicYet_CreatesTopicThenSends` (UC.21.1).
- `NotifyAsync_TelegramDisabled_IsNoOp_DoesNotThrow`.
- `HandleUpdate_MessageInMappedTopic_RunsTurnOnCorrectSession` (UC.21.3) — asserts the fake `ISessionTurnService` receives the right NodeId + prompt text.
- `HandleUpdate_UnauthorizedUserId_DropsMessage_NoTurnRun` (UC.21.6).
- `HandleUpdate_WrongChatId_DropsMessage_NoTurnRun` (UC.21.6).
- `HandleUpdate_SessionsCommand_ListsFavoritedAndTopicMappedSessionsOnly` (UC.21.4).
- `HandleUpdate_OpenCommand_ReturnsLastEntriesForMatchedSession` (UC.21.5) — including a session with no topic yet.
- `HandleUpdate_UnmappedThreadAndUnrecognizedCommand_RepliesWithUnrecognizedMessage`.

### TC.21.4 — `SessionRegistryTests` additions
- `BuildAgentSessionSummaries_IncludesIsFavoriteAndLastActivityUtc` — extends existing coverage for the FR.21.8 field additions, asserting `LastActivityUtc` reflects the last session-file entry's timestamp regardless of role.

### TC.21.5 — `InstructionBlockBuilderTests` additions
- `Build_AgentToolsAndTelegramEnabledOnNewSession_IncludesTelegramNotifyGuidance`.
- `Build_TelegramDisabled_OmitsTelegramNotifyGuidance`.
- `Build_NotANewSession_OmitsTelegramNotifyGuidanceEvenIfEnabled` (mirrors the existing once-per-fresh-session gating already tested for `NoWorkflowTool`).

### TC.21.6 — Update acknowledgement and attachment parsing (`TelegramClientTests`, `TelegramBridgeServiceTests`) — FR.21.12, FR.21.13; ISSUE.011
- `GetUpdatesAsync_ReturnsEveryUpdate_FlagsNonActionable` (non-message, edited_message, message without `from`, unsupported type).
- `GetUpdatesAsync_Caption_UsedAsText`, `..._Photo_TakesLargestSize`, `..._Document/Video/Audio/Voice/VideoNote/Animation_Parsed`.
- `PollLoop_OnlyNonActionableUpdates_AdvancesAndPersistsOffsetPastMaxUpdateId`; `PollLoop_FilteredChatUpdates_StillAcknowledged`.
- Plain-text `/sessions`, `/open`, `/stop` handling unchanged (existing TC.21.3 tests); `HandleUpdate_CaptionLooksLikeCommandWithAttachment_NotTreatedAsCommand`. Tests live in `TelegramBridgeServiceAttachmentTests.cs` / `TelegramClientAttachmentTests.cs`.

### TC.21.7 — Download, storage and turn text (`TelegramInboxTests`, `TelegramClientTests`, `TelegramBridgeServiceTests`) — FR.21.14, FR.21.15, FR.21.17
- `GetFileAsync_/DownloadFileAsync_` URL shape via fake handler; "file is too big" mapped to a too-large result.
- File naming: `{updateId}-{index}-{name}{ext}`, sanitization, extension inference (name → MIME → file_path, photo → `.jpg`), collision-free names.
- `BuildTurnText`: text + `[attachment: path]` lines; > 20 MB note line; download-failure note line; text-only unchanged; attachment-only message.
- `HandleUpdate_AttachmentMessage_RoutesTurnWithAttachmentLines_AndEchoStillSuppressed`; `HandleUpdate_DownloadFails_StillRoutesTextWithFailureLine`.
- `Cleanup_DeletesFilesOlderThan30Days_KeepsRecent`.

### TC.21.8 — Albums (`TelegramBridgeServiceTests`) — FR.21.16
- `Album_SameBatch_CoalescedIntoOneTurn_FirstCaptionWins_OrderPreserved`.
- `Album_SplitAcrossBatches_FollowUpPollMergesIntoOneTurn`.
- `Album_FollowedByOtherUpdate_DispatchedWithoutFollowUpPoll`.

### TC.21.9 — `EditForumTopicAsync` (`TelegramClientTests`) — FR.21.18
Run against a fake `HttpMessageHandler`.
- `EditForumTopicAsync_Success_PostsRequestShapeAndReturnsTrue` — POSTs `editForumTopic` with JSON `chat_id`, `message_thread_id` and `name` equal to the inputs; returns `true` on `{"ok":true,"result":true}`.
- `EditForumTopicAsync_NameOverMaxLength_SendsTruncatedTo128` — a name longer than `Constants.Telegram.MaxTopicNameLength` is sent as exactly 128 characters (same as `CreateForumTopicAsync`).
- `EditForumTopicAsync_TopicNotModified_ReturnsFalseWithoutThrowing` — `{"ok":false,"error_code":400,"description":"Bad Request: TOPIC_NOT_MODIFIED"}` returns `false`.
- `EditForumTopicAsync_OtherError_ThrowsTelegramApiExceptionWithCodeAndDescription` — e.g. 400 `TOPIC_ID_INVALID` throws `TelegramApiException` with `ErrorCode` and `Description`; `RetryAfterSeconds` is `null` when the response has no `parameters`.
- `EditForumTopicAsync_RateLimit_ThrowsWithRetryAfterSeconds` — 429 with `parameters.retry_after` 7 throws `TelegramApiException` with `RetryAfterSeconds == 7`.
- `SendMessageAsync_ApiError_StillThrowsInvalidOperationException` — existing client methods keep their `InvalidOperationException` behaviour.

### TC.21.10 — Topic name reconciliation (`TelegramBridgeService` reconciliation tests, fake `ITelegramClient`, `DelayAsync` seam recording requested delays) — FR.21.2, FR.21.18; UC.21.8
- `Reconcile_NameDiffersFromStored_RenamesOnceAndRecords` — a differing stored name triggers one `EditForumTopicAsync` with the current name, which is stored in `TelegramTopicNamesByNodeId`.
- `Reconcile_NoStoredName_RenamesAndRecords` — a mapped topic with no stored name is renamed and recorded (migration).
- `Reconcile_StoredNameEqualsCurrent_MakesNoCallAndNoWait` — equal names: no API call and no delay.
- `Reconcile_OrphanMapping_SkippedAndRunContinues` — no session for the NodeId: no call, mapping untouched, next topic processed.
- `EnsureTopicForSessionAsync_NewTopic_RecordsNameAndReconcileMakesNoCall` — the applied name is recorded at topic creation, and reconciliation then makes no call.
- `Reconcile_Renamed_WaitsTopicRenameIntervalAfterRename` — requested delay after a real rename is 6000 ms.
- `Reconcile_NotModified_RecordsNameContinuesAndWaitsNoOpInterval` — `false` result: name recorded, run continues, requested delay is 1000 ms.
- `Reconcile_RateLimitThenSuccess_RetriesSameTopicOnceAndRecords` — 429 → wait `retry_after` → one retry of the same topic, name recorded.
- `Reconcile_RateLimitTwice_StopsRunAndDoesNotAttemptLaterTopics` — failed retry stops the run.
- `Reconcile_RateLimit_WaitsRetryAfterClampedToOneThroughThreeHundredSeconds` — 0 → 1 s, 7 → 7 s, 1000 → 300 s.
- `Reconcile_RateLimitWithoutRetryAfter_WaitsOneSecond` — missing `retry_after` → 1 s.
- `Reconcile_RightsError_StopsRunWithSingleWarningAndNothingRecorded` — 403, and 400 "not enough rights": no further calls, one warning, nothing recorded.
- `Reconcile_ChatNotFound_StopsRunWithSingleWarning` — "chat not found" stops the run with one warning.
- `Reconcile_OtherApiError_SkipsTopicWaitsNoOpIntervalAndProcessesRest` — e.g. `TOPIC_ID_INVALID`: topic skipped (not recorded), one warning, 1000 ms delay, remaining topics processed.
- `Reconcile_NetworkFailure_StopsRunWithoutThrowing` — a non-API exception is logged and stops the run; nothing is thrown out of `Start()`.
- `Reconcile_TimeoutWithoutStopRequest_IsLoggedAsNetworkFailureAndStopsRun` — a cancellation not caused by the bridge stopping (HTTP timeout) is logged as a network failure and stops the run.
- `Reconcile_ChatIdNotConfigured_DoesNothing` — `TelegramChatId` 0: no calls.
- `Reconcile_FortyFiveTopics_SavesAfter20thAnd40thAndAtEnd` — 3 saves.
- `Reconcile_StoppedEarly_SavesWhatWasRecorded` — early stop still saves recorded names.
- `Reconcile_StopDuringThrottleDelay_EndsPromptlyQuietly` — stopping during a throttle delay ends the run promptly, no warnings.
- `Reconcile_StopDuringRateLimitWait_EndsPromptlyQuietly` — same during a 429 wait.
- `Start_FirstEditBlocks_ReturnsImmediatelyAndPollingProceeds` — `Start()` does not wait for the reconciliation.
- `Start_StopThenStartAgainWhileFirstEditBlocked_SecondRunWaitsForFirstToEnd` — runs never overlap.

### TC.21.11 — `DeleteForumTopicAsync` (`TelegramClientTests`) — FR.21.19
Run against a fake `HttpMessageHandler`, mirroring TC.21.9's style.
- `DeleteForumTopicAsync_Success_PostsRequestShapeAndReturnsTrue` — POSTs `deleteForumTopic` with JSON `chat_id` and `message_thread_id` equal to the inputs; returns `true` on `{"ok":true,"result":true}`.
- `DeleteForumTopicAsync_TopicIdInvalid_ReturnsTrueWithoutThrowing` — `{"ok":false,"error_code":400,"description":"Bad Request: TOPIC_ID_INVALID"}` returns `true` (already gone is the desired post-condition).
- `DeleteForumTopicAsync_OtherError_ThrowsTelegramApiExceptionWithCodeAndDescription` — e.g. 403 "not enough rights" throws `TelegramApiException` with `ErrorCode`/`Description`; `RetryAfterSeconds` is `null` when the response has no `parameters`.
- `DeleteForumTopicAsync_RateLimit_ThrowsWithRetryAfterSeconds` — 429 with `parameters.retry_after` 3 throws `TelegramApiException` with `RetryAfterSeconds == 3`.

### TC.21.12 — Worker Session exclusion (`TelegramBridgeService` tests, fake `IDaemonClient`/`ITelegramClient`) — FR.21.19; UC.21.9, UC.21.10
- `EnsureTopicForSessionAsync_WorkerSession_DoesNotCreateTopic` — `OrchestrationDepth > 0` skips topic creation entirely; no mapping is persisted.
- `NotifyAsync_WorkerSession_ReturnsFalse_NoTopicNoSend` — a worker's `notify_user` call returns `false` and sends nothing.
- `EntryAppended_WorkerSession_DoesNotMirror` — a worker's appended USER/ASSISTANT/SYSTEM entries are never mirrored and never trigger topic creation.
- `HandleUpdate_SessionsCommand_ExcludesWorkerSessionsEvenIfFavorited` — `/sessions` omits a worker even when `IsFavorite` is true and it already has a mapped topic.
- `Start_DeletesExistingTopicsForWorkerSessions_AndRemovesMappings` — a pre-existing worker-session topic is deleted via `DeleteForumTopicAsync` and both its `TelegramTopicThreadIdsByNodeId`/`TelegramTopicNamesByNodeId` entries are removed; a depth-0 session's topic is left untouched.
- `Start_WorkerTopicDeleteFails_LeavesMappingForRetry` — a delete failure (e.g. HTTP 500) leaves the mapping in place so the next `Start()` retries it.

### TC.21.13 — Silent delivery (`TelegramBridgeServiceTests`, fake `ITelegramClient` recording `disableNotification`) — FR.21.21; UC.21.1, UC.21.11
- `NotifyAsync_SendsWithSound_AndMentionPrefix` — a `notify_user` message is sent with `disableNotification == false` and the `TelegramMentionHandle` prefix.
- `Mirror_FullMode_EveryPostIsSilent` — USER, ASSISTANT and SYSTEM mirror posts (including every chunk of a long entry) are sent with `disableNotification == true` and no mention prefix.
- `Mirror_AnswersOnlyMode_AnswerPostIsSilent` — the turn's answer posted on `TurnCompleted` is sent with `disableNotification == true`.
- `HandleUpdate_CommandReplies_AreSilent` — the replies to `/sessions`, `/open`, `/stop` and an unrecognized message are all sent with `disableNotification == true`.

### TC.21.14 — Mirror modes and the Turn Completed event (`TelegramBridgeServiceTests`, `SessionFileServiceTests`, `SessionTurnServiceTests`) — FR.21.22; UC.21.11
- `AnswersOnly_TurnWithSeveralAssistantEntries_PostsOnlyTheLastOnTurnCompleted` — three ASSISTANT entries, then `TurnCompleted`: exactly one post, carrying the third entry; nothing is posted before `TurnCompleted`.
- `AnswersOnly_UserAndSystemEntries_NotPosted` — USER and SYSTEM entries produce no post, before or after `TurnCompleted`.
- `AnswersOnly_CancelledTurn_PostsNothingAndClearsPending` — `TurnCompleted` with `cancelled == true` posts nothing, and a later completed turn posts only its own answer.
- `AnswersOnly_TurnCompletedWithoutAssistantEntry_PostsNothing`.
- `AnswersOnly_UsageLimitTurn_PostsLimitTextOnce` — an ASSISTANT entry `You've hit your session limit · resets 1:50pm`, a SYSTEM entry with the same text, then `TurnCompleted` with that error text: exactly one silent post carrying the text unchanged.
- `AnswersOnly_ErrorTextWithoutAssistantEntry_PostsErrorTextWithGearPrefix` — no ASSISTANT entry, `TurnCompleted` with error text: one post, `⚙️ ` + the text.
- `AnswersOnly_AnswerThenDifferentErrorText_PostsBothInOrder` — the kept answer first, then `⚙️ ` + the error text.
- `AnswersOnly_EntryOutsideTurn_SupersededByNextTurnsAnswer` — an ASSISTANT entry with no `TurnCompleted` is not posted; after the next turn's entry and `TurnCompleted`, only the newer entry is posted.
- `AnswersOnly_LongAnswer_ChunkedLikeFullMode` — an answer over 4096 characters is split as FR.21.9 specifies.
- `AnswersOnly_IsDefaultMode` — a fresh `AppSettingsModel` has `TelegramMirrorMode == AnswersOnly`.
- `Off_PostsNothing_ButNotifyStillSends` — no mirror post in any case; `NotifyAsync` still sends.
- `Full_PostsEveryEntryAsBefore` — the existing FR.21.9 mirror tests, run with `TelegramMirrorMode = Full`.
- `TurnCompleted_WorkerSession_Ignored` — a Worker Session's `TurnCompleted` posts nothing in any mode (FR.21.19).
- `SessionFileServiceTests.NotifyTurnCompleted_RaisesTurnCompleted_WritesNothing` — the event carries the file name, the `cancelled` flag and the error text, and the session file is unchanged.
- `SessionTurnServiceTests.RunTurnAsync_RaisesTurnCompletedOnceAfterLastEntry` — exactly one `TurnCompleted` per turn, raised after the turn's last `EntryAppended`, with no error text on success.
- `SessionTurnServiceTests.RunTurnAsync_Cancelled_RaisesTurnCompletedWithCancelledTrue`.
- `SessionTurnServiceTests.RunTurnAsync_ErrorResult_RaisesTurnCompletedWithErrorText` — a usage-limit error result gives `cancelled == false` and the result's text as error text.
- `SessionTurnServiceTests.RunTurnAsync_AuthRaceRetriedSuccessfully_NoErrorText` — an ISSUE.010 OAuth-race result that is retried and succeeds raises one `TurnCompleted` with no error text.
- `SessionTurnServiceTests.RunTurnAsync_StreamingRedirect_RaisesTurnCompleted` (FR.19.13).

### TC.21.15 — Mailbox Deliveries in Full mode (`TelegramBridgeServiceTests`) — FR.21.23; UC.21.11
- `Full_MailboxDeliveryEntry_PostedAsFirstLineWithGearPrefix` — a USER entry `[Scheduled] Worker 'W2' result` + `[Worker finished] W2` + result text posts only `⚙️ [Scheduled] Worker 'W2' result`.
- `Full_MergedMailboxDelivery_PostedAsOneLine` — a merged delivery (`Worker results (2): W2, W3`) posts one line.
- `Full_OrdinaryUserEntry_StillPostedWithUserPrefix` — a USER entry without a `[Worker finished]` line is mirrored as before.

### TC.21.16 — Scheduled-turn notify reminder (`InstructionBlockBuilderTests`, `SessionTurnServiceTests`) — FR.21.24; UC.21.12
- `Build_ScheduledTurnWithAgentToolsAndTelegram_IncludesScheduledTurnNotifyReminder`.
- `Build_ScheduledTurn_IncludesReminderEvenWhenNotANewSession` — not subject to the once-per-fresh-session gate.
- `Build_ScheduledTurn_WorkerSession_OmitsReminder`.
- `Build_ScheduledTurn_TelegramDisabled_OmitsReminder` and `..._AgentToolsDisabled_OmitsReminder`.
- `Build_NotAScheduledTurn_OmitsReminder`.
- `SessionTurnServiceTests.RunTurnAsync_ScheduledSource_SendsReminder_OtherSourcesDoNot` — `TurnSource.Scheduled` carries the reminder; `User`, `Orchestrated` and `Telegram` do not; covered for the one-shot and the streaming-redirect call sites.

### TC.21.17 — Notification channels (`SchedulerServiceTests`, `AgentMcpServerNotifyUserTests` with fake `INotificationService` and `ITelegramBridgeService`) — FR.21.5; UC.21.1, UC.21.12
- `FireSchedule_CompletedTurn_RunsTurnWithoutAnyNotification` — `SchedulerService` is constructed without a notification service (the dependency no longer exists); firing a schedule runs the turn and removes or re-arms the schedule exactly as before. Existing `SchedulerServiceTests` are updated for the removed constructor parameter, and their `FakeNotificationService` moves to the `notify_user` tests below.
- `NotifyUser_TopLevelSession_SendsTelegramAndShowsToast` — both channels receive the message; the toast's title is the session's `{directory label} / {session name}` and its body the message.
- `NotifyUser_WorkerSession_ReachesNoChannel` — neither the bridge nor the toast is called, and the tool result says nothing was sent (FR.21.19).
- `NotifyUser_ToolResult_NamesDeliveredChannels` — Telegram and desktop → `Notification sent (Telegram, desktop).`; one of them → only that channel named; neither → a "not sent" result.
- `NotifyUser_TelegramFails_ToastStillShown` and `NotifyUser_ToastUnavailable_TelegramStillSent` — the channels are independent.
- The toast itself (`WindowsNotificationService`: `NotificationsEnabled` gating, body truncation, click-to-select) lives in the app project, which the Core-only test project cannot reach — covered by manual item 10.

## Manual / E2E verification (requires a real bot + supergroup — cannot run in CI)

Follow FR.21's setup steps to provision a real bot, supergroup, and chat ID first.

1. **UC.21.1** — Enable the bridge in Settings, run any session turn, and confirm a new forum topic appears in the supergroup named `{directory label} / {session name}`. Have a session call `notify_user` and confirm the message arrives with the mention prefix and with sound.
2. **UC.21.2 / UC.21.3** — Have a session call `notify_user` with a question; confirm the turn completes without blocking; reply in the topic; confirm the reply becomes the session's next turn and Tessyn's session view shows the exchange.
3. **UC.21.4** — Send `/sessions` from Telegram; cross-check the list against the app's favorites and any sessions already active via the bridge.
4. **UC.21.5** — Send `/open <name>` for a session that has never had a turn since the bridge was enabled; confirm output is returned without a topic being created as a side effect.
5. **UC.21.6** — From a second Telegram account not on the allow-list, message the bot directly; confirm no reply and a warning in the Tessyn log.
6. Restart Tessyn mid-session and confirm `TelegramUpdateOffset` persistence prevents replayed/duplicate command handling for messages sent before the restart.
7. **FR.21.18 / UC.21.8** — With existing topics named with full paths (e.g. `C:\Projects\oDesk\HardStop / ...`), restart Tessyn with the bridge enabled. Confirm the app and bridge respond immediately, and that topics are renamed gradually (about one per 6 s, each leaving a "topic renamed" service message) to `HardStop / <session>`. Restart again and confirm no topics are renamed. Rename a session in the tree, restart, and confirm only that topic is renamed.
8. **FR.21.19 / UC.21.9 / UC.21.10** — Have a session spawn several Worker Sessions (e.g. `orchestrate_parallel` with a handful of tasks). Confirm no topic appears for any worker, only the originating session's own topic shows activity, and `/sessions` never lists a worker. Then, with one or more worker topics already present from before this exclusion existed (or created by temporarily bypassing the guard), restart Tessyn and confirm those topics are deleted from the group automatically on the next start, with the originating session's own topic left untouched.
9. **FR.21.21 / FR.21.22 / UC.21.11** — With the group unmuted and the default `AnswersOnly` mode: run a desktop turn that makes several tool calls and confirm the topic receives exactly one message (the final answer) when the turn finishes, with no sound; stop a turn mid-way and confirm nothing is posted; when a session hits its usage limit, confirm the limit text appears in its topic once, without sound; send `/sessions` and confirm the reply makes no sound. Switch to `Full` and confirm every entry is posted, still silently; switch to `Off` and confirm nothing is mirrored while `notify_user` still rings.
10. **FR.21.5 / FR.21.24 / FR.22 / UC.21.1 / UC.21.12** — Have a supervisor send two workers `async` work and arm a short self-wake, with the session not selected in the tree. Confirm that when the delivery and wake turns finish there is no `✅ finished` ping, no desktop toast and no completion sound, and that the supervisor's topic gets one silent answer per turn. Then have it reach a point where the User must decide and confirm it calls `notify_user` once: Telegram rings, and a Windows toast titled `{directory label} / {session name}` shows the same message; clicking the toast selects the session. Untick "Show a desktop notification when a session asks for you" in Settings and confirm the next `notify_user` reaches Telegram only.
