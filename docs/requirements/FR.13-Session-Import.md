> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.13 — Session Import

The application supports importing Claude Code sessions that were created outside of TessynDesktop (e.g., from the terminal, VS Code, JetBrains, or other tools). Import converts a Claude Code JSONL session file into the TessynDesktop session format and adds it to the session tree, preserving the Claude session ID for immediate resumability.

**FR.13.1 — Entry point:** The context menu on Directory and Group nodes shall include an "Import Claude Session" option. This scopes the import to the working directory of the selected node.

**FR.13.2 — Session discovery:** When the import picker opens, the application derives the project slug from the node's working directory (using the same algorithm as `ClaudeSessionStatusService`) and scans `~/.claude/projects/<slug>/` for `.jsonl` files. Each file represents a discoverable session.

**FR.13.3 — Discovery data extraction:** For each discovered JSONL file, the following metadata is extracted via local file parsing (no Claude CLI calls):

| Field | Source |
|---|---|
| Session ID | File name (UUID portion before `.jsonl`) |
| Created | Timestamp of the first event in the file |
| Last used | Timestamp of the last event in the file |
| Message count | Count of `user` + `assistant` type events |
| First user prompt | Content of the first `user` event's `message.content`, truncated to 500 characters |

**FR.13.4 — Import picker dialog:** A dialog displays all discovered sessions as a scrollable list, sorted by last-used date (most recent first). Each row shows:
- Session title (initially the truncated first user prompt; replaced by a Claude-generated title when available — see FR.13.8)
- Date range (created → last used)
- Message count
- An "already imported" indicator if the session's ID matches any existing `ClaudeSessionId` in the tree (see FR.13.10)

**FR.13.5 — Multi-select:** The picker supports multi-selection via checkboxes. An "Import Selected" button triggers import for all checked sessions. Already-imported sessions cannot be checked.

**FR.13.6 — Search box:** The picker includes a search text box. Pressing Enter sends the query along with session summaries to the Claude CLI for semantic matching (see FR.13.9). Results are reordered by relevance. When the Claude CLI is unavailable, search falls back to case-insensitive substring matching against first user prompts.

**FR.13.7 — JSONL parsing:** The import service parses a Claude Code JSONL file into TessynDesktop session entries. Parsing rules:

| JSONL event type | Action |
|---|---|
| `user` | Extract `message.content` (string) → `USER` entry with original timestamp |
| `assistant` | Extract `message.content[]` blocks where `type` is `"text"` → concatenate text → `ASSISTANT` entry with original timestamp |
| `assistant` (tool_use blocks) | Extract `name` field from each `tool_use` block → `SYSTEM` entry formatted as `[Tool: <name>] <input summary>` with original timestamp |
| `system`, `progress`, `file-history-snapshot`, `queue-operation`, `pr-link` | Skipped — no conversation content |

Parsing is line-by-line with per-line error handling: malformed or unrecognised lines are skipped and logged. Files are opened with `FileShare.ReadWrite` to handle concurrent access from an active Claude process. The `thinking` block type within assistant events is skipped (internal reasoning, not conversation content).

**FR.13.8 — Claude-powered title generation:** When the import picker opens, the application asynchronously generates titles for all discovered sessions by calling the Claude CLI in print mode:
```
claude -p --tools "" --no-session-persistence --model <model> --output-format json
```
The model is selected using a fallback order: Haiku (preferred for speed/cost), then the user's selected model from FR.12, then no `--model` flag (CLI default). Sessions are batched (up to 20 per call) with each session identified by its ID. The prompt requests a JSON response mapping session IDs to concise 3–6 word titles. As titles arrive, they progressively replace the first-prompt preview in the picker UI. If title generation fails, the truncated first prompt remains as the display text and the default session name. **Titles are persisted** (Stage 6, `IClaudeSessionIndexService`) keyed by the Claude CLI session ID, so a session already titled in a previous import-picker session is never regenerated — reopening the picker (or a future app run) reuses the stored title at zero additional CLI cost.

**FR.13.9 — Claude-powered search (current project):** When the user enters a search query and presses Enter with global search off, the application sends session summaries (session IDs + truncated first prompts + message counts) along with the query to the Claude CLI (same print-mode invocation as FR.13.8), scoped to the sessions already discovered under the selected source directory. The prompt requests a JSON array of matching session IDs ranked by relevance. The picker reorders to show matches first. A spinner indicates search is in progress.

**FR.13.10 — Duplicate detection:** Before displaying the picker, the application collects all `ClaudeSessionId` values from existing session nodes across the entire tree. Discovered sessions whose ID matches an existing node are marked "already imported" in the picker and cannot be selected for import.

