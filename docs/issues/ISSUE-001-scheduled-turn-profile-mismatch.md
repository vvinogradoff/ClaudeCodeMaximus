# ISSUE-001: Scheduled turns fail with 401 due to profile-resolution mismatch

## Symptom

A session works fine when the user interacts with it directly, but when a scheduled
wakeup fires for that same session (via `SchedulerService` -> `SessionTurnService`),
the turn immediately fails with:

```
Failed to authenticate. API Error: 401 Invalid authentication credentials
```

Observed 2026-07-14 on session `f90901b092ec4b6a975e1b5fc88e762d`
("HearmemanAI WAN22 SVI 4-pass", directory `C:\Projects\Personal\fanvue.one`),
immediately after MCP-scheduled resumes fired at 17:51 and 03:00.

## Root Cause

`ClaudeProcessManager.TryStartProcess` (`Services/ClaudeProcessManager.cs:296-297`)
sets `CLAUDE_CONFIG_DIR` from a `profileConfigDir` string, which selects which
account/profile's credential store the `claude` CLI process uses. There is no other
auth mechanism in the app — a wrong `profileConfigDir` means the CLI authenticates as
the wrong (or logged-out) account, producing a 401.

The two call paths resolve `profileConfigDir` from different sources of truth:

- **Interactive path**: `SessionViewModel.RestoreLastUsedSettings()`
  (`ViewModels/SessionViewModel.cs:2069-2100`), called from
  `MainWindowViewModel.OnSelectedSessionChanged` (`ViewModels/MainWindowViewModel.cs:445`)
  every time a session tab is opened. It scans **that session's own** message-history
  file for the last recorded `ProfileName` and assigns `_selectedProfileIndex` directly
  to the backing field (bypassing the `SelectedProfileIndex` property setter), so it is
  never written back to `AppSettings`.

- **Scheduled path**: `SessionTurnService.ResolveProfileConfigDir`
  (`Services/SessionTurnService.cs:262-268`) instead reads the **shared, persisted,
  per-directory** setting `directoryModel.SelectedProfileIndex`
  (`ViewModels/SessionViewModel.cs:424-441`), which only changes when the user
  explicitly picks a profile from the directory-level dropdown, and defaults to `0`
  (Default profile) otherwise.

**Divergence:** if a session's actual profile (restored per-session from its own
history) differs from the shared per-directory `SelectedProfileIndex` (which may still
be `0`/Default, or set by a different session under the same directory), interactive
turns use the correct account while scheduled turns launch `claude` against the wrong
one's `CLAUDE_CONFIG_DIR` — hence the 401 only on the scheduled path.

## Introduced By

Commit `5b9b5d2` (2026-07-13, "Restore last-used profile, model and effort on session
selection"). That change intentionally decoupled in-memory session-level profile
selection from the persisted directory-level setting so that switching between
sessions shows each session's own history rather than the shared per-directory value —
but `SessionTurnService` (used by the scheduler) was not updated to match, and still
consults the old shared per-directory field.

## Options for Fix (needs Architect decision before implementing)

1. Make `SessionTurnService.ResolveProfileConfigDir` resolve the profile the same way
   `RestoreLastUsedSettings()` does — scan the session's own history for the last
   recorded `ProfileName` — so scheduled and interactive turns agree.
2. Persist the per-session profile choice somewhere both paths read (e.g. on
   `SessionNodeModel` itself) instead of inferring it from history scans, and have both
   paths read that.
3. Revert to a single shared per-directory profile (simpler, but reintroduces the UX
   regression commit `5b9b5d2` was fixing).

## Status

Open — root cause identified, no fix implemented yet.
