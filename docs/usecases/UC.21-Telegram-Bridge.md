# UC.21 — Telegram Notification Bridge

Related requirement: [FR.21 — Telegram Notification Bridge](/docs/requirements.md#fr21--telegram-notification-bridge)

## Actors

- **User** — the human supervisor, away from the desktop or at it, reachable via a Telegram bot.
- **Session** — a running Claude Code turn inside a Tessyn session node.
- **Telegram Bridge** — `ITelegramBridgeService`, the component mediating between sessions and the Telegram Bot API.

## Preconditions

- `TelegramEnabled` is true, a valid bot token is stored, `TelegramChatId` points at a Topics-enabled supergroup the bot administers, and the User's Telegram user ID is in `TelegramAllowedUserIds`.

---

## UC.21.1 — Session notifies the user

**Trigger:** A session calls `mcp__cmx__notify_user` with a message. Nothing else triggers a notification — in particular, a turn finishing (scheduled, orchestrated, Mailbox Delivery or interactive) is never announced (FR.21.5, ISSUE.012). A Worker Session's call reaches neither channel (FR.21.19).

1. The Telegram Bridge ensures a topic exists for the session (creating one if this is the session's first activity since the bridge went live).
2. The message is posted into that topic, prefixed with the mention handle and sent with sound — the only bridge message that makes a sound (FR.21.21).
3. The User sees the notification on their phone/desktop Telegram client, labeled with the session's topic name (`{directory label} / {session name}`, where the directory label is the short label of FR.1.3, e.g. `HardStop / Fix login`).
4. At the same time, if desktop notifications are enabled, a Windows toast shows the same message under the same name (FR.21.5); clicking it selects the session in Tessyn.

**Outcome:** The User is informed without needing the Tessyn window open or focused — and when the phone rings or a toast appears, a session actually needs them.

## UC.21.2 — Session asks a plain-text question

**Trigger:** A session needs input mid-task and calls `notify_user` with a question, then ends its turn.

1. Same posting flow as UC.21.1 — the question lands in the session's topic.
2. The turn completes (it does not block waiting for a reply — FR.21.3).
3. The User replies in that topic whenever they are able to.
4. The reply is routed back (UC.21.3) as an ordinary new prompt to the same session, continuing the conversation with the User's answer as context.

**Outcome:** The User can answer on their own schedule; a slow or missing reply never leaves a Tessyn turn hung.

## UC.21.3 — User replies to a session from Telegram

**Trigger:** The User sends a text message inside a session's Telegram topic.

1. The Telegram Bridge validates the sender's chat ID and user ID against the configured allow-list; anything else is dropped silently (logged).
2. The topic's `message_thread_id` is looked up in the Topic Map to find the target NodeId.
3. A turn is run against that session (`TurnSource.Telegram`) with the message text as the prompt.
4. The turn's output reaches the same topic through the Universal I/O Mirror, silently — by default only its final answer, including an error such as a usage-limit message (FR.21.21, FR.21.22).

**Outcome:** The User can carry on a conversation with any session entirely from Telegram.

## UC.21.4 — User lists sessions from Telegram

**Trigger:** The User sends `/sessions` to the bot (in any topic, including the group's General topic).

1. The bridge builds the list of sessions that are favorited or already have a Telegram topic.
2. Each line shows the session name, directory, and running state.

**Outcome:** The User can see which sessions are reachable from Telegram and which are currently active, without opening Tessyn.

## UC.21.5 — User reads a session's recent output from Telegram

**Trigger:** The User sends `/open <name or NodeId prefix>`.

1. The bridge resolves the argument to a session (exact/partial NodeId match, or name match).
2. The session's last 20 conversation entries are posted back to the chat.

**Outcome:** The User can review what a session has been doing without stepping into the desktop app — including a session that has no topic yet (this command works standalone).

## UC.21.6 — Unauthorized sender

**Trigger:** A message arrives from a chat ID or user ID not on the allow-list.

1. The bridge logs a warning and drops the message with no reply.

**Outcome:** The bot never responds to, or is discoverable as functional by, anyone outside the configured allow-list.

## UC.21.7 — User sends files from Telegram

**Trigger:** The User sends a photo, document, video, audio, voice note, video note, animation, or an album of these (optionally with a caption) inside a session's Telegram topic.

1. The bridge acknowledges the update, validates sender/chat as in UC.21.3, and (for an album) waits for the rest of the group (FR.21.16).
2. Each attachment is downloaded to `%AppData%\TessynDesktop\telegram-inbox\yyyy-MM-dd\` (files over 20 MB or failed downloads become a note line instead).
3. A turn runs with the caption/text followed by one `[attachment: <absolute path>]` line per file; the session can upload/read the files by path.

**Outcome:** The User can attach files to a session from Telegram. Unsupported updates never cause the bridge to re-poll in a tight loop.

## UC.21.8 — Existing topics are renamed at bridge start

**Trigger:** The bridge starts (app launch, or the bridge is enabled in Settings) while topics already exist whose names no longer match the current naming (e.g. topics created with full-path labels before FR.1.3, or sessions renamed since).

1. `Start()` returns immediately; a background reconciliation begins (FR.21.18) — only if a chat ID is configured, and never overlapping a previous run.
2. For each mapped topic whose session still exists, the current name (`{directory label} / {session name}`) is compared with the last-applied name; equal names are skipped with no Telegram call.
3. Differing names are applied with `editForumTopic`, one at a time, paced (6 s after a real rename, 1 s after a no-op) to stay within Telegram's group flood limit; the applied name is recorded and progress is saved periodically.
4. On a rate-limit (429) the run waits (1–300 s) and retries once; on a rights error, a "chat not found" error, a network failure or an HTTP timeout it stops and the next start retries; a deleted topic is skipped (after a short pause).

**Outcome:** The User's topic list gradually shows the short names (e.g. `HardStop / <session>`) without any manual action; bridge messaging works normally throughout, and later starts only touch topics whose session was renamed.

## UC.21.9 — Worker Sessions never reach Telegram

**Trigger:** The User's session (plain, or acting as a Supervisor Session) spawns one or more Worker Sessions via `spawn_session`, `orchestrate_parallel`, or `orchestrate_pipeline` — e.g. Opus fanning out 40 subagents for a large task.

1. Each Worker Session runs its own turns normally, but `OrchestrationDepth > 0` on every one of them means the bridge excludes them outright (FR.21.19): no topic is ever created for a worker, none of its USER/ASSISTANT/SYSTEM entries are mirrored, and an explicit `notify_user` call made by a worker produces no Telegram message at all.
2. `/sessions` never lists a Worker Session, favorited or not.
3. The depth-0 session that actually started the orchestration behaves exactly as any other session — its own topic gets the Supervisor's answers (or, in Full mode, its prompts and responses too) and any of its own `notify_user` pings (UC.21.12).

**Outcome:** A single topic represents the work the User actually started, regardless of how many workers it fans out to internally — the group never gets cluttered with one topic per subagent.

## UC.21.10 — Pre-existing Worker Session topics are cleaned up automatically

**Trigger:** The bridge starts while a Worker Session topic already exists in `TelegramTopicThreadIdsByNodeId` — created before FR.21.19 existed, or in case the depth-0 exclusion is ever bypassed some other way.

1. `Start()` runs a cleanup pass — chained before topic name reconciliation (UC.21.8) in the same background task, so a worker topic is never renamed moments before being deleted.
2. For every mapped topic whose session now resolves to `OrchestrationDepth > 0`, the bridge calls `deleteForumTopic` and removes both the thread-id and last-applied-name mappings (and the in-memory reverse lookup).
3. A topic already gone on Telegram's side (`TOPIC_ID_INVALID`) counts as success — the mapping is still cleaned up. Any other delete failure leaves the mapping in place for a retry on the next start. An orphaned mapping (session no longer found at all) is left untouched, same as topic rename reconciliation — there's no way to tell whether a deleted node was a worker.

**Outcome:** Worker Session topics created before the exclusion existed disappear from the group automatically the next time the app starts, with no manual cleanup needed.

## UC.21.11 — User follows sessions in Telegram without being disturbed

**Trigger:** Sessions run turns — from the desktop, from Telegram, or headless — while the User keeps the Telegram group unmuted.

1. Each turn's output is mirrored according to the Mirror Mode (FR.21.22). In the default `AnswersOnly` mode, the bridge holds the turn's latest assistant text and posts it once, when the turn completes; the narration a turn emits between tool calls, the User's own prompts and system lines are not posted. A stopped turn posts nothing. A turn that ends in an error also posts the error text exactly as reported — so when a session hits its usage limit, the topic shows e.g. `You've hit your session limit · resets 1:50pm`, once.
2. Every mirrored message, and every reply to `/sessions`, `/open` and `/stop`, is sent silently (FR.21.21): it appears in the topic and the unread count, but the phone makes no sound.
3. In `Full` mode every entry is mirrored as before, still silently, except that a Mailbox Delivery is shown as a single `⚙️` line instead of the workers' full results (FR.21.23). In `Off` mode nothing is mirrored.
4. A `notify_user` message (UC.21.1) is the only one that makes a sound.

**Outcome:** Topics stay readable — one answer per turn by default — and the User's phone only makes a sound when a session actually needs them.

## UC.21.12 — A long orchestration reaches the User only when they are needed

**Trigger:** A Supervisor Session fans work out with `send_to_session` in `async` mode and/or arms self-wakes, while the User is away from the desktop.

1. Each worker result reaches the supervisor as a Mailbox Delivery; results that arrive while the supervisor is busy are merged into one delivery turn (UC.15.1). Self-wakes fire as scheduled turns.
2. None of these turns announces its completion — no Telegram ping, no desktop toast (FR.21.5) and no completion sound (FR.22).
3. Every scheduled turn carries a hidden reminder (FR.21.24) that the User may not be watching: the supervisor calls `notify_user` once if — and only if — its reply needs the User to decide or act (UC.21.1, UC.21.2), and otherwise stays silent.
4. In `AnswersOnly` mode the supervisor's topic receives one silent answer per delivery or wake turn (UC.21.11).

**Outcome:** An orchestration with many workers rings the User's phone only at the moments it actually needs them (on 2026-10-28 that would have been about 5–6 pings for the day instead of 24, ISSUE.012).

---

## Out of scope for this use case set

- Blocking "ask and wait for reply" behavior (see FR.21.3 rationale) — UC.21.2 is fire-and-forget by design.
- Closing/archiving a session's topic when the session is deleted or hidden.
- Rate limits, cooldowns, quiet hours or presence-aware suppression for `notify_user`, and redaction of secrets in mirrored text — declined in ISSUE.012.
