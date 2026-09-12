# ISSUE.009 — JSONL-Watcher Write-Back Races the .txt Watcher, Duplicating Entries

**Status:** Resolved (2026-09-12)
**Affects:** Any session whose turn is driven by something other than this session's own `TurnController.SendAsync` — most visibly Telegram Bridge (FR.21) turns, but also scheduled self-wake turns and any other daemon-orchestrated (`IDaemonClient.RunTurnAsync`) turn. Most likely to manifest under UI-thread load (e.g. immediately after an app or machine restart, or with many sessions active at once).

## Symptom

The session output pane occasionally shows the same user message and/or the same assistant reply twice in a row, back to back. First reported on a Telegram-bridged session right after the host machine was restarted (heavy startup activity: Telegram bridge resuming, several tool calls in flight).

## Investigation

`SessionContentViewModel` displays a session's `.txt` file, but watches **two** files with independent `FileSystemWatcher`s and independent debounce timers:

- `_fileWatcher` (500ms debounce) → `RefreshFromFile()`, which detects new entries by comparing the `.txt` file's entry count against the live `Messages.Count`.
- `_jsonlWatcher` (1000ms debounce) → `RefreshFromJsonl()`, which detects new entries in Claude CLI's JSONL against `_lastKnownEntryCount`, and — because the JSONL is not itself persisted as the session's history — **also writes those same entries into the `.txt` file** so they survive.

Both watchers are only suppressed while `_isBusyProvider()` (`TurnController.IsBusy`) is true. That flag is set for the whole duration of a turn sent through *this* session's own `TurnController.SendAsync` — but a turn started elsewhere (Telegram Bridge's `RouteToSessionAsync` → `IDaemonClient.RunTurnAsync`, or a scheduled self-wake) never sets this session's local `TurnController.IsBusy`. For those turns, both watchers are live for the entire run.

Root cause, confirmed against the pre-fix code (`SessionContentViewModel.RefreshFromJsonl`):

```csharp
var newEntries = displayable.Skip(_lastKnownEntryCount).ToList();
foreach (var entry in newEntries)
    _fileService.AppendMessage(_node.FileName, entry.Role, entry.Content);   // (1) synchronous .txt write

_lastKnownEntryCount = displayable.Count;

Dispatcher.UIThread.Post(() =>
{
    foreach (var entry in newEntries)
        Messages.Add(EntryToViewModel(entry));                              // (2) UI update, only queued
    ...
});
```

Step (1)'s `.txt` write fires `_fileWatcher.Changed` immediately, which schedules `RefreshFromFile()` 500ms later. Step (2) is only *queued* onto the UI thread at that point, not executed — under normal load it runs within milliseconds, safely ahead of the 500ms window, so no duplicate appears. But if the UI thread is busy for longer than that (the reported case: right after a restart, with the Telegram bridge resuming and several tool calls queued), `RefreshFromFile()` can run *before* step (2)'s `Messages.Add` has executed. `RefreshFromFile()` sees `Messages.Count` not yet incremented, treats the same entries as new, and adds them — and then the original queued `Messages.Add` from step (2) runs afterward and adds them *again*. Net effect: every entry `RefreshFromJsonl` picked up gets duplicated in the pane.

## Root Cause

`RefreshFromJsonl` wrote its new entries to the `.txt` file *before* those same entries were reflected in the in-memory `Messages` collection, leaving a window in which the sibling `.txt` `FileSystemWatcher` could independently detect and re-add the exact same entries. This race was latent even before Telegram Bridge (any genuinely-external CLI run already exercised both watchers), but became load-bearing once daemon-orchestrated turns (FR.21 Telegram, scheduled wakes) started running for extended periods with this session's local `IsBusy` never set, keeping both watchers armed for the turn's full duration instead of just the tail end.

## Resolution

`SessionContentViewModel.RefreshFromJsonl` (`code/TessynDesktop/ViewModels/SessionContentViewModel.cs`) now performs the `.txt` write-back **inside** the same `Dispatcher.UIThread.Post` callback, immediately after the `Messages.Add` loop — not before it. Both mutations now happen back-to-back on the UI thread with no async gap between them, so by the time the `.txt` watcher's write-triggered event can fire and its 500ms debounce elapse, `Messages.Count` already reflects the addition and `RefreshFromFile()` correctly finds nothing new.

## Prevention

No dedicated automated regression test was added — `SessionContentViewModel` composes live `FileSystemWatcher`s, background `Timer` debounces, and the Avalonia `Dispatcher`, which is impractical to drive deterministically in a unit test. The fix was verified by code inspection of the corrected ordering and a full solution build (`dotnet build code/TessynDesktop.sln`).

## Known residual gaps (accepted, not blocking)

- The underlying two-watcher-for-one-file-graph design remains: `RefreshFromFile`'s new-entry detection is still count-based against `Messages.Count` rather than content/identity-based. This fix closes the specific race that caused the reported duplication; a more thorough rework (e.g. a single source of truth for "entries already displayed") was judged out of scope for this fix.
