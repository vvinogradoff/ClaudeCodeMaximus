# ISSUE.011: Telegram poll loop re-fetches unsupported updates in a tight loop

## Symptom

After the operator sends a photo (or any non-text update) to the bot and no text message follows, the Telegram Bridge's `getUpdates` calls return immediately instead of long-polling for 30 s — the same update comes back over and over until a later text message arrives. Also, photos/videos/documents never reach a session at all (FR.21.13).

## Root Cause

`TelegramClient.GetUpdatesAsync` skipped every update lacking `message.text` (and non-message updates, and messages without `from`) before building the result list. `TelegramBridgeService.PollLoopAsync` advanced `TelegramUpdateOffset` only over the updates it was handed (`update.UpdateId + 1`), so a skipped trailing update was never acknowledged; Telegram redelivers unacknowledged updates, and since one is always pending, `getUpdates` returns instantly.

## Fix (implemented)

FR.21.12: `GetUpdatesAsync` returns one `TelegramUpdate` for every raw update (non-actionable ones flagged `IsActionable == false`), and the poll loop advances and persists the offset to `max(update_id) + 1` over the whole batch before dispatching only actionable updates. FR.21.13+ add attachment support so photos etc. are now routed instead of dropped.

## Tests

TC.21.6 (`TelegramClientTests`, `TelegramBridgeServiceTests` offset cases).
