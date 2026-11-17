# ISSUE.016: Idle 1.9 GB / 70-110% CPU and GC-driven UI hangs: code index rescans and unbounded session cache

**Status:** Fixes built and tested in branch `wpf/p0-perf` (1724 + 95 tests green). Not yet observed in the running app; compare MEM lines before and after (see Verification).
**Affects:** the Avalonia app (daily driver). Related: ISSUE.015 (tree-search FTS), WPF plan items 0.4, 0.5; `.perf-diag/` reports 01-12.

## Summary

The dominant cost was not the session cache but the **code index** (`CodeIndex`, the `#` / `##` autocomplete). It indexed every file of every working directory a session had ever opened (HardStop: 337k files and growing), rebuilt the whole index and re-parsed every `.cs` file after any file change in that tree, and was never released. Rebuild garbage (about 3 strings plus a model object per file, on the order of 200 MB per rebuild for HardStop) explains the 3-7M `IndexedFileModel` objects and 3.3 GB of `System.String` in the heap histograms, the idle CPU, gen1 collections almost as frequent as gen0, and the GC pauses behind the UI hangs. The session cache (every opened `SessionViewModel` kept forever, undisposed subscriptions on long-lived tree nodes) is the second contributor.

## Evidence

- `diag/heap-20261113-145639.txt`: 31.5M objects, 3,935 MB; `System.String` 20.8M objects / 3,267 MB; `IndexedFileModel` 6.82M objects / 260 MB (+122 MB arrays); `CodeSymbolModel` 42k. Everything else (Avalonia visuals, bindings) is single-digit MB per type. Earlier histograms: `heap-20261112-123603` 3.14M models / 1.5 GB strings; `heap-20261113-005704` 3.94M models / 1.9 GB strings. The count grows with each histogram, i.e. with uptime.
- `logs/log-2026111*.txt`, `Code index ready for ...`: HardStop 287k files (11-11) -> 298k -> 314k -> 319k -> 337k (11-13); MentalCure 46k, fanvue.one 42k, QuickBooksSQL 27k files / 35k symbols. Nothing ever logs a release, because nothing calls `ReleaseIndex`.
- Live `diag_snapshot` (2026-11-14): priv 1.47 GB, heap 859 MB, fragmented 237 MB, LOH 206 MB, gen0/1/2 = 10854/10034/326, GC pause total 286 s, cpu 69%.
- MEM series 12:19-12:37: gen0 grows by 40-80 per minute while idle, cpu 72-111%, frag swinging 27 MB <-> 718 MB (large arrays built and dropped).
- `hang-*.txt` (6): 4 are startup `RefreshLastPromptTimes` / `RefreshGitOrigins` file reads on the UI thread (startup scans, not addressed here); 1 is a 9.3 s stall with an idle UI thread while the render thread was busy; the 2.5 s hang at 12:26 had a 749 ms GC pause (gen2 +2).
- `SLOW-UI`: `session-switch-until-idle` 161 lines today (326-2621 ms), `session-switch` 28; `restore-last-used-settings` only 1 (it is cheap per call but re-parses the whole file, growing with transcript size).
- HardStop tree: `Code` alone holds 860k files (338k remain after the old exclusions), `vcpkg` 23k, `LabControl` 66k, `Data` 16k.

## Root causes (ranked)

| # | Cause | Where | Confidence |
|---|---|---|---|
| 1 | Any file event in an indexed tree triggers a **full** re-scan plus Roslyn parse of every `.cs`; a new `Timer` per event; overlapping rescans possible; `Changed` events of log or build files (outside the exclusion list) keep it permanently busy | `CodeIndex.cs` (pre-fix) `ScheduleReindex`, `ProcessPendingChanges` -> `FullScan` | High (heap histogram, idle CPU and gen churn all fit; not profiled live) |
| 2 | Index never released: `GetOrCreateIndexAsync` per `SessionViewModel`, `ReleaseIndex` never called, so each directory's file table, symbol table and `FileSystemWatcher` live until exit | `SessionViewModel.cs:480`, `CodeIndexService.cs` | High |
| 3 | Per-file cost three strings (name, relative, absolute path) plus object; no cap; reparse points and vendored trees (vcpkg, venv) indexed | `IndexedFileModel`, `CodeIndex.ScanDirectory` | High |
| 4 | `_sessionCache` keeps every opened `SessionViewModel` (messages, two watchers, timers, code-index reference) forever; `Dispose` was never called, and had gaps (see 5) | `MainWindowViewModel.cs:36` (pre-fix) | High (code), medium on size (messages are a small share of the heap: no Avalonia/VM type shows in the histogram top) |
| 5 | Undisposed `WhenAnyValue(...).Subscribe` on the long-lived `SessionNodeViewModel` (SessionViewModel x2, TurnController x1) would keep an evicted VM and its transcript reachable; thinking timer and pending draft timer not stopped | `SessionViewModel.cs:464-465`, `TurnController.cs:281` | High (code) |
| 6 | Switch re-reads and parses the whole session file | `SessionViewModel.RestoreLastUsedSettings` | High (code); small share of the measured 255 ms |
| 7 | Not addressed: transcript not virtualized (SessionView build dominates `session-switch-until-idle`), startup scans on the UI thread, 60 s recency refresh over ~3,100 nodes | see WPF plan (transcript virtualization, 1.10) | Known |

Ruled out: `SessionView` event subscriptions (planned D2.3). `SessionView.OnDataContextChanged` already unsubscribes `Messages.CollectionChanged`, `CancelScheduleRequested`, `StreamingDisableConfirmationRequested` and the progress-message handler from the previous VM; the view is a single instance. The real leak was on the VM side (cause 5). The 1 s screensaver tick and the 60 s recency tick are cheap.

