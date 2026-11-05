# ISSUE.015 — Tree search: FTS5 syntax errors, slow fallback, account-email false hits, stuck spinner

**Status:** In progress 2026-11-05.
**Affects:** Session tree search box (FR.1.9–FR.1.13), Cross-Project Search Index (FR.13.15).
**Related:** [[ISSUE.007]] (earlier tree-search fixes: rank cutoff, tool_result content).

## Symptoms

1. Search often feels slow.
2. Searching for the user's own account email (`vitaly@vvinogradoff.com`) shows almost every session.
3. The searching spinner keeps spinning after the search seems done, and the tree does not filter until another session is selected. Then it filters.
4. Sessions run during the current app run are not found until the app restarts.

## Measurements (live index, 2026-11-04)

- Index: 3,790 sessions, 392,117 messages, 910 MB. 2,551 of the 2,739 tree sessions are in it.
- FTS5 queries are fast: 0.01–0.08 s, even for terms matching 1,000+ sessions. The index is not the bottleneck.
- `vitaly@vvinogradoff.com` unquoted → `fts5: syntax error near "@"`. Same for `foo.bar` (near "."), `C:\Projects` (`no such column: C`), and any query containing `"`.
- The log shows `Session search failed, falling back to local substring match` for every such search on 2026-11-04 and 2026-11-05.
- As an exact phrase, the email appears in 217 sessions of the index. These are real content (config files, seed SQL, DB query output) and are kept.
- The session `.txt` files carry the account on every user-turn header: `[...] USER profile="vitaly@vvinogradoff.com"` (1,295 turns).

## Root causes

1. **FTS5 syntax errors.** `EscapeFtsQuery` only doubled `"` and passed the rest to FTS5 as raw query syntax. Any `@ . : \ -` made the query invalid.
2. **Slow fallback with false hits.** On any search error, the tree fell back to `SessionSearchService.FindMatchingSessionFiles`: `File.ReadAllText` of all 2,965 `.txt` files (63 MB) and a substring match on the raw text, header lines included. That is both the slowness and the email false hits (symptoms 1, 2).
3. **Stuck spinner.** `File.ReadAllText` opens with `FileShare.Read`, which fails with a sharing violation while a running session has the file open for writing. The exception was thrown inside the `catch` block of `SessionTreeViewModel.ApplySearchFilter`'s background task, so nothing caught it. `IsSearching` was never reset and the tree never filtered. Selecting another session re-ran the search, which usually succeeded on the retry (symptom 3).
4. **No live indexing.** `ReindexSessionsAsync` ran only once at app startup (symptom 4).

## Resolution (planned, being implemented)

- Safe query building: split on whitespace and quote each word as an FTS5 string. Strict = whole-query phrase, loose = all words (FR.13.15).
- `SearchSessionScoresAsync` gives strict and loose match counts per session; the tree is ordered by relevance, view-only (FR.1.12).
- `IndexSessionAsync` plus a `TurnCompleted`-driven updater for live indexing; index writes serialized (FR.1.13).
- Fallback only when the index cannot be opened. It matches message bodies only, opens files with `FileShare.ReadWrite` and skips unreadable files. `IsSearching` is reset on every path (FR.1.9, FR.1.11).
