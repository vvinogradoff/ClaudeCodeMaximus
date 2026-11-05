# ISSUE.014 — Duplicate USER/ASSISTANT entries in session files (JSONL write-back vs headless turns)

**Status:** Resolved 2026-11-04 (fix built, 669/669 tests pass; not yet observed in the running app).
**Affects:** Session files (FR.3) of sessions that receive headless turns (FR.14 scheduled turns and Mailbox Deliveries, FR.15 orchestration, FR.21 Telegram), most of all top-level sessions in Streaming Session Mode (FR.19). Every consumer of the `.txt` file sees the copies: the output view, `read_session`, compaction transcripts, `BuildContextPreamble` and the Telegram mirror.
**Reported:** 2026-11-04, found while analysing the "HSE-150 Android backend" session for the Focus view (FR.23). Related: [[ISSUE.009]] (same family, two earlier fixes).

## Symptom

The same USER entry, and less often the same ASSISTANT entry, appears twice in a row in the session `.txt` file, usually 1–5 seconds apart. Example from `2026-10-21-1253-bbbmgj.txt` ("HSE-150 Android backend", Streaming Session Mode):

```
[2026-11-04T04:50:12Z] USER
[Scheduled] Worker 'HSE-238 Windows: RC Android address, RC phone app, clash rules' result
[Worker finished] HSE-238 Windows: RC Android address, RC phone app, clash rules
...

[2026-11-04T04:50:14Z] USER
[Scheduled] Worker 'HSE-238 Windows: RC Android address, RC phone app, clash rules' result
[Worker finished] HSE-238 Windows: RC Android address, RC phone app, clash rules
...
```

The two copies are adjacent and identical. Only one assistant response follows, so the turn itself ran once. The extra copy is a second write of the same entry, not a second turn.

## Measurements (session files modified 2026-10-10 – 2026-11-04)

An entry counts as a duplicate when an identical USER or ASSISTANT entry (same role, same body) was written within the previous 10 seconds.

| Sessions | Touched | With duplicates |
|---|---|---|
| Top-level, Streaming Session Mode | 96 | 30 |
| Top-level, One-shot Mode | 23 | 2 |
| Worker Sessions, One-shot Mode | 1,510 | 22 |

- **"HSE-150 Android backend":** 132 of its 295 USER entries copy an earlier entry; 126 of them were written within 5 s of the original, and 113 are `[Scheduled]` Mailbox Deliveries. 36 ASSISTANT entries are also exact copies.
- **Worst file:** "Postgres DB for backend" (`2026-10-26-0347-klfgze.txt`), with 201 duplicates among 1,084 USER/ASSISTANT entries.
- **Since when:** duplicates go back to at least 2026-09-28. They grow with orchestration use: 122 on 2026-10-26, 125 on 2026-11-02 and 119 on 2026-11-03.

## Analysis (suspected mechanism — to be confirmed against the app log)

Two writers persist the same entry:

1. **Core writers.** `SessionTurnService.RunTurnAsync` appends the turn's USER prompt to the `.txt` file (`SessionTurnService.cs:101`) for every headless turn. When the target has a live Streaming Session Mode process (FR.19.13), the turn is redirected into it, and that process's primary callback (`TurnController.HandleStreamEvent`) appends the ASSISTANT/SYSTEM entries.
2. **UI write-back.** `SessionContentViewModel.RefreshFromJsonl` parses the Claude JSONL. For every entry past `_lastKnownEntryCount` it adds a message *and appends the entry to the `.txt` file* (`SessionContentViewModel.cs:447-448`). This write-back exists for turns run outside Tessyn (ISSUE.009).

The guards between them don't hold for headless turns:

- **Busy suppression is local.** Both watchers stand down only while the session's own `TurnController.IsBusy` is true (`OnSessionFileChanged` / `OnJsonlFileChanged`, `_isBusyProvider`). Headless turns never set it, so both watchers stay armed for the whole turn, including turns redirected into a live streaming process (inferred, consistent with Streaming mode being 30 of the 55 affected sessions).
- **The dedupe compares only the last message.** ISSUE.009's follow-up skips a JSONL entry only when it equals the *current last item* of `Messages`. That holds only if the `.txt` watcher's `RefreshFromFile` post has already added the Core-written entry. It may not have: the `.txt` watcher debounces 500 ms and re-arms on every write, while the JSONL watcher debounces 1000 ms, and the UI thread can be busy. In that case the last item is older, the check passes, and the entry is written a second time. Several entries arriving in one batch are compared only against the tail, never against each other or the file.
- **Counts diverge.** `_lastKnownEntryCount` counts JSONL-derived entries and `RefreshFromFile` counts `.txt` entries (`displayable.Skip(Messages.Count)`). The two lists don't correspond one to one: tool-use SYSTEM lines exist only in the JSONL projection, and `[Scheduled: …]` and other Core SYSTEM lines only in the `.txt`. Count-based "what's new" checks can therefore replay entries.
- **Watchers stay armed for every opened session.** `MainWindowViewModel._sessionCache` never evicts. Every session opened since app start keeps its `SessionContentViewModel` and both watchers, so the race runs for many sessions at once, not just the one on screen.

