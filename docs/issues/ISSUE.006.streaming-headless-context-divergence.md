# ISSUE.006 — Streaming Headless Context Divergence

**Status:** Resolved (FR.19.13, 2026-08-26)
**Affects:** Any session in Streaming Session Mode (FR.19) that also receives a headless turn — Telegram-routed (FR.21.4), Scheduled (FR.14), or Orchestrated (FR.15).

## Symptom

Reported by the Architect against the "LoRA training of LLM" session (`WorkingDirectory: C:\Projects\Personal\FanvueChat\`, `IsStreamingMode: true`):

> I was sending "Yes please proceed but dont download 26 moe gemma. And let's also download the decoding assistant" from Telegram and my session here in Tessyn correctly shows this my phrase and model's response, but then as I asked "any news?" it looked like model doesn't have my phrase and its own response in the session.

The Tessyn session **file** was correct throughout — both the Telegram-origin exchange and the follow-up were present, in order, with plausible timestamps. The problem was that **Claude's own next answer acted like it had no memory of the Telegram exchange**, even though the transcript on disk clearly had it.

## Investigation

Initial hypothesis (wrong): the Telegram bridge itself was failing to write to the session file. Ruled out first —
- The session file's `[2026-08-26T03:13:43Z] USER` entry (no `profile=` attribute — the fingerprint of a headless-origin write, since only the interactive desktop path stamps a profile name) matched the Architect's Telegram message exactly, followed by three `ASSISTANT` entries through `03:16:05Z`.
- The desktop's `SessionContentViewModel` file watcher logged `FileWatcher: 1 new entries detected in <file>` four times in that exact window — the live UI refresh worked too.
- Across every session file modified that day, this was the *only* headless (no-`profile=`) entry anywhere — no routing errors, no "unauthorized"/wrong-chat drops, only one Tessyn process running. The bridge mechanism (post the FR.21.9–21.11 rework) was working exactly as designed.

Second pass, after the Architect pointed at the actual confusion (quoting a later assistant message: *"I never said that — in this conversation, my last message was asking you to confirm the plan... nothing has been kicked off here"*): the model's response to a later prompt showed zero awareness of the Telegram exchange that undisputedly happened earlier in the same file. That reframed the bug from "not written" to "written correctly, but the model's own context diverged from it."

Root cause traced through the log:
1. `2026-08-26 03:13:43Z` — the Telegram message triggers `SessionTurnService.RunTurnAsync` (headless). Per the FR.19.11 design at the time, headless turns **always** ran via One-shot Mode regardless of the target session's Streaming Session Mode setting — confirmed via `ClaudeProcessManager: Attempting to spawn claude ... --resume 429edca5-... ` in the log, a brand-new one-shot process.
2. That one-shot process resumes the session from its on-disk JSONL, appends the new turn, and exits (`Claude process exited. Code=0`). The JSONL on disk is now up to date.
3. Separately, a **Persistent Session Process** (`ClaudeStreamingSessionManager`) was already alive for the same `NodeId`/`ClaudeSessionId` — spawned earlier by an interactive desktop send once the Architect turned Streaming Session Mode on. That process holds its own in-memory conversation state, established at spawn time and extended only by turns written to its own open stdin.
4. The persistent process has **no way to detect** that a separate, external one-shot process just read and extended the same on-disk JSONL. Its own in-memory state silently fell one exchange behind.
5. The next turn sent to that same session (per the file, `[2026-08-26T05:08:51Z] USER profile="anna@vvinogradoff.com"` — "any news?", sent from the desktop and therefore routed to the *already-alive* persistent process) got answered from the **stale** in-memory context — hence "Nothing's running yet... my last message was asking you to confirm the plan," with no memory of the "Yes please proceed" exchange two hours earlier.

This was a real, if narrow, architectural gap that FR.19.11 had explicitly flagged and deferred at the time ("a known, undocumented-by-Claude-Code interaction... accepted v1 limitation") but had not yet been confirmed as an actual user-facing failure, nor connected specifically to the Telegram bridge.

## Resolution (FR.19.13)

`SessionTurnService.RunTurnAsync` now checks `IClaudeStreamingSessionManager.IsAlive(nodeId)` before falling back to spawning a one-shot `--resume` process. If a Persistent Session Process is already alive for the target node, the turn is instead written to that process's own open stdin (`SendTurnAsync`) — the same mechanism the interactive desktop path already uses — so there is only ever one process, and therefore one conversation state, per session at a time.

The tricky part: `SessionTurnService` still needs to return a `TurnResultModel` (assistant text, tokens, error state) to its own caller (Telegram bridge / scheduler / orchestration tools), but the process's single `onEvent` callback is already bound to whichever `TurnController` started and keeps the process alive — overwriting it would silently break that `TurnController`'s own file-append/UI-update logic for all subsequent turns. `IClaudeStreamingSessionManager.SendTurnAsync` gained an optional, additive `onTurnEvent` observer parameter instead: every event for the turn goes to both the existing primary callback (unchanged) and this new one, so `SessionTurnService` can build its return value without ever touching file or UI state itself — that's already handled, correctly, by the primary callback, exactly as if the message had been typed on the desktop.

See `docs/requirements.md` FR.19.11/FR.19.13 for the resulting specification, and `SessionTurnServiceTests.cs` for regression coverage (redirect happens when a process is alive, does not double-append assistant content, propagates errors/tokens correctly).

## Known residual gaps (accepted, not blocking)

- A redirected headless turn cannot change the process's model/effort/MCP config — those are spawn-time-only arguments on an already-running process, same as for interactive sends.
- The session file's assistant/system entries for a redirected turn do not carry `model=`/`effort=` metadata, since that stamping is owned by `TurnController`'s own per-turn pending-fields (UI-layer state) that a headless caller in Core has no access to. Cosmetic only — role, content, and ordering are unaffected.