**FR.13.11 — Import execution:** For each selected session, the import process:
1. Parses the JSONL file into session entries (per FR.13.7)
2. Creates a new TessynDesktop session file (standard naming: `YYYY-MM-dd-HHmm-{6random}.txt`)
3. Writes all parsed entries to the file using the standard session file format (`[timestamp] ROLE` headers)
4. Creates a `SessionNodeModel` with: the generated title (or truncated first prompt) as `Name`, the new file as `FileName`, the working directory from the parent node, and the original Claude session ID as `ClaudeSessionId`
5. Adds the session node to the tree under the selected parent (Directory or Group)
6. Saves `appsettings.json`

**FR.13.12 — Resumability after import:** Because the imported session retains the original `ClaudeSessionId`, the session is immediately resumable via `--resume` if the JSONL file still exists in `~/.claude/projects/<slug>/`. The existing resumability check (60-second poll) applies.

**FR.13.13 — Fallback behaviour:**
- Claude CLI unavailable for title generation → truncated first prompt used as session name
- Claude CLI unavailable for search → case-insensitive substring match against first prompts
- JSONL parse errors on individual lines → skip and log, import remaining entries
- Empty JSONL file (no user/assistant events) → shown in picker but greyed out, not selectable
- No JSONL files found for the slug → picker shows empty state with explanatory message

**FR.13.14 — CLI process management for import:** All Claude CLI calls for title generation and search use timeouts, stderr capture, and structured error propagation. The process management reuses the existing `ClaudeProcessManager` infrastructure (shared `TryStartProcess` pattern). The `--tools ""` flag prevents the CLI from executing any tools. The `--no-session-persistence` flag prevents the CLI from creating session files for these utility calls. The model for assist calls follows a fallback order: Haiku (preferred), then the user's FR.12 model selection, then no `--model` flag (CLI default). This ensures the feature works regardless of the user's plan entitlements.

**FR.13.15 — Cross-project search index (Stage 6, `IClaudeSessionIndexService`):** Toggling "Global" search (`IsGlobalSearch`, always available — no external dependency) searches message content across every Claude CLI session on the machine, not just the currently selected project. Backed by a SQLite FTS5 index (`%AppData%/TessynDesktop/session-index.db`) built and incrementally refreshed (by JSONL file mtime) from every `*/projects/*/*.jsonl` under **both** `~/.claude/` and every registered Claude account profile's own config directory (`%AppData%/TessynDesktop/profiles/<profileId>/`) — a profile's sessions run with `CLAUDE_CONFIG_DIR` set to its own folder, so they never appear under `~/.claude`. (Fixed 2026-07-27 — the index originally scanned only `~/.claude/projects`, silently missing every session run under a named profile and making tree/global search return empty results for most real usage.) Reindexed once in the background at app startup and exposed through `IDaemonClient.SearchSessionsAsync`/`ReindexSessionsAsync`.

Indexed message content includes USER, ASSISTANT, and tool-use SYSTEM entries (as parsed for display, FR.13.7) **plus** a TOOL_RESULT entry per `tool_result` block's text content — the output of Read/Grep/Bash/WebFetch/Task-subagent calls, which typically carries most of a session's substantive content but is never shown in the session view or exported .txt files (kept out of those to avoid flooding the conversation display with raw tool output). An `IndexContentVersion` marker (SQLite `PRAGMA user_version`) forces a one-time full rebuild of an existing index whenever the extracted content scope changes, since the per-file mtime check alone can't detect "the same file now yields richer content." (Fixed 2026-09-11 — TOOL_RESULT indexing added; see [[ISSUE.007]].)

`SearchAsync` results are ranked by FTS5 `bm25` relevance, preferring an exact-phrase match (adjacent tokens) and falling back to a looser AND-of-terms match only when the phrase finds nothing — plain AND-of-terms ranking otherwise scores incidental term co-occurrence the same as a genuine phrase hit. Results carry the session's stable external ID directly (no separate ID-resolution round trip) and are capped (default 50) for top-N display use, e.g. the import picker's global search.

