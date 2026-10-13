# TC.21 — Telegram Notification Bridge

Related requirement: [FR.21](/docs/requirements.md#fr21--telegram-notification-bridge). Related use cases: [UC.21](/docs/usecases/UC.21-Telegram-Bridge.md).

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

## Manual / E2E verification (requires a real bot + supergroup — cannot run in CI)

Follow FR.21's setup steps to provision a real bot, supergroup, and chat ID first.

1. **UC.21.1** — Enable the bridge in Settings, run any session turn, confirm a new forum topic appears in the supergroup named `{directory label} / {session name}`, and that a scheduled turn's completion posts into it.
2. **UC.21.2 / UC.21.3** — Have a session call `notify_user` with a question; confirm the turn completes without blocking; reply in the topic; confirm the reply becomes the session's next turn and Tessyn's session view shows the exchange.
3. **UC.21.4** — Send `/sessions` from Telegram; cross-check the list against the app's favorites and any sessions already active via the bridge.
4. **UC.21.5** — Send `/open <name>` for a session that has never had a turn since the bridge was enabled; confirm output is returned without a topic being created as a side effect.
5. **UC.21.6** — From a second Telegram account not on the allow-list, message the bot directly; confirm no reply and a warning in the Tessyn log.
6. Restart Tessyn mid-session and confirm `TelegramUpdateOffset` persistence prevents replayed/duplicate command handling for messages sent before the restart.
7. **FR.21.18 / UC.21.8** — With existing topics named with full paths (e.g. `C:\Projects\oDesk\HardStop / ...`), restart Tessyn with the bridge enabled. Confirm the app and bridge respond immediately, and that topics are renamed gradually (about one per 6 s, each leaving a "topic renamed" service message) to `HardStop / <session>`. Restart again and confirm no topics are renamed. Rename a session in the tree, restart, and confirm only that topic is renamed.
