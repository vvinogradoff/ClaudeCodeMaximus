# ISSUE.012 — Notification noise: automatic completion pings and an audible conversation mirror

**Status:** Open — requirements drafted (FR.21.5 changed; FR.21.21–FR.21.24 and FR.15.12 added; FR.14.9, FR.15.3, FR.15.11, FR.21.1/21.3/21.4/21.7/21.9/21.10 and FR.22 amended), awaiting Architect review. Code not started.
**Affects:** Telegram Bridge (FR.21), the desktop toast (FR.21.5), the async orchestration mailbox (FR.15.3), scheduled turns (FR.14).
**Reported:** 2026-10-28 — the Architect's phone "rings like every minute"; sessions "tag me for every move they do". The desktop toast had already been muted for the same reason.

## Symptom

With the Telegram Bridge enabled, the phone notified about once a minute during busy periods: `@harmlessevil ✅ finished [...] — see topic above for output.` pings plus a stream of short mirror messages. Muting the Telegram group did not help, because the pings @-mention the user.

## Measurements (2026-10-28, 00:00 – ~14:00 local, UTC+8)

Reconstructed from session files, Claude JSONL transcripts and the app log (successful Telegram sends are not logged). Per-ping verdicts came from a Sonnet reviewer session and the mirror breakdown from a Haiku reviewer session (both under Tessyn → "Notification review"); the counts were re-checked with scripts.

| Source | Messages | Mentions the user? | Verdict |
|---|---|---|---|
| FR.21.5 completion ping after a scheduled turn | 22 — 18 async worker results delivered to a supervisor, 4 self-wakes | yes | 18 noise, 2 needed, 2 worth knowing |
| `notify_user` (FR.21.3) | 2 | yes | both needed, but each was followed within seconds by a completion ping for the same event |
| Universal I/O Mirror (FR.21.9) | 209 | no, but sent with sound | 87 narration lines between tool calls, 34 pasted worker results, 21 echoes of desktop prompts, 49 final answers, 18 system / wake lines |

- Bursts: 6 tagged pings in 9 minutes from one supervisor whose workers finished one after another (13:37–13:46), each saying "finished" while the orchestration was still mid-way; 4 tagged pings in 8 minutes whose turns only said "You've hit your session limit"; 13 mirror messages in 4 minutes of deploy narration.
- About 5–6 pings would have covered every moment the Architect was actually needed that day.
- Not causes: `notify_user` volume (10–16 a day on Oct 24–26, 2 on Oct 28 — FR.21.20's guidance works); Worker Sessions (218 worker entries correctly not mirrored, FR.21.19); Claude Code's native `PushNotification` tool (never called).

## Root cause

1. **Orchestration plumbing is reported as a user event.** An async worker's result reaches its supervisor as a delay-0 schedule (FR.15.3, `AgentMcpServer.SendToSessionAsync`), and every completed scheduled turn calls `INotificationService.ShowResult` (`SchedulerService.FireScheduleAsync`), which `CompositeNotificationService` turns into a mention-prefixed `✅ finished` Telegram ping plus a desktop toast. A supervisor with N workers rings the user N+ times, and "finished" only means that one scheduled turn ended.
2. **The mention gets through a muted group** (FR.21.10), so the only way to silence the pings was to silence the whole bridge, including the pings that mattered.
3. **The mirror is audible and verbose.** `TelegramClient.SendMessageAsync` never sets `disable_notification`, and the mirror posts every ASSISTANT entry (each text block a turn emits between tool calls), every worker result pasted into the supervisor's prompt, and every desktop prompt.
4. **Results that queue behind a busy supervisor each get their own turn** (each due schedule waits on the turn lock separately, FR.14.9), multiplying supervisor turns, pings and tokens.

## Previous attempts

- **FR.21.19** — Worker Sessions excluded from the bridge entirely. Correct, but the pings come from the depth-0 supervisor.
- **FR.21.20** — `notify_user` restraint guidance. Cut `notify_user` volume sharply, but `notify_user` was not the main source.

## Resolution (drafted, pending Architect review)

Architect decisions, 2026-10-28:
- Only `notify_user` may make a sound or mention the user. Turn completion is never announced — no Telegram ping and no desktop toast (FR.21.5).
- The desktop toast becomes a useful signal again: it shows exactly the `notify_user` messages that ring in Telegram, titled like the session's topic, and clicking it opens the session (FR.21.5). Its `NotificationsEnabled` setting stays.
- The per-session Session Completion Sound (FR.22) stays the only completion signal, and only for turns sent from the desktop — turns started by the scheduler, orchestration or Telegram finish without a sound, by design.
- Everything else the bridge posts is silent (FR.21.21).
- New mirror mode setting, default "answers only": the final assistant message of each turn (FR.21.22). In Full mode a worker-result delivery is shown as one line (FR.21.23).
- Usage-limit messages (and any other error a turn ends with) must stay visible in the topic as text, worded as the CLI reports them — no notification needed. In "answers only" mode the turn's error text is posted after its answer, once (FR.21.22).
- Scheduled turns carry a hidden per-turn reminder to call `notify_user` once, and only when the user must act (FR.21.24): on 2026-10-28, 2 of the 4 moments the user was needed reached them only through the completion ping that is now removed. The `cmx-orchestration` skill's supervisor guidance is tightened accordingly (FR.15.11).
- Worker results that queue up while the supervisor is busy are merged into one delivery turn (FR.15.12).
- Declined: rate limits or cooldowns on `notify_user`, quiet hours, presence-aware suppression (quiet hours are handled in the phone's notification settings), and redacting secrets from mirrored text (anything agents touch is treated as exposed).

## Tests

TC.21.2 additions, TC.21.13–TC.21.17, TC.15.1 (drafted).
