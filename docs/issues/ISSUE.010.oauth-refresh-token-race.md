# ISSUE.010: Concurrent `claude` processes sharing a profile race OAuth token refresh

## Symptom

A turn fails out of nowhere — no prior warning, no obviously wrong profile — with the
`claude` CLI's own error text:

```
Failed to authenticate: OAuth session expired and could not be refreshed
```

The account is not actually revoked: re-authenticating that profile fixes it
immediately, and other sessions on the same account keep working the whole time.
Observed 2026-09-22 12:26:18 on session `92341fe9` (FanvueChat project), ~35s after
another session (`361b477e`, HardStop project) had spawned its own `claude` process.

## Root Cause

`Failed to authenticate: OAuth session expired and could not be refreshed` is emitted
by the `claude` CLI subprocess itself (visible verbatim in its `stream-json` output) —
it means the CLI's own internal OAuth refresh attempt failed, not that TessynDesktop
generated the message.

Auth is entirely delegated to the CLI via `CLAUDE_CONFIG_DIR`
(`ClaudeProcessLauncher.TryStartProcess`), which selects which credential store
(`.credentials.json`) a given `claude` process reads/writes. Nothing in
`ClaudeProcessLauncher`, `ClaudeProcessManager`, or `ClaudeStreamingSessionManager`
serialized *launching* two processes against the same config dir — only
`SessionTurnService`'s per-node `SemaphoreSlim` (`GetTurnLock`) prevented concurrent
`--resume` on the *same session*; two different sessions sharing one profile could
launch `claude` processes at the same instant with no coordination at all.

The "Default" profile (`profileConfigDir` null/empty — no `CLAUDE_CONFIG_DIR` override)
is the highest-risk case: every session whose profile resolves to Default shares the
literal `~/.claude` credential store, not a TessynDesktop-scoped directory, so its
collision domain is the whole machine (any other TessynDesktop session on Default, a
manual terminal `claude` invocation, etc.) — confirmed on the failing session above
(`ConfigDir=null` in its spawn log line). Orchestrated children (ISSUE.002) make the
same-profile case common even for named profiles, since a supervisor's children all
inherit its profile and can run fully in parallel.

OAuth refresh tokens are rotated on use: if two `claude` processes sharing a config dir
both decide their access token is near-expiry and both refresh around the same moment,
whichever request lands second presents an already-invalidated refresh token — the CLI
reports it as an unrecoverable auth failure for that turn, even though the account and
the *other* process's (now-current) token are both fine.

## Fix (implemented)

Two complementary mitigations, since the actual refresh happens opaquely inside the CLI
and can't be intercepted directly:

1. **Serialize the spawn moment per config dir** (`ClaudeProcessLauncher.Start`) — a
   lock keyed by `profileConfigDir` (a shared sentinel key for Default/null) wraps only
   the `Process.Start` call, not the process's subsequent lifetime. Two turns sharing a
   profile can no longer have their `claude` launches — where the CLI checks/refreshes
   its token at startup — interleave. Turns already running under a shared profile
   still execute fully in parallel; only the launch instant is serialized. This is the
   single chokepoint shared by One-shot Mode, Streaming Session Mode, and
   `RunPrintModeAsync`, so all three are covered by one change.
2. **Treat the error as retryable, not fatal** — `Constants.Auth.OAuthRefreshRaceMarker`
   identifies the exact CLI error text. `TurnController` (interactive one-shot and
   Streaming Session Mode) and `SessionTurnService` (headless one-shot and the
   streaming-redirect path, FR.19.13) each detect it on the `result`/`system` error
   event, suppress the file write and "Re-authenticate" prompt for that event, wait
   `Constants.Auth.OAuthRefreshRaceRetryDelayMs` (1.5s, to let the winning process finish
   writing its refreshed credentials), and silently retry the same turn exactly once.
   A second failure with the same marker is *not* retried again and surfaces normally —
   this is a mitigation for a transient race, not a masking of a genuinely revoked
   account.

Mitigation 2 is the primary fix (it directly addresses the user-visible symptom
regardless of *when* in a process's lifetime the race happens); mitigation 1 reduces
how often the race gets a chance to occur in the first place, particularly for bursts
of near-simultaneous launches (e.g. `orchestrate_parallel` fanning out several children
that inherited the same profile).

## Notes / Limitations

- The retry only covers the moment TessynDesktop can observe: a `claude` process's
  `result`/`system` error event. If a Streaming Session Mode process's *later* turns
  hit this after the process has been alive for a while, each turn gets its own
  independent one-retry attempt — there's no cross-turn backoff or circuit breaker.
- The per-config-dir spawn lock only narrows the launch-time collision window; it does
  not prevent a race between two processes that are already running and each
  independently decide, mid-turn, that their token needs refreshing. The retry (not the
  lock) is what actually resolves that case.
- Only the CLI's exact reported text is recognized as this transient case
  (`Constants.Auth.OAuthRefreshRaceMarker`). Any other auth error text still goes
  through the existing `Constants.Auth.InvalidCredentialsMarker` handling (FR.12.16)
  unchanged.

## Status

Fixed — build clean, 373/373 tests pass (2 new tests cover the retry-and-succeed path
for both the one-shot and streaming-redirect branches of `SessionTurnService`). No unit
test was added for the `ClaudeProcessLauncher` spawn lock itself — that class has no
existing test coverage (it spawns real OS processes) and adding it would require
refactoring scope beyond this fix.