## Fixes

1. `547cf52` Code index: apply file events to the affected file or directory subtree only; single reusable debounce timer; one processor at a time; full re-scan only after a watcher buffer overflow; ignore `Changed` events for non-`.cs` files; more excluded directories (vcpkg, venv, .next, .nuget, .gradle, .terraform, .cache, ...); skip reparse points; cap 200,000 files and 2 MB per parsed `.cs`; lazy snapshots; `AbsolutePath` derived from a shared root; `SessionViewModel.Dispose` releases its reference; tier-1 early exit in search.
2. `939efe0` Index keyed by the relative path, shared with the model (one path string per file).
3. `36b7286` `BoundedLruCache` (Core) with active session + 4 (`Constants.SessionCacheCapacity`); `SessionViewModel.CanEvict` = not busy, not node-running, no live streaming process, not externally active, no unsent input; evicted VMs disposed after the new session is active; `Dispose` now idempotent, disposes node subscriptions, thinking timer, flushes a pending draft.
4. `20ee2fa` `LastUsedSessionSettingsReader` (Core): 64 KB tail, widened to 256 KB, 1 MB, whole file only while a value is missing.

## Follow-ups (not done)

- A tree larger than 200k files (HardStop) is truncated for autocomplete (Warning logged). A real fix is `.gitignore`-aware indexing or indexing on first `#` use (lazy) instead of at session open; a UX decision.
- Index per-file memory could drop further with a trie or a name-only array.
- Startup `RefreshLastPromptTimes` / `RefreshGitOrigins` still read files on the UI thread (4 of 6 hangs).
- Transcript virtualization (WPF 1.10) for switch latency; the Avalonia bump (D2.5) is already done on master (11.3.23, commit db8913d).
- Scroll offset of an evicted session is lost on reload.

## Verification

After deploying, compare `MEM` lines at the same uptime: expect gen0 rate while idle to drop to a few per minute, cpu below about 5% idle, heap not growing over a day. Look for `Code index ready` once per directory and no repeats. If heap still grows, take `diag_heap` and look for `SessionViewModel` / `MessageEntryViewModel` counts (should stay near 5 sessions' worth).

## Follow-up 2026-11-14: render-loop CPU after the fixes

After the restart onto the fixed build, memory was settled: about 0.7 GB private and a 440 MB heap, against 1.9 GB and 1.3 GB before, with gen-1 churn gone. But CPU stayed at 73-78 % of one core while idle.

- **Measurement:** per-thread CPU sampling showed one thread using about 3.7 s of 3.8 s over 5 s. A ClrMD snapshot (`DataTarget.CreateSnapshotAndAttach`, three samples) showed it was Avalonia's composition render thread: `RenderLoop.TimerTick` -> `ServerCompositor.RenderCore` -> about 20 nested `ServerCompositionContainerVisual.Update` calls.
- **Cause:** the tree-search "Searching…" spinner (`SessionTreeView.axaml`, FR.1.11) and the Tags tab spinner (`TagSearchView.axaml`) had an infinite rotate animation on the style selector `TextBlock`. That selector always matches, so both spinners kept animating while hidden. Every tick requested a frame, and every frame re-walked the whole visual tree, including the non-virtualized transcript of the open session.
- **Fix:** commit `7de9c1f`. The animation runs only on `TextBlock.spinning`, a class bound to `IsSearching` / `IsLoading`.
- **Verify** after the next restart: `cpu=` in the MEM lines should drop to low single digits when the app is idle.

## Follow-up 2026-11-15: measured after the fixes, and the startup repair

Data: `logs/log-2026111[1-5].txt` and `diag/` (10 hang captures, 3 heap histograms). The restart at 2026-11-15 15:02 runs a build from 11:32 that includes `547cf52`, `939efe0`, `36b7286`, `20ee2fa`, `7de9c1f` and `bfe4691`.

| Metric | 2026-11-13 | 2026-11-15, after 15:02 |
|---|---|---|
| CPU while idle (MEM `cpu=`) | 100-490 % | 3-13 % |
| Share of time paused for GC | 5.7-8.7 % | 0.3-0.5 % |
| GC heap | 1.4-2.5 GB, 19-31 M objects | 0.4-1.3 GB |
| UI stalls of 2 s or more | about 16 a day; 228 of 237 stalls in 11-11..15 had no UI operation running, and their GC pause matched the stall (for example 13.4 s with 12.3 s of GC pause) | 3 in the first 20 min, 2.4-3.6 s each, about 1 s of GC pause each |

The no-operation stalls were GC pauses: the hang captures show an idle UI thread (`Win32DispatcherImpl.RunLoop`) while the stall lasted.

**Remaining startup hang:** `SessionFileService.RepairCorruptedCompactions` read every session file twice on the UI thread, in the `MainWindowViewModel` constructor. It froze the UI for 33.7 s on 2026-11-15 15:02 (`hang-20261115-150259.txt`), and for 14-30 s at earlier starts. No `Repaired N` line appears in the 11-11..15 logs. Fixed in `bf44890`: the repair runs on a background task and reads each file once.

**Still open:**
- HardStop's index is at the 200,000-file cap, and building it adds about 450 MB of heap a few minutes after startup (MEM 15:06-15:07). The remaining 2-4 s GC stalls follow it. Decided 2026-11-15 (Architect): build the index lazily on the first `#` / `##` autocomplete, cap unchanged. Done in `7da82ff`: `SessionViewModel.EnsureCodeIndex`, called from `SessionView.UpdateAutocompleteTrigger`; suggestions refresh when the index is ready.
- `session-switch-until-idle` averages 1.1 s, with a maximum of 6 s (191 lines). That is the non-virtualized transcript build, left to the WPF client.