Free-typed queries are never passed to FTS5 as raw syntax. The query is split on whitespace and each word is quoted as an FTS5 string, so characters FTS5 treats as syntax (`@ . : \ - "` etc.) are matched literally — e.g. `vitaly@example.com` becomes the phrase of tokens `vitaly example com`. Strict match = the whole query as one phrase; loose match = every quoted word present (implicit AND). `SearchSessionScoresAsync(query, projectSlug)` returns, per matching external ID, the count of messages with a strict match and the count with a loose match — the input to the tree's relevance ordering (FR.1.12). `IndexSessionAsync(externalId, workingDirectory)` re-indexes a single session's JSONL (located under each Claude projects directory by the working directory's project slug) when its mtime changed, for live indexing (FR.1.13); index writes from it and from the startup `ReindexAsync` are serialized so they never contend for the SQLite write lock. (Fixed 2026-11-05 — before this, any query containing FTS5 syntax characters failed with `fts5: syntax error` and silently dropped to the slow file-scan fallback; see [[ISSUE.015]].)

`SessionTreeViewModel`'s tree search bar filters the tree by content match, including sessions from other projects, via the separate `SearchMatchingSessionIdsAsync` — an unranked, uncapped distinct-ID lookup. This is deliberately not `SearchAsync`: filtering already-known tree nodes is a membership test, not a ranked results list, and a hard result cap there would silently hide genuine matches in the current project whenever unrelated projects elsewhere on the machine score higher on the same terms. (Fixed 2026-09-11, see [[ISSUE.007]].)

Sessions found via global search that aren't yet tracked in the current project's tree are synthesized into importable picker entries (original project path best-effort recovered from the Claude CLI project slug, matching FR.13.11's cross-project import path).

**Upkeep (decision A5; reports `.wpf-work/330-index-upkeep.md`, `330b-index-purge-scope.md`, `330c-index-review-fixes.md`, `330d-index-n1.md`).** The index is maintained in the background. Each startup runs one upkeep pass on a low-priority thread. The pass has a single writer, works in small batches with short yields and sets a 30 s SQLite busy timeout. It does the following:
- Migrates an old schema.
- Reindexes changed sessions. A JSONL file written to within the last 20 seconds is left for a later pass (at most 3 retries, 40 s apart, one chain at a time). A future-dated file is not "live". A title-only session without messages is indexed too. A file with no indexable messages gets an empty map row, so it is not parsed again.
- Purges orphans (see below).
- Runs the FTS5 `optimize` command when at least 200 sessions were indexed or purged.
- Runs `VACUUM` only when at least 20% of the file and 64 MB are free pages. It never runs on the UI thread, never while a search is in flight, and not when the free disk space is below twice the database size.
- Checkpoints the WAL: passive normally, a full `TRUNCATE` (under the write gate) only after an optimize or a `VACUUM`.
Indexed message content is never capped or truncated, including tool output (decision A5).

**Session rowid map.** The table `session_rows (external_id, first_rowid, last_rowid, row_count)` maps each session to the FTS5 rowid range of its messages, so replacing or deleting one session's rows is an indexed range lookup, not a scan of the FTS table. The FTS table and the search queries are unchanged, so results and ranking are the same. `PRAGMA user_version` is the marker: 2 = the tool_result content scope, 3 = the rowid map. A new database starts at 3. Moving from 2 to 3 builds the map in one transaction (about 0.7 s on a 1 GB index) and does not touch the FTS table. Every write runs as an immediate transaction (`BEGIN IMMEDIATE`), including title writes, which queue behind the writer gate without blocking a thread. Each upkeep pass first audits the map against the FTS table (the mapped row total and the highest mapped rowid against the row count and the highest rowid) and rebuilds the map when they disagree.

**Orphan purge.** A session's index rows are removed only when both conditions hold: its JSONL exists in no Claude home, and no tree node refers to its id (decision A17). The ids of a node are its `ClaudeSessionId`, `ExternalId` and every `PriorClaudeSessionIds` entry. Rows and titles of tree sessions therefore stay, even when Claude has deleted the JSONL. The purge runs only after a complete scan and has these guards:
- It is skipped when the scan saw no file while the index is not empty, or when a folder could not be listed.
- A candidate is purged only if its recorded JSONL home was listed in the same scan and the file is still absent.
- A configured home that is missing but still holds indexed sessions does not stop the purge. Its sessions are kept (their home was not listed), and a Warning reports how many.
- A purge of more than 50% of the indexed sessions is skipped with a Warning.
- With no tree source configured, nothing is purged.
The log line reports the removed count and the number of tree sessions kept without a JSONL. On a copy of the live index (final rules, 2026-11-15), the first upkeep pass took 11.7 s, including a 0.9 s migration. It removed 657 of 4,267 sessions, kept 1,492 tree sessions without a JSONL, and skipped 105 sessions whose recorded home was not listed (one profile folder whose `projects` folder is missing).

A failing write of one session is logged and counted, and the pass carries on. `IndexSessionAsync` (after a turn) is asynchronous and does not throw on a database error.

---
