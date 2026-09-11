# ISSUE.007: Tree/global search misses relevant sessions and surfaces irrelevant ones

## Symptom

The "Search sessions..." tree search box frequently found nothing for queries the user
knew should match, and when it did find something the results often felt unrelated to
the query. Concretely: searching "Visual Studio" in the Tessyn project directory
returned an empty tree, even though a real Tessyn session discussing Visual Studio
integration existed and was already indexed.

Reported 2026-09-11.

## Root Causes (two independent bugs, one content gap)

### 1. Tree-filter search applied a ranked top-50 cutoff, not a membership test

`SessionTreeViewModel.ApplySearchFilter` called `IDaemonClient.SearchSessionsAsync`
(`ClaudeSessionIndexService.SearchAsync`) with no `projectSlug` and the default
`limit: 50`. That query is `ORDER BY bm25(messages) LIMIT 50` **across every indexed
project on the machine**, and the resulting external IDs are used only to toggle
visibility of nodes already present in the tree — a boolean membership test, not a
top-N results list. A hard `LIMIT` is the wrong tool for that: it silently drops
genuine matches in the currently-open project whenever unrelated projects elsewhere on
the machine happen to score higher on the same terms.

Verified against the live `session-index.db`: the real Tessyn session containing
"Visual Studio" ranked **#57** globally for that query — one place past the cutoff —
because sessions in other projects (e.g. an IDE-choice discussion in a different
project) scored higher. It wasn't a relevance problem; the match existed and was
correctly indexed, but the tree never saw it.

### 2. `tool_result` content was never indexed — most of a session's substance was invisible

`ClaudeSessionImportService.ExtractUserContent`'s array-content fallback only extracts
`type == "text"` blocks from a user turn's content array; it silently ignores
`type == "tool_result"` blocks (the output of Read/Grep/Bash/WebFetch/Task-subagent
calls). `ClaudeSessionIndexService.IndexSession` fed `ParseJsonlSession`'s output
straight into the FTS index, so tool results were never indexed at all — only the
assistant's own typed narration, the user's typed prompts, and thin one-line tool-use
summaries (e.g. `[Tool: Bash] git status`, truncated to ~80–120 chars) made it in.

Measured on one real Tessyn session: **12.7K chars** of indexed user text + **20.4K
chars** of indexed assistant text, versus **265K chars** sitting in `tool_result`
blocks — roughly **89% of the session's textual content** was unsearchable. Given this
project's workflow leans heavily on Task/subagent orchestration (per `CLAUDE.md`), the
substantive output of research work — like the actual "Visual Studio integration"
findings — typically lives in a tool result, not in the assistant's own narration, so
it was essentially never searchable.

### 3. (Contributing factor) No phrase-priority ranking

A bare multi-word query like `Visual Studio` becomes an implicit AND of `visual` and
`studio` under plain `bm25` ranking, which doesn't account for term adjacency. A
document where the two words appear hundreds of lines apart scores comparably to one
containing the genuine phrase "Visual Studio" — measured 99 AND-matches vs. 71 true
phrase-matches for that query against the live index. This diluted precision on top of
the recall problems above.

## Fix (implemented)

**`ClaudeSessionIndexService`**
- Added `SearchMatchingSessionIdsAsync(query)` — an unranked, uncapped
  `SELECT DISTINCT external_id FROM messages WHERE messages MATCH @query` — for
  membership tests. `SearchAsync` (ranked, `LIMIT`-capped) is now reserved for actual
  top-N display use (the import picker's global search).
- `SearchAsync` now tries an exact-phrase match first (`"term1 term2"`, quotes escaped)
  and only falls back to the looser AND-of-terms match when the phrase finds nothing —
  pushing genuine phrase hits above incidental co-occurrence. A `SqliteException` from a
  degenerate phrase (e.g. punctuation-only input the tokenizer reduces to nothing) is
  treated as "no phrase matches" rather than propagating.
- Added `IndexContentVersion` (`PRAGMA user_version`), currently `2`. On schema init, if
  the stored version is behind, every session's `mtime_ticks` is reset to `0`, which
  makes `ReindexAsync`'s incremental mtime check treat all of them as stale so they get
  rebuilt with the richer content on the next reindex pass — necessary because "the same
  file now yields more content" can't be detected by mtime alone.

**`ClaudeSessionImportService`**
- Added `ParseJsonlSessionForIndexing(jsonlPath)` — same extraction as `ParseJsonlSession`
  plus a new `TOOL_RESULT`-role entry per `tool_result` block's text content (string or
  array-of-text-blocks form, mirroring the existing user-content array fallback).
  Deliberately a separate method from `ParseJsonlSession`: that method also backs the
  session viewer and session import/export, and dumping raw tool output into those would
  flood the conversation view with noise the existing terse `[Tool: X] description`
  summaries were designed to avoid. Only the indexer calls the new method.
- Added `Constants.SessionFile.RoleToolResult = "TOOL_RESULT"`.

**`SessionTreeViewModel`**
- `ApplySearchFilter` now calls `SearchMatchingSessionIdsAsync` instead of
  `SearchSessionsAsync`, so tree filtering sees every matching session regardless of how
  it ranks against unrelated projects.

**`IDaemonClient` / `InProcDaemonClient` / `WireDaemonClient`**
- Added the `SearchMatchingSessionIdsAsync` passthrough (in-proc delegates to the index
  service; the wire client throws `NotSupportedException` like the rest of the
  cross-project search surface, pending TDCP wire support).

Existing indexed sessions get the richer `TOOL_RESULT` content automatically on the
next app-startup background reindex, via the `IndexContentVersion` bump — no manual
database reset needed.

## Notes / Limitations

- `TOOL_RESULT` content is indexed in full, uncapped, for symmetry with how
  user/assistant text is already indexed without a length cap. A single very large tool
  output (e.g. a full multi-thousand-line file read) is indexed as one FTS row; this
  grows the index but wasn't judged worth an arbitrary truncation the user didn't ask
  for.
- Phrase-first ranking only affects `SearchAsync` (the ranked/display path).
  `SearchMatchingSessionIdsAsync` intentionally uses the looser AND-of-terms match only,
  since membership tests want maximum recall — ranking/precision is irrelevant when the
  result is just "show or hide this tree node".

## Status

Fixed — build clean, 349/349 tests pass. Related: [[FR.13.15]] (Cross-Project Search
Index), [[ISSUE.003]] (prior search-index fix, same subsystem).
