# Telegram-Bridge_Topic-Name-Reconciliation

*Backs [FR.21.18](/docs/requirements.md) (and FR.21.1/FR.21.2). Use case UC.21.8, test cases TC.21.9–TC.21.10.*

## Why reconciliation exists

When directory labels became short ([Session-Tree_Directory-Label-derivation](/docs/business-analysis/Session-Tree_Directory-Label-derivation.md)), topics created earlier still carried the old names (`C:\Projects\oDesk\HardStop / …`). The Telegram Bot API cannot read a topic's current name back, so Tessyn cannot detect drift by asking Telegram. Instead it persists the **last-applied topic name** per session — `AppSettingsModel.TelegramTopicNamesByNodeId` (`code/TessynDesktop.Core/Models/AppSettingsModel.cs:152`, NodeId → untruncated name) — and compares it with the current name. This also keeps topics in sync with session renames on every later start. No database change; the field is additive JSON.

## Flow

`TelegramBridgeService.Start()` launches a background run (`TelegramBridgeService.cs:97-102`) that never blocks `Start()` or polling. `ReconcileTopicNamesAsync` (`:154`):

1. Do nothing if `TelegramChatId == 0` (`:156`).
2. Snapshot `TelegramTopicThreadIdsByNodeId` under `SyncRoot`.
3. Per topic: orphan (no session) → skip, mapping untouched; `desired = BuildTopicName(node)` (`:149`); equal to the stored name → skip with no call; otherwise `ITelegramClient.EditForumTopicAsync` with one 429 retry (`EditTopicWithRetryAsync`, `:242`).
4. Success (renamed or not modified) records `desired` and counts toward the save batch.

## Outcomes

| Outcome | Handling |
|---|---|
| Renamed | Record name; wait 6000 ms |
| `TOPIC_NOT_MODIFIED` (returns `false`) | Record name; wait 1000 ms |
| 429 | Wait `retry_after` clamped to 1–300 s (1 s if missing), retry same topic once; second failure stops the run |
| Rights (401/403, "not enough rights") or "chat not found" | One warning, stop run (`IsChatLevelError`, `:258`) |
| Other API error (e.g. `TOPIC_ID_INVALID`, deleted topic) | Warning, skip topic, wait 1000 ms, continue |
| Network failure / HTTP timeout | Logged, stop run; next start retries |
| Cancellation by bridge stop | Quiet exit |

## Throttling rationale

Every real rename posts a "topic renamed" service message into the group. Telegram's group flood limit is about 20 messages/minute; the normal bridge traffic (mirroring, pings) does not retry on 429. A 6 s pause after a real rename (`Constants.Telegram.TopicRenameIntervalMs`, `Constants.cs:525`) uses at most ~10 messages/minute and leaves half the budget for normal traffic. No-op and skipped-error results post no service message, so 1 s (`TopicRenameNoOpIntervalMs`, `:528`) is enough to avoid hammering the API.

## Run chaining, persistence, test seams

- **Runs never overlap.** `Start()` chains the new run after the previous `ReconciliationTask` (`:97-102`), so a Start → Stop → Start waits for the earlier run including its final save. The task never faults.
- **Persistence cadence.** Settings are saved every `TopicRenameSaveBatchSize` (20, `Constants.cs:531`) recorded names and once more in `finally` (including stop/cancel), under `SyncRoot`. The name is also recorded at topic creation (`:133`).
- **Test seams** (public settable members, no constructor change): `DelayAsync` (`:53`, all throttle and 429 waits), `Logger` (`:47`), `TopicRenameIntervalMs` (`:56`), `TopicRenameNoOpIntervalMs` (`:59`), and `ReconciliationTask` (`:62`, awaited by tests). Tests: `TelegramBridgeServiceReconciliationTests.cs`.
- No code path currently removes thread-id mappings; a future removal must also remove the `TelegramTopicNamesByNodeId` entry.

## One-time migration estimate (Architect's machine, at time of writing)

687 mapped topics, 686 live sessions (1 orphan). About 403 names change (348 HardStop, 42 fanvue.one, 6 TelegramAnalyzer, …). The first run is a background job of roughly 45 minutes (403 × ~6 s); afterwards only differing names (e.g. renamed sessions) are edited per start.

## Operational requirement

`editForumTopic` works only if the bot is the topic's creator or has the **Manage Topics** admin right. Topics were created by the bot, and Manage Topics is already required for `createForumTopic` (FR.21 setup step 3). Without it the run stops on the first rights error with one warning and retries on the next start.
