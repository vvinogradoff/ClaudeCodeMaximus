# ISSUE-003: Session search (tree + global) returns empty results for almost every query

## Symptom

The tree search box (FR.10.x) and the import picker's global search (FR.13.15) went from
working to returning an empty tree / no hits for essentially any query, no matter what
was typed.

Reported 2026-07-27.

## Root Cause

`ClaudeSessionIndexService` (the SQLite FTS5 index backing both search paths, added in
Stage 6) only ever scanned the OS-default Claude CLI home:

```
~/.claude/projects/*/*.jsonl
```

But `ClaudeProfileService` launches each non-Default Claude account profile with
`CLAUDE_CONFIG_DIR` pointed at its own directory,
`%AppData%/TessynDesktop/profiles/<profileId>/`, so that profile's session JSONL files
land under `%AppData%/TessynDesktop/profiles/<profileId>/projects/*/*.jsonl` instead —
never under `~/.claude`.

Verified on the live machine: `~/.claude/projects` held only 11 project slugs (mostly
short, incidental sessions run under the Default profile — several literally just an
auth-failure exchange), while `%AppData%/TessynDesktop/profiles/` held 18 profile
directories, one of which (`profile_15`) contained the **entire** session history for
this project (`C--Projects-Personal-Tessyn`) — completely invisible to the indexer. A
scratch reindex against only `~/.claude` produced 20 indexed sessions / 40 messages
total; the real corpus the user actually searches against lives almost entirely in the
per-profile directories.

The search code itself (FTS5 query, ranking, tree filtering by external ID) was correct
— it just had nothing to search, so every query legitimately returned zero rows.

## Fix (implemented)

`ClaudeSessionIndexService`:
- `_claudeProjectsDir: string` → `_claudeProjectsDirs: IReadOnlyList<string>`.
- `GetDefaultClaudeProjectsDirs()` now returns `~/.claude/projects` **plus** one
  `projects` path per subdirectory of `%AppData%/TessynDesktop/profiles/` (each Claude
  account profile's own config dir).
- `ReindexCore` loops over all of them, skipping any that don't exist (e.g. the
  `_default_auth` scratch folder `ClaudeProfileService` also creates under `profiles/`).
- Constructor param renamed `claudeProjectsDir` → `claudeProjectsDirs`
  (`IReadOnlyList<string>?`); test call sites updated to pass single-element arrays.

Verified against the live machine's real `~/.claude` + profile directories (writing to a
scratch DB, not the live one): reindex went from 20 → 1031 sessions indexed, and content
search for terms known to exist only in this project's profile-15 history (e.g.
`"SessionTreeViewModel"`) now returns hits.

## Notes / Limitations

- Indexing scans *every* profile directory regardless of which profile is currently
  active — correct, since a session in the tree may have been created under any profile
  historically, and the tree itself isn't scoped per-profile.
- If profiles are renamed/removed (`_rem` suffix, see `RemovedProfile` in the glossary),
  their old JSONL is still scanned as long as the directory exists — consistent with how
  removed profiles keep their sessions for manual recovery.

## Status

Fixed — build clean, 221/221 tests pass. Related: [[FR.13.15]] (Cross-Project Search
Index), `ClaudeProfileService` (source of the per-profile `CLAUDE_CONFIG_DIR` behavior
this depends on).
