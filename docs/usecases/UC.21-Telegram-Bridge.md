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

**Trigger:** A session calls `mcp__cmx__notify_user` with a message, or a scheduled/orchestrated turn completes.

1. The Telegram Bridge ensures a topic exists for the session (creating one if this is the session's first activity since the bridge went live).
2. The message is posted into that topic.
3. The User sees the notification on their phone/desktop Telegram client, labeled with the session's topic name (`{directory label} / {session name}`).

**Outcome:** The User is informed without needing the Tessyn window open or focused.

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
4. The assistant's response (or an error) is posted back into the same topic.

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

---

## Out of scope for this use case set

- Blocking "ask and wait for reply" behavior (see FR.21.3 rationale) — UC.21.2 is fire-and-forget by design.
- Closing/archiving a session's topic when the session is deleted or hidden.