The app log should confirm this: look for `JSONL watcher: N new entries detected` and `FileWatcher: N new entries detected` around 2026-11-04T04:50:12Z for `2026-10-21-1253-bbbmgj.txt`, in `%AppData%\TessynDesktop\logs\`.

## Previous attempts

- **ISSUE.009 (2026-09-12):** write-back moved inside the UI-thread post, after `Messages.Add`. Fixed a display-only race.
- **ISSUE.009 follow-up (2026-09-14):** the "skip if equal to the current last message" dedupe in `RefreshFromJsonl`, and the "what's new" decision in `RefreshFromFile` moved inside the post. Fixed the interactive Streaming-mode replay, but not headless turns.

## Fix direction (for the implementer to confirm)

- **Suppress while any Tessyn turn runs.** Stand the JSONL write-back down while *any* Tessyn-run turn is in progress for the node: interactive, headless, or redirected into a streaming process. Key this off a node-level signal the Core already owns (the session's running state or per-node turn lock, FR.14.9), not the local `TurnController.IsBusy`. When the turn completes (`TurnCompleted`, FR.21.22), re-baseline `_lastKnownEntryCount` from the JSONL, allowing for a late JSONL flush (the 2026-09-14 race).
- **Dedupe by identity.** Base the write-back's dedupe on identity against the `.txt` file's recent tail (role plus exact content of the last few entries on disk), not only on the last displayed item.
- **Fix the cause, not the symptom.** Keep the write-back working for genuinely external turns (a `claude --resume` run in a terminal).

## Acceptance

- No new duplicate entries are written by scheduled turns, Mailbox Deliveries, orchestration turns or Telegram turns, in either Streaming or One-shot Mode. This includes turns redirected into a live streaming process, and sessions that are open in a tab, cached, or not open at all.
- Interactive turns still write each entry once (no ISSUE.009 regression).
- A turn run outside Tessyn on the same Claude session is still written back to the `.txt` file once.
- Existing session files are not rewritten: historical duplicates stay as they are, as with ISSUE.009. A one-off cleanup tool, if wanted, is a separate decision.

## Tests

Extract the "which JSONL entries still need writing to the `.txt`" decision into a pure, Avalonia-free function in Core, so it can be covered by `TessynDesktop.Tests`: tail-identity dedupe, batches, and suppression while a turn runs. The watcher/`Dispatcher` composition itself stays untested, as noted in ISSUE.009.

## Investigation (2026-11-04)

Log `%AppData%\TessynDesktop\logs\log-20261104.txt` (local time UTC+8, so 04:50:12Z = 12:50:12) for `2026-10-21-1253-bbbmgj.txt`, node `d70c59e9...`:

```
12:50:12.872 SchedulerService: Firing schedule 8efbd3ca -> node d70c59e9...
12:50:12.883 ClaudeStreamingSessionManager: Writing streaming turn to node d70c59e9... stdin (3073 chars)
12:50:14.040 ClaudeSessionImportService: Import: parsed 785 entries from ...a71cfe6d-....jsonl
12:50:14.040 SessionContentViewModel: JSONL watcher: 1 new entries detected from JSONL
```

- Confirmed: a headless scheduled turn redirected into the live streaming process. `SessionTurnService` wrote the USER entry at :12; the JSONL watcher (1 s debounce) fired at :14.04 and wrote the same entry back.
- There is **no** `FileWatcher: N new entries` line for this session in that window. So `Messages` did not hold the Core-written entry when the JSONL write-back ran, and the "equal to the last displayed item" dedupe could not catch it. This is the hypothesis in the Analysis section, with the tail-only dedupe as the failing guard.
- Only the local `TurnController.IsBusy` gated the watcher. Headless turns never set it, but the Core sets `ISessionRegistry` running state for them (`SessionTurnService.RunTurnAsync`).

Approaches tried: none were discarded. The fix direction was confirmed by the evidence and implemented directly.

## Resolution

Refined after orchestrator review: suppressing the write-back during headless turns would have lost or reordered the `[Tool: ...]` lines (230 in the HSE-150 file, all produced by this write-back; the JSONL parser emits only tool summaries as SYSTEM entries) and left the baseline stale. The raw stream keeps landing as before, minus the duplicates.

- Pure Core class `JsonlWriteBackPlanner` (`TessynDesktop.Core/Services/JsonlWriteBackPlanner.cs`, result `JsonlWriteBackPlanModel`). `Plan(jsonlEntries, lastKnownCount, txtTail, isHeadlessTurnActive)` always advances the baseline to the JSONL count. Outside headless turns it filters candidates by identity (role plus trimmed content) against the last `Constants.SessionFile.WriteBackTailWindow` (20) `.txt` entries, one-to-one (a tail entry absorbs one candidate, so a repeated external message is still written once).
  - Headless turn active: every SYSTEM entry (tool line) past the baseline is written with no tail dedupe (the baseline already offers each entry once; repeated identical lines such as a polling loop's must stay). USER/ASSISTANT are never written, because the Core writes them itself, even when not yet on disk.
  - Otherwise (external turns, late flushes): all entries past the baseline, tail-deduped.
- `SessionContentViewModel`: `OnJsonlFileChanged` is suppressed only by the interactive `TurnController.IsBusy` (re-baseline after interactive sends unchanged). "Headless active" = `IDaemonClient.IsSessionRunning(nodeId)` and not `IsBusy` (constructor argument `isHeadlessRunningProvider`, wired in `SessionViewModel`).
- `RefreshFromJsonl` now appends to the `.txt` file only; `RefreshFromFile` is the single path into `Messages`. The ISSUE.009 first race (Messages.Add vs. the `.txt` watcher) cannot return because the JSONL path no longer touches `Messages`, and the count-based file refresh can no longer see an entry twice or miss one.

## Tests

`TessynDesktop.Tests/Services/JsonlWriteBackPlannerTests.cs` (12 tests): no-op at baseline; external turn written once; headless mode writes tool lines only (USER/ASSISTANT skipped though absent from the tail) and advances the baseline; consecutive headless passes write each tool line once; three identical `[Tool: Bash] poll` lines across two passes are all written; a headless turn longer than the tail window yields no duplicates after it ends; dedupe against a non-last tail entry; partial-overlap batch; one-to-one tail matching; role mismatch; late flush after turn end; whitespace tolerance. Watcher/`Dispatcher` composition remains untested (as in ISSUE.009).

## Residual risks

- An external message identical to one of the last 20 `.txt` entries may be treated as already written.
- JSONL-only ASSISTANT entries of a headless turn (AskUserQuestion / plan markdown) are not written back while it runs.
- Existing duplicated entries are not rewritten.
