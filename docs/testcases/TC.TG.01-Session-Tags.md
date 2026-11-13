# TC.TG.01 — Session Tags

Related requirements: FR.24.1–FR.24.14 in [requirements.md](/docs/requirements.md). Related use cases: [UC.TG.01](/docs/usecases/UC.TG.01-Session-Tags.md).

**Legend.** AUTOMATED = a Core integration test in `code/TessynDesktop.Tests` (xUnit); the intended test class is named in each case. MANUAL = a UI test in Avalonia. File-based tests use a temp-dir root passed through the constructor. The server is built the way `AgentMcpServerNotifyUserTests` builds it, with a fake session-file service that records `AppendMessage(…, Constants.SessionFile.RoleSystem)` calls. Exact strings below are the expected results; assert them verbatim.

## Summary

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.TG.01 | Parser: accepted forms | AUTOMATED | UC.TG.01, UC.TG.02 | FR.24.1, FR.24.4 |
| TC.TG.02 | Parser: invalid input | AUTOMATED | UC.TG.02, UC.TG.03 | FR.24.4, FR.24.10 |
| TC.TG.03 | Name token matching | AUTOMATED | UC.TG.04 | FR.24.2 |
| TC.TG.04 | Workspace map | AUTOMATED | UC.TG.01, UC.TG.03 | FR.24.3 |
| TC.TG.05 | Store: persistence, corruption, atomic write | AUTOMATED | UC.TG.12 | FR.24.5 |
| TC.TG.06 | Service: add, already tagged, remove | AUTOMATED | UC.TG.01, UC.TG.05 | FR.24.2, FR.24.4 |
| TC.TG.07 | Service: name-derived tags and dedupe | AUTOMATED | UC.TG.04 | FR.24.2, FR.24.3 |
| TC.TG.08 | Service: ordering | AUTOMATED | UC.TG.07 | FR.24.1, FR.24.7 |
| TC.TG.09 | Service: events | AUTOMATED | UC.TG.04, UC.TG.08 | FR.24.3, FR.24.7 |
| TC.TG.10 | Tool: result lines | AUTOMATED | UC.TG.01–UC.TG.05 | FR.24.4 |
| TC.TG.11 | Tool: partial success, limits, listing | AUTOMATED | UC.TG.11 | FR.24.4 |
| TC.TG.12 | Tool: SYSTEM entry | AUTOMATED | UC.TG.01, UC.TG.05 | FR.24.4 |
| TC.TG.13 | Tool: workers allowed; description | AUTOMATED | UC.TG.10 | FR.24.4, FR.24.6 |
| TC.TG.14 | Guidance gate and plumbing | AUTOMATED | UC.TG.09 | FR.24.6 |
| TC.TG.15 | Guidance once per node per run | AUTOMATED | UC.TG.09 | FR.24.6 |
| TC.TG.16 | Guidance text, suffix and placement | AUTOMATED | UC.TG.09 | FR.24.6 |
| TC.TG.17 | `SessionTagUrlPolicy` | AUTOMATED | UC.TG.06 | FR.24.7 |
| TC.TG.18 | `InProcDaemonClient` | AUTOMATED | UC.TG.08 | FR.24.8 |
| TC.TG.19 | Tag row placement and visibility | MANUAL | UC.TG.07 | FR.24.7 |
| TC.TG.20 | Wrapping with many tags | MANUAL | UC.TG.07 | FR.24.7 |
| TC.TG.21 | Chip look and tooltip | MANUAL | UC.TG.06, UC.TG.07 | FR.24.7 |
| TC.TG.22 | Click opens the browser | MANUAL | UC.TG.06 | FR.24.7 |
| TC.TG.23 | Chip order | MANUAL | UC.TG.07 | FR.24.1, FR.24.7 |
| TC.TG.24 | Live refresh on `tag_session` | MANUAL | UC.TG.08 | FR.24.7, FR.24.8 |
| TC.TG.25 | Live refresh on rename and on learning a team | MANUAL | UC.TG.04, UC.TG.08 | FR.24.2, FR.24.3, FR.24.7 |
| TC.TG.26 | Visible in Session and Focus views | MANUAL | UC.TG.07 | FR.24.7 |
| TC.TG.27 | End to end: a session tags itself | MANUAL | UC.TG.01–UC.TG.05, UC.TG.09 | FR.24.4, FR.24.6, FR.24.7 |
| TC.TG.28 | Phase 2 status colours (DEFERRED) | NOT BUILT | UC.TG.13 | FR.24.9 |

## FR → TC coverage

| FR | Test cases |
|---|---|
| FR.24.1 | TC.TG.01, 08, 23 |
| FR.24.2 | TC.TG.03, 06, 07, 25 |
| FR.24.3 | TC.TG.04, 07, 09, 25 |
| FR.24.4 | TC.TG.01, 02, 06, 10, 11, 12, 13, 27 |
| FR.24.5 | TC.TG.05 |
| FR.24.6 | TC.TG.13, 14, 15, 16, 27 |
| FR.24.7 | TC.TG.08, 09, 17, 19–27 |
| FR.24.8 | TC.TG.18, 24 |
| FR.24.9 | TC.TG.28 (deferred) |
| FR.24.10 | TC.TG.02 (PR without owner), TC.TG.07-5 (no text detection) |

---

## Automated tests

### TC.TG.01 — `SessionTagParserTests` (accepted forms) — FR.24.1, FR.24.4 (UC.TG.01, UC.TG.02)

Each row is a theory case: input → kind, key, label, URL. The Linear workspace map contains `HSE → hardstop` where a short key is used.

| Input | Kind | Key | Label | URL |
|---|---|---|---|---|
| `https://linear.app/hardstop/issue/HSE-230` | LinearIssue | `HSE-230` | `HSE-230` | `https://linear.app/hardstop/issue/HSE-230` |
| `https://linear.app/hardstop/issue/HSE-230/fix-the-thing?x=1` (slug, query ignored) | LinearIssue | `HSE-230` | `HSE-230` | `https://linear.app/hardstop/issue/HSE-230` |
| `HSE-230` (short) | LinearIssue | `HSE-230` | `HSE-230` | `https://linear.app/hardstop/issue/HSE-230` |
| `hse-230` (lower case; key upper-cased) | LinearIssue | `HSE-230` | `HSE-230` | `https://linear.app/hardstop/issue/HSE-230` |
| `https://github.com/tryhardstop/windows-app/pull/215` | GitHubPullRequest | `tryhardstop/windows-app#215` | `windows-app PR #215` | `https://github.com/tryhardstop/windows-app/pull/215` |
| `https://github.com/tryhardstop/windows-app/pull/215/files?w=1#diff` (path, query, fragment ignored) | GitHubPullRequest | same | same | same |
| `tryhardstop/windows-app#215` | GitHubPullRequest | same | same | same |
| `https://github.com/tryhardstop/windows-app` | GitHubRepository | `tryhardstop/windows-app` | `windows-app` | `https://github.com/tryhardstop/windows-app` |
| `https://github.com/tryhardstop/windows-app.git` | GitHubRepository | same | same | same |
| `https://github.com/tryhardstop/windows-app/` | GitHubRepository | same | same | same |
| `tryhardstop/windows-app` | GitHubRepository | same | same | same |

- **TC.TG.01-1 Trim:** `"  HSE-230  "` is parsed as `HSE-230`.
- **TC.TG.01-2 Case kept for label and URL:** `TryHardStop/Windows-App#215` → label `Windows-App PR #215`, URL `https://github.com/TryHardStop/Windows-App/pull/215`; key compares case-insensitively with `tryhardstop/windows-app#215` (same tag).
- **TC.TG.01-3 Learning:** parsing a full Linear URL reports the learned pair (team key `HSE`, workspace `hardstop`); a short key does not.
- **TC.TG.01-4 Linear URL host case:** `https://LINEAR.APP/hardstop/issue/HSE-230` is accepted.

### TC.TG.02 — `SessionTagParserTests` (invalid input) — FR.24.4, FR.24.10 (UC.TG.02, UC.TG.03)

- **TC.TG.02-1:** each of `foo`, `HSE`, `HSE-`, `#215`, `windows-app#215`, `http://example.com`, `https://github.com`, `https://github.com/onlyowner`, `https://linear.app/hardstop`, `https://gitlab.com/o/r` is invalid (not a Linear issue, GitHub pull request or repository).
- **TC.TG.02-2 Unknown team:** `ABC-123` with no map entry is distinct from invalid: the parser reports the unknown team `ABC`.
- **TC.TG.02-3:** an empty or whitespace entry is ignored (no result, no line).
- **TC.TG.02-4 Look-alike host:** `https://github.com.evil.io/o/r` and `https://evil.io/github.com/o/r` are invalid.
- **TC.TG.02-5 PR number:** `o/r#0` and `o/r#abc` are invalid; `o/r#215` is valid.

### TC.TG.03 — `SessionTagParserTests` (name token) — FR.24.2 (UC.TG.04)

Pattern `(?<![A-Za-z0-9])([A-Z][A-Z0-9]{0,9})-(\d{1,6})(?![A-Za-z0-9])`.
- **TC.TG.03-1 Matches:** `HSE-150 Android backend` → `HSE-150`; `W14 HSE-165 rebase` → `HSE-165`; `(HSE-150)`, `[HSE-150]`, `HSE-150:`, `HSE-150.` match.
- **TC.TG.03-2 Several:** `HSE-150 and ABC-9, HSE-151` → `HSE-150`, `ABC-9`, `HSE-151` in order of appearance.
- **TC.TG.03-3 No match:** `xHSE-150`, `HSE-1234567`, `HSE-150a`, `hse-150` (lower case), `H-1` is a match but `1HSE-5` is not; `ABCDEFGHIJK-1` (11 chars) is not.
- **TC.TG.03-4 Dedupe:** `HSE-150 HSE-150` yields one token.

### TC.TG.04 — `SessionTagServiceTests` (workspace map) — FR.24.3 (UC.TG.01, UC.TG.03)

- **TC.TG.04-1 Learn and persist:** adding `https://linear.app/hardstop/issue/HSE-230` writes `linear-workspaces.json` with `HSE → hardstop`; a new service instance on the same root resolves `HSE-231` to `https://linear.app/hardstop/issue/HSE-231`.
- **TC.TG.04-2 Last wins:** learning `https://linear.app/other/issue/HSE-1` replaces `HSE → hardstop` with `HSE → other`; subsequent short keys use `other`. (Tags already stored keep the URL they were stored with, or follow the map: assert per the implementation and record in the report.)
- **TC.TG.04-3 Unknown team rejected:** a short key with no map entry is rejected (no tag, no map change).
- **TC.TG.04-4 Event on a new team:** learning a team key not yet in the map raises `SessionTagsChanged` with `NodeId == null`; re-learning an identical pair raises none.
- **TC.TG.04-5 Corrupt map file:** `linear-workspaces.json` containing `{ not json` is moved to `linear-workspaces.json.bad-<yyyyMMddHHmmss>` with a warning, and the map starts empty.
- **TC.TG.04-6 Atomic write:** after a save the folder holds no `.tmp` leftover.

### TC.TG.05 — `SessionTagStoreTests` — FR.24.5 (UC.TG.12)

- **TC.TG.05-1 Round trip:** save declared tags for a node (all fields including `AddedUtc`); a fresh store on the same root loads them equal.
- **TC.TG.05-2 Missing file:** load returns no tags; no file is created by the load.
- **TC.TG.05-3 Corrupt file:** a file with `{ not json` → load returns no tags; the original is moved to `<NodeId>.json.bad-<yyyyMMddHHmmss>` (pattern matched, content unchanged); a warning is logged; the next save writes a fresh `<NodeId>.json` and leaves the `.bad-*` file alone.
- **TC.TG.05-4 Atomic write:** after a save only `<NodeId>.json` exists in the folder (no temp file); a failing rename leaves the previous file intact and loadable.
- **TC.TG.05-5 Temp-dir root and keying:** the store uses the root from its constructor and creates the folder if missing; two nodes produce two files and do not see each other's tags.
- **TC.TG.05-6 Lazy load, then cache:** the file is read once; deleting it on disk afterwards does not change what the service returns in the same run. Tessyn itself never deletes a tag file.
- **TC.TG.05-7 Serialised writes:** 20 concurrent adds on one node leave a valid file with 20 distinct tags.

### TC.TG.06 — `SessionTagServiceTests` (add, already tagged, remove) — FR.24.2, FR.24.4 (UC.TG.01, UC.TG.05)

- **TC.TG.06-1 Add:** a new key is stored with source `Session` and `AddedUtc` ≈ now; `GetTags` returns it.
- **TC.TG.06-2 Already tagged (declared):** adding the same key (any case or form: URL vs short) reports already tagged, changes nothing, `AddedUtc` unchanged.
- **TC.TG.06-3 Already tagged (name-derived):** with the name `HSE-150 …`, adding `HSE-150` reports already tagged and stores nothing.
- **TC.TG.06-4 Remove declared:** removes it; second removal reports not tagged.
- **TC.TG.06-5 Remove name-derived:** refused with the "comes from the session name" outcome; nothing changes.
- **TC.TG.06-6 Remove invalid:** an entry that parses to nothing reports the invalid outcome.
- **TC.TG.06-7 Order of processing:** `add: [X]`, `remove: [X]` in one call leaves X untagged (add first, then remove).

### TC.TG.07 — `SessionTagServiceTests` (name-derived) — FR.24.2, FR.24.3 (UC.TG.04)

- **TC.TG.07-1 From name:** map `HSE → hardstop`; name `HSE-150 Android backend` → one tag: LinearIssue, `HSE-150`, source `Name`, `AddedUtc == null`, URL `https://linear.app/hardstop/issue/HSE-150`.
- **TC.TG.07-2 Several keys:** `HSE-150 and HSE-151` → two Name tags.
- **TC.TG.07-3 Unknown team ignored:** `KVM-1 DNS-01 HSE-150` → only `HSE-150`.
- **TC.TG.07-4 Rename changes them:** `GetTags(nodeId, "HSE-165 W14 rebase")` → `HSE-165` only; nothing is persisted (the tag file is unchanged and no tag file is created for a name-only node).
- **TC.TG.07-5 Not a source:** nothing in conversation text creates tags; the service has no text-scanning API (assert via the public surface: only `Add` and name-derivation).
- **TC.TG.07-6 Team learned later:** name `ABC-9 spike` yields nothing; after a full URL for `ABC` is learned, `ABC-9` appears.
- **TC.TG.07-7 Dedupe across sources:** declared `HSE-150` and a name containing `HSE-150` → one tag, source `Name`.
- **TC.TG.07-8 After rename away:** the declared copy of `HSE-150` (if any) shows again once the name no longer contains it.

### TC.TG.08 — `SessionTagServiceTests` (ordering) — FR.24.1, FR.24.7 (UC.TG.07)

- **TC.TG.08-1:** declared repo, declared PR, declared Linear (all different times), plus a Name Linear tag → order: Name Linear, declared Linear, PR, repo.
- **TC.TG.08-2 Within a kind:** name-derived first, then declared, newest `AddedUtc` first.
- **TC.TG.08-3:** `Tags: …` in the tool result and the guidance suffix use the same order.

### TC.TG.09 — `SessionTagServiceTests` (events) — FR.24.3, FR.24.7 (UC.TG.08)

- **TC.TG.09-1:** `SessionTagsChanged(nodeId)` is raised once after an add or a remove that changed something (once per call, not once per entry).
- **TC.TG.09-2:** not raised for already-tagged, not-tagged, rejected or listing calls.
- **TC.TG.09-3:** a call that learns a new team raises `SessionTagsChanged(null)` (see TC.TG.04-4) and may also raise the node event.

### TC.TG.10 — `AgentMcpServerTagToolTests` (result lines) — FR.24.4 (UC.TG.01–UC.TG.05)

Each case asserts the full result text, one line per entry, then the final `Tags:` line.

- **TC.TG.10-1:** `add: ["https://linear.app/hardstop/issue/HSE-230"]` →
  ```
  Tagged HSE-230 - https://linear.app/hardstop/issue/HSE-230
  Tags: HSE-230.
  ```
- **TC.TG.10-2:** the same add again → `Already tagged HSE-230.` then `Tags: HSE-230.`
- **TC.TG.10-3:** `add: ["tryhardstop/windows-app#215"]` → `Tagged windows-app PR #215 - https://github.com/tryhardstop/windows-app/pull/215`; then `remove: ["tryhardstop/windows-app#215"]` → `Removed windows-app PR #215.`
- **TC.TG.10-4:** `add: ["foo"]` → `Not tagged: 'foo' - not a Linear issue, GitHub pull request or repository.`
- **TC.TG.10-5:** `add: ["ABC-123"]` (unknown team) → `Not tagged: 'ABC-123' - unknown Linear team 'ABC'; pass the full https://linear.app/<workspace>/issue/ABC-123 URL once.` Then the full URL succeeds, then `ABC-124` succeeds as a short key.
- **TC.TG.10-6:** `remove: ["owner/other"]` → `Not removed: 'owner/other' - not tagged.`
- **TC.TG.10-7:** name `HSE-150 …`, `remove: ["HSE-150"]` → `Not removed: 'HSE-150' - it comes from the session name; rename the session to drop it.`
- **TC.TG.10-8:** `remove: ["foo"]` → `Not removed: 'foo' - not a Linear issue, GitHub pull request or repository.`
- **TC.TG.10-9 Final line:** always present; labels in FR.24.7 order separated by `, ` and ended by `.`; `Tags: none.` when empty.
- **TC.TG.10-10 Entry echo:** the entry in the `Not tagged`/`Not removed` lines is the trimmed input, not a normalised form.

### TC.TG.11 — `AgentMcpServerTagToolTests` (partial success, limits, listing) — FR.24.4 (UC.TG.11)

- **TC.TG.11-1 Partial success:** `add: ["HSE-230" (known team), "foo", "owner/repo#5"]` → two `Tagged …` lines and one `Not tagged: 'foo' …` line, in input order, then the `Tags:` line; the valid ones are stored.
- **TC.TG.11-2 30 entries accepted** in `add`; **31 entries** in `add` (or in `remove`) is a tool error (`McpToolException`) and nothing is changed; 30 add + 30 remove is accepted.
- **TC.TG.11-3 Listing:** no arguments, `add: []` and `remove: []` all list the current tags: only the `Tags:` line (or `Tags: none.`), no per-entry lines, no SYSTEM entry, no event.
- **TC.TG.11-4 Blank entries ignored:** `add: ["", "  "]` behaves as a listing.
- **TC.TG.11-5 Order within a call:** `add` entries are reported before `remove` entries.
- **TC.TG.11-6 Case-insensitive entries:** `hse-230` and `TRYHARDSTOP/WINDOWS-APP` are accepted.

### TC.TG.12 — `AgentMcpServerTagToolTests` (SYSTEM entry) — FR.24.4 (UC.TG.01, UC.TG.05)

The entry is appended with role `Constants.SessionFile.RoleSystem`, ASCII.
- **TC.TG.12-1 Added only:** `[Tags] added HSE-230, windows-app PR #215`.
- **TC.TG.12-2 Removed only:** `[Tags] removed windows-app`.
- **TC.TG.12-3 Both:** `[Tags] added HSE-230, windows-app PR #215; removed windows-app`.
- **TC.TG.12-4 Exactly one entry per call** that changed something, with labels (not URLs or keys).
- **TC.TG.12-5 None** for a listing, an all-"Already tagged" call, an all-rejected call and a "Not removed" only call.
- **TC.TG.12-6 Rejected entries are not named** in the entry; only the applied ones are.

### TC.TG.13 — `AgentMcpServerTagToolTests` (workers, description) — FR.24.4, FR.24.6 (UC.TG.10)

- **TC.TG.13-1 Worker allowed:** with `OrchestrationDepth = 1`, `add: ["tryhardstop/windows-app#215"]` stores the tag on the worker's node, returns the normal `Tagged …` line and writes the SYSTEM entry (contrast `post_attention`, which is refused).
- **TC.TG.13-2:** the tool does not return the "Not posted - worker sessions…" text for workers.
- **TC.TG.13-3 Description:** the tool's description equals the FR.24.4 wording (`Tag this session with the Linear issues, GitHub pull requests and GitHub repositories it works on. …Call with no arguments to list the current tags.`), and the schema has `add` and `remove` (string arrays, ≤ 30).

### TC.TG.14 — `InstructionBlockBuilderTests` (guidance gate) — FR.24.6 (UC.TG.09)

- **TC.TG.14-1:** `IsAgentToolsEnabled && !IsWorkerSession && IsTagGuidanceDue` → the block contains `Constants.Instructions.SessionTagGuidance` (with `mcp__cmx__tag_session` built from `Agent.McpServerName`).
- **TC.TG.14-2:** each of `IsAgentToolsEnabled = false`, `IsWorkerSession = true`, `IsTagGuidanceDue = false` → absent.
- **TC.TG.14-3 Not gated on `IsNewSession`:** present with `IsNewSession = false` (long-running session) and with `true`.
- **TC.TG.14-4 Plumbing:** `InstructionOptionsModel` exposes `IsTagGuidanceDue` (default false) and `SessionTags` (default null); with defaults the block is unchanged versus before FR.24.
- **TC.TG.14-5 Not consumed when unusable:** with agent tools off or a worker, `TakeTagGuidanceDue` is not called (a fake service counts calls).

### TC.TG.15 — `SessionTagServiceTests` (once per run) — FR.24.6 (UC.TG.09)

- **TC.TG.15-1:** `TakeTagGuidanceDue(node)` returns true on the first call and false on every later call for the same node.
- **TC.TG.15-2:** a different node still returns true on its first call.
- **TC.TG.15-3 Per run:** the state is in memory only; a new service instance (a new app run) returns true again for the same node.
- **TC.TG.15-4:** thread-safe: 20 concurrent calls for one node return exactly one true.

### TC.TG.16 — `InstructionBlockBuilderTests` (text, suffix, placement) — FR.24.6 (UC.TG.09)

- **TC.TG.16-1 Text:** equals the FR.24.6 wording.
- **TC.TG.16-2 No tags:** no `Current tags:` suffix.
- **TC.TG.16-3 With tags:** `… remove a tag only if it was added by mistake. Current tags: HSE-150, HSE-230, windows-app PR #215.` in Tag-row order (name-derived included).
- **TC.TG.16-4 Placement:** with attention guidance (FR.23.10) and Telegram guidance both present, the order is attention guidance, then tag guidance, then Telegram guidance.
- **TC.TG.16-5 Worker block** contains none of the tag guidance.

### TC.TG.17 — `SessionTagUrlPolicyTests` — FR.24.7 (UC.TG.06)

`IsOpenable(url)`:
- **Allowed (true):** `https://linear.app/hardstop/issue/HSE-230`, `https://github.com/tryhardstop/windows-app/pull/215`, `https://github.com/o/r`, `https://LINEAR.APP/x` (host case).
- **Rejected (false):** `http://linear.app/x`, `http://github.com/o/r`, `https://github.com.evil.io/o/r`, `https://evil.io/github.com`, `https://linear.app.evil.io/x`, `https://www.github.com/o/r` (host must be exactly `github.com`), `https://evil.io@github.com.evil.io/`, `https://github.com@evil.io/`, `file:///C:/Windows/System32/calc.exe`, `ftp://github.com/o/r`, `javascript:alert(1)`, `/relative/path`, `github.com/o/r` (no scheme), `""`, whitespace, `null`.
- **TC.TG.17-1:** theory of the allowed list; **TC.TG.17-2:** theory of the rejected list.

### TC.TG.18 — `InProcDaemonClientTests` — FR.24.8 (UC.TG.08)

- **TC.TG.18-1 `GetSessionTags(nodeId)`:** resolves the node and its current name through the registry and returns the service's name-derived + declared tags in FR.24.7 order; an unknown node returns an empty list.
- **TC.TG.18-2 Rename:** after the registry name changes, `GetSessionTags` reflects the new name-derived tags.
- **TC.TG.18-3 `TakeTagGuidanceDue`:** delegates; true once per node.
- **TC.TG.18-4 Relay:** raising the service's `SessionTagsChanged` (with a node id, and with `null`) raises `IDaemonClient.SessionTagsChanged` with the same `NodeId`, as `EntryAppended` and `AttentionChanged` are relayed.
- **TC.TG.18-5 Unsubscribe:** after dispose no more relays.
- **TC.TG.18-6 Wire client:** `WireDaemonClient.GetSessionTags` and `TakeTagGuidanceDue` throw `NotSupportedException`; its `SessionTagsChanged` event never fires.

---

## Manual UI tests (Avalonia)

Common set-up: a top-level session with agent tools enabled. Create tags by asking the session "tag this session with <urls> using tag_session", or by calling the tool from an MCP test client. Name-derived tags need `HSE → hardstop` learned (tag any `https://linear.app/hardstop/issue/HSE-…` URL once).

### TC.TG.19 — Tag row placement and visibility — FR.24.7 (UC.TG.07)

1. Open a session with no tags. **Expected:** the header shows the DockPanel row (name and location, search box, Session/Focus switch, MD, JSONL) and **no** extra row or empty space below it.
2. Have the session tag `https://linear.app/hardstop/issue/HSE-230`. **Expected:** a row appears directly **below** the header row, inside the same bordered header, so the header's bottom rule is under the chips; the chip reads `HSE-230`.
3. Remove the tag (`remove: ["HSE-230"]`). **Expected:** the row disappears and the space is reclaimed.
4. Open a different session. **Expected:** its own tags only.

### TC.TG.20 — Wrapping — FR.24.7 (UC.TG.07)

1. Tag 12 PRs/issues/repos in one session. **Expected:** the chips flow left to right and wrap onto further lines; the header grows taller; nothing is clipped or overlaps the output area.
2. Narrow the window. **Expected:** chips re-wrap.
3. Remove all but two. **Expected:** the row shrinks to one line.

### TC.TG.21 — Chip look and tooltip — FR.24.7 (UC.TG.06, UC.TG.07)

1. Inspect a chip. **Expected:** rounded (radius ≈ 9), 1 px border in the theme's low foreground colour, padding ≈ 8,1, font size 11, neutral colours; check dark and light theme.
2. Hover a chip. **Expected:** hand cursor; a subtle hover highlight.
3. Hover a declared tag. **Expected:** tooltip line 1 is the URL; line 2 is `Tagged by the session HH:mm` for a tag added today or `Tagged by the session yyyy-MM-dd HH:mm` for an older one (local time).
4. Hover a name-derived tag. **Expected:** line 1 the URL, line 2 `From the session name`.
5. Labels: Linear `HSE-230`; PR `windows-app PR #215`; repo `windows-app`.

### TC.TG.22 — Click opens the browser — FR.24.7 (UC.TG.06)

1. Click the `HSE-230` chip. **Expected:** the default browser opens `https://linear.app/hardstop/issue/HSE-230`.
2. Click `windows-app PR #215`. **Expected:** `https://github.com/tryhardstop/windows-app/pull/215` opens.
3. Click a repository chip. **Expected:** `https://github.com/tryhardstop/windows-app` opens.
4. (Policy) Edit the session's tag file `%AppData%\TessynDesktop\tags\<NodeId>.json` with the app closed so one URL is `http://github.com/o/r` (or `https://evil.io/github.com`), start the app and click that chip. **Expected:** nothing opens, the app stays responsive, and the log records the ignored URL. Restore the file afterwards.

### TC.TG.23 — Chip order — FR.24.1, FR.24.7 (UC.TG.07)

1. Tag, in this order: a repository, a PR, an issue `HSE-230`, a second PR (newer), a second issue `HSE-231` (newer). Name the session "HSE-150 spike".
2. **Expected order:** `HSE-150` (name-derived), `HSE-231`, `HSE-230` (declared, newest first), then the PRs newest first, then the repository.
3. The `Tags:` line in the tool's result shows the same order.

### TC.TG.24 — Live refresh on `tag_session` — FR.24.7, FR.24.8 (UC.TG.08)

1. Keep the session open and visible. Have it tag a new PR. **Expected:** the chip appears within about a second, without switching views or reopening the session.
2. Have it remove the tag. **Expected:** the chip disappears live.
3. Have it call `tag_session` with only "Already tagged" outcomes. **Expected:** no visible change.
4. Tag something in a **different** session. **Expected:** this session's row does not change.

### TC.TG.25 — Live refresh on rename and on learning a team — FR.24.2, FR.24.3, FR.24.7 (UC.TG.04, UC.TG.08)

1. Rename the session to "HSE-165 W14 rebase". **Expected:** the row updates at once to show `HSE-165` (and no longer `HSE-150`).
2. Rename it to a name without a key. **Expected:** the name-derived chip disappears.
3. Name session B "ABC-9 spike" (no chip, team unknown). In session A tag `https://linear.app/acme/issue/ABC-1`. **Expected:** session B's row now shows `ABC-9` without any action in B.
4. Restart the app. **Expected:** declared tags and the name-derived chips are still there (the map is persisted).

### TC.TG.26 — Visible in Session and Focus views — FR.24.7 (UC.TG.07)

1. With tags present, select Focus. **Expected:** the Tag row is unchanged, below the header.
2. Select Session. **Expected:** unchanged.
3. Click a chip while Focus is selected. **Expected:** the browser opens; the view stays on Focus.
4. Tag/untag while Focus is selected. **Expected:** the row refreshes live.

### TC.TG.27 — End to end: a session tags itself — FR.24.4, FR.24.6, FR.24.7 (UC.TG.01–UC.TG.05, UC.TG.09)

1. Restart the app; open a long-running top-level session that has tags. Send any prompt. **Expected:** that turn's instruction block (inspect the log) contains the tag guidance with ` Current tags: …`. Send a second prompt. **Expected:** no tag guidance.
2. Start a fresh session and ask it to start work on a PR. **Expected:** it calls `tag_session` with the PR URL; the chip appears; the session output shows a `[Tags] added …` SYSTEM entry.
3. Ask it to tag `ABC-123` (unknown team). **Expected:** it gets the rejection text, retries with the full URL, and the chip appears.
4. Ask it to drop a tag added by mistake. **Expected:** `Removed …`; the chip disappears.
5. Try to remove a name-derived tag. **Expected:** `Not removed: 'HSE-150' - it comes from the session name; rename the session to drop it.`

### TC.TG.28 — Phase 2 status colours — DEFERRED, not built in Phase 1 — FR.24.9 (UC.TG.13)

No test is runnable in Phase 1. When Phase 2 is approved, cases are to cover: chip colour per Linear state type (backlog, unstarted, started, completed, canceled) and PR state (open, draft, merged, closed); scheduled pull cadence and the once-on-open pull; the cached status shown in the tooltip with the last-checked time; and the chosen status source (Linear API key, Linear MCP, or session-reported; GitHub `gh` or token). Chips must stay neutral in Phase 1: **Expected now:** all chips have the same neutral colours whatever the ticket or PR status.

---

# Tags tab (FR.24.11–FR.24.14)

Related use cases: UC.TG.14–UC.TG.23. TC.TG.29–TC.TG.36 are AUTOMATED Core xUnit tests in `code/TessynDesktop.Tests`; TC.TG.37–TC.TG.47 are MANUAL UI tests; TC.TG.48 is NOT BUILT. The related Recent-tab cases are in [TC.RT.01](/docs/testcases/TC.RT.01-Recent-Tab.md).

## Summary (Tags tab)

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.TG.29 | `SessionTagSearch.Matches` | AUTOMATED | UC.TG.14 | FR.24.11 |
| TC.TG.30 | `SessionTagSearch.SessionsWithAnyTag` | AUTOMATED | UC.TG.14, UC.TG.15 | FR.24.12 |
| TC.TG.31 | `SessionTagSearch.Order` | AUTOMATED | UC.TG.15 | FR.24.11 |
| TC.TG.32 | Index builder: aggregation, dedupe, representative copy | AUTOMATED | UC.TG.14, UC.TG.17 | FR.24.13 |
| TC.TG.33 | Index builder: Worker Session roll-up | AUTOMATED | UC.TG.17 | FR.24.12, FR.24.13 |
| TC.TG.34 | Index builder: scope | AUTOMATED | UC.TG.16, UC.TG.17 | FR.24.11, FR.24.13 |
| TC.TG.35 | `InProcDaemonClient.GetSessionTagIndex` | AUTOMATED | UC.TG.20 | FR.24.13 |
| TC.TG.36 | `WireDaemonClient.GetSessionTagIndex` | AUTOMATED | UC.TG.20 | FR.24.13 |
| TC.TG.37 | Tab, layout and search bar | MANUAL | UC.TG.14 | FR.1.15, FR.24.11 |
| TC.TG.38 | Search and select one tag | MANUAL | UC.TG.14 | FR.24.11, FR.24.12 |
| TC.TG.39 | Combine tags from several searches (OR) | MANUAL | UC.TG.15 | FR.24.12 |
| TC.TG.40 | Scope toggle | MANUAL | UC.TG.16 | FR.24.11 |
| TC.TG.41 | Worker Session roll-up in the UI | MANUAL | UC.TG.17 | FR.24.12, FR.24.13 |
| TC.TG.42 | Open in browser, tooltip | MANUAL | UC.TG.18 | FR.24.12, FR.24.7 |
| TC.TG.43 | Empty states | MANUAL | UC.TG.19 | FR.24.11, FR.24.12 |
| TC.TG.44 | Live refresh after `tag_session`, loading | MANUAL | UC.TG.20 | FR.24.13 |
| TC.TG.45 | Selection survives scope change and tab switch | MANUAL | UC.TG.21 | FR.24.12 |
| TC.TG.46 | Session list: look, click, collapsible groups | MANUAL | UC.TG.22 | FR.24.12, FR.1.16 |
| TC.TG.47 | Chip look, selected state, order | MANUAL | UC.TG.14, UC.TG.15 | FR.24.11, FR.24.12 |
| TC.TG.48 | Related tags (DEFERRED) | NOT BUILT | UC.TG.23 | FR.24.14 |

## FR → TC coverage (Tags tab)

| FR | Test cases |
|---|---|
| FR.24.11 | TC.TG.29, 31, 34, 37, 38, 40, 43, 47 |
| FR.24.12 | TC.TG.30, 33, 38, 39, 41, 42, 43, 45, 46, 47 |
| FR.24.13 | TC.TG.32, 33, 34, 35, 36, 41, 44 |
| FR.24.14 | TC.TG.48 (deferred) |

---

## Automated tests (Tags tab)

Helper data: a usage entry is `SessionTagUsageModel { Tag, NodeIds }`. Tags: `HSE-254` (LinearIssue, label `HSE-254`), `tryhardstop/backend#303` (GitHubPullRequest, label `backend PR #303`), `tryhardstop/windows-app` (GitHubRepository, label `windows-app`).

### TC.TG.29 — `SessionTagSearchTests.Matches` — FR.24.11 (UC.TG.14)

- **TC.TG.29-1 Single part:** `HSE-254` matches the `HSE-254` tag; `254` matches it too (label substring).
- **TC.TG.29-2 Key, not only label:** `#303` matches `backend PR #303` (key `tryhardstop/backend#303`); `tryhardstop` matches it through the key.
- **TC.TG.29-3 Several parts:** `backend 303` matches `backend PR #303`; `backend 999` does not; part order does not matter (`303 backend`).
- **TC.TG.29-4 Case:** `hse-254`, `BACKEND` match.
- **TC.TG.29-5 Repository and PRs:** `windows-app` matches the repository and every `windows-app` PR.
- **TC.TG.29-6 URL not searched:** `linear.app` and `github.com` do not match tags whose label and key lack them.
- **TC.TG.29-7 Blank:** `null`, `""` and `"   "` match every tag.
- **TC.TG.29-8 Whitespace:** leading, trailing and repeated spaces or tabs between parts are ignored.
- **TC.TG.29-9 No match:** `zzz` matches nothing.

### TC.TG.30 — `SessionTagSearchTests.SessionsWithAnyTag` — FR.24.12 (UC.TG.14, UC.TG.15)

- **TC.TG.30-1 One tag:** returns exactly the NodeIds of that tag.
- **TC.TG.30-2 OR:** two keys return the union; a NodeId carrying both appears once.
- **TC.TG.30-3 Key case:** `hse-254` selects the `HSE-254` usage; NodeIds stay ordinal (`a1` ≠ `A1`).
- **TC.TG.30-4 Unknown key:** returns an empty set.
- **TC.TG.30-5 Empty selection:** returns an empty set, even when the index is non-empty.

### TC.TG.31 — `SessionTagSearchTests.Order` — FR.24.11 (UC.TG.15)

- **TC.TG.31-1 Kind:** Linear issue, then pull request, then repository, whatever the activity or label.
- **TC.TG.31-2 Recency:** within a kind, the tag whose sessions have the newest Last Activity first (the maximum over `NodeIds`).
- **TC.TG.31-3 No activity last:** a tag whose sessions all return `null` sorts after tags with activity, within its kind.
- **TC.TG.31-4 Label tie-break:** equal recency (or both `null`) → label, case-insensitive (`alpha` before `Beta`).
- **TC.TG.31-5 Input not mutated; stable:** the same input twice gives the same order.

### TC.TG.32 — `SessionTagIndexBuilderTests` (aggregation, dedupe, representative copy) — FR.24.13 (UC.TG.14, UC.TG.17)

- **TC.TG.32-1 Dedupe:** the same key on two sessions gives one usage with two NodeIds; keys compare case-insensitively (`HSE-254` vs `hse-254`).
- **TC.TG.32-2 NodeIds distinct:** a tag carried by a session and its worker lists the supervisor once.
- **TC.TG.32-3 Representative copy, name-derived wins:** if any session's copy has source `Name`, its label and URL are used.
- **TC.TG.32-4 Representative copy, newest declared:** with only declared copies, the one with the latest `AddedUtc`.
- **TC.TG.32-5 Groups:** sessions inside Groups are included.
- **TC.TG.32-6 No tags:** an empty tree or sessions without tags give an empty index.

### TC.TG.33 — `SessionTagIndexBuilderTests` (Worker Session roll-up) — FR.24.12, FR.24.13 (UC.TG.17)

- **TC.TG.33-1 One level:** W's tag is attributed to its supervisor S; W's NodeId is not listed.
- **TC.TG.33-2 Several levels:** W2 → W1 → S attributes to S.
- **TC.TG.33-3 Missing supervisor:** `SupervisorNodeId` names a node not in the snapshot; the walk stops at the last session found, which is then used.
- **TC.TG.33-4 Cycle:** A → B → A terminates (no hang, no exception); the result is deterministic.
- **TC.TG.33-5 Worker in another directory:** the tag counts under the supervisor's directory (see TC.TG.34-4).
- **TC.TG.33-6 Worker not listed on its own:** no usage lists a worker NodeId.

### TC.TG.34 — `SessionTagIndexBuilderTests` (scope) — FR.24.11, FR.24.13 (UC.TG.16, UC.TG.17)

- **TC.TG.34-1 Path scope:** a directory path returns only tags of top-level sessions under that Directory node.
- **TC.TG.34-2 Case-insensitive:** `c:\proj\x` matches `C:\Proj\X`.
- **TC.TG.34-3 Null is all:** `null` includes every directory.
- **TC.TG.34-4 Roll-up across directories:** worker in directory Y under supervisor in X: the tag appears for scope X, not for scope Y.
- **TC.TG.34-5 Unknown path:** an empty index.

### TC.TG.35 — `InProcDaemonClientTests.GetSessionTagIndex` — FR.24.13 (UC.TG.20)

- **TC.TG.35-1 Delegation:** builds the index from the registry snapshot and `ISessionTagService.GetTags` (fake service records the calls); the result matches the builder's.
- **TC.TG.35-2 Current name:** name-derived tags use the session's current name; after a rename the index reflects it.
- **TC.TG.35-3 Scope:** a directory path scopes the result; `null` returns every directory.
- **TC.TG.35-4 Declared and name-derived agree** with `GetSessionTags` for the same node.

### TC.TG.36 — `WireDaemonClientTests.GetSessionTagIndex` — FR.24.13 (UC.TG.20)

- **TC.TG.36-1:** `GetSessionTagIndex(null)` and `GetSessionTagIndex("C:\\x")` throw `NotSupportedException` (as `GetSessionTags`, TC.TG.18-6).

---

## Manual UI tests (Tags tab)

Common set-up: directory X with top-level sessions A (tagged `HSE-254`), B (tagged `tryhardstop/backend#303`) and C (tagged `HSE-254` and `tryhardstop/windows-app`); directory Y with session D (tagged `HSE-300`). Worker Session W under A, tagged `tryhardstop/backend#304`. `HSE → hardstop` learned.

### TC.TG.37 — Tab, layout and search bar — FR.1.15, FR.24.11 (UC.TG.14)

1. Start the app. **Expected:** the left panel has three tabs in the order Tree, Recent, Tags; the app starts on Tree.
2. Open Tags. **Expected, top to bottom:** search bar (text box with watermark `Search tags…`, the 📁 toggle; no exact-phrase toggle, no refresh button), scrollable tag results, a horizontal splitter, the scrollable session list. Tag results take about a third of the height.
3. Drag the splitter to both extremes. **Expected:** neither part collapses below its minimum height.
4. Switch away and back. **Expected:** the splitter position is not kept (back to the default).
5. Restart. **Expected:** the app starts on Tree again.

### TC.TG.38 — Search and select one tag — FR.24.11, FR.24.12 (UC.TG.14)

1. Select session A; open Tags. **Expected:** 📁 on; chips for `HSE-254`, `backend PR #303`, `backend PR #304`, `windows-app`, not `HSE-300`; session list shows `Select tags to list their sessions`.
2. Type `HSE-254`. **Expected:** only the `HSE-254` chip, applied while typing.
3. Click it. **Expected:** the chip is highlighted; the list shows A and C under the X header, not B, W or D.
4. Type `#303`. **Expected:** `HSE-254` stays first, then `backend PR #303`; `backend 303` also finds it.
5. Click the `HSE-254` chip again. **Expected:** deselected; the list shows `Select tags to list their sessions`.
6. Select a tag, then click a session row. **Expected:** the session opens and is selected in the tree.

### TC.TG.39 — Combine tags from several searches (OR) — FR.24.12 (UC.TG.15)

1. Search `HSE-254`, select the chip. Search `#303`, select `backend PR #303`. **Expected:** the list shows A, B and C (OR); selected chips are first in the tag area.
2. Search `zzz`. **Expected:** both selected chips still show and the list is unchanged; no `No matching tags` text while selected chips are pinned.
3. Clear the text. **Expected:** both stay selected, listed first.
4. Deselect one. **Expected:** the list shrinks accordingly.

### TC.TG.40 — Scope toggle — FR.24.11 (UC.TG.16)

1. Select A; open Tags. **Expected:** 📁 on and enabled; only X's tags and sessions.
2. Select D (in Y). **Expected:** the tab reloads; only `HSE-300` and D.
3. Turn 📁 off. **Expected:** reload; every directory's tags.
4. Toggle the Tree tab's 📁. **Expected:** the Tags toggle does not change, and the other way round.
5. Deselect all sessions in the tree (or restart with none selected). **Expected:** 📁 off and disabled; tags of every directory.
6. Select W (a Worker Session). **Expected:** 📁 on; scope is the Directory node it is nested under (X).

### TC.TG.41 — Worker Session roll-up in the UI — FR.24.12, FR.24.13 (UC.TG.17)

1. In Tags, select `backend PR #304` (carried only by W). **Expected:** the list shows A (W's top-level supervisor), not W.
2. Hover the chip. **Expected:** `In 1 session`.
3. Click A's row. **Expected:** A is selected in the tree; W stays nested under A.
4. Tag W with a tag A also carries. **Expected:** one chip, A listed once.

### TC.TG.42 — Open in browser, tooltip — FR.24.12, FR.24.7 (UC.TG.18)

1. Hover `HSE-254` (carried by A and C). **Expected:** line 1 `https://linear.app/hardstop/issue/HSE-254`; line 2 `In 2 sessions`.
2. Hover a tag carried by one session. **Expected:** line 2 `In 1 session`.
3. Left click a chip. **Expected:** selection toggles; no browser opens.
4. Right click a chip. **Expected:** a context menu with **Open in browser**.
5. Pick it. **Expected:** the default browser opens the URL.
6. With the app closed, change a URL in a tag file to `http://github.com/o/r`; restart, right-click that chip, **Open in browser**. **Expected:** nothing opens, the app stays responsive and the log records the ignored URL. Restore the file.
7. Turn 📁 on/off. **Expected:** the counts follow the scope.

### TC.TG.43 — Empty states — FR.24.11, FR.24.12 (UC.TG.19)

1. Select a session in a directory with no tagged sessions, 📁 on. **Expected:** `No tags in this directory`.
2. Turn 📁 off in an install with no tags at all. **Expected:** `No tags yet`.
3. With tags present, type `zzz` with nothing selected. **Expected:** `No matching tags`.
4. Nothing selected. **Expected:** the session list shows `Select tags to list their sessions`.
5. Select `HSE-300`, then select a session in X (📁 on). **Expected:** `HSE-300` stays selected and shown; the list shows `No sessions with the selected tags`.

### TC.TG.44 — Live refresh after `tag_session`, loading — FR.24.13 (UC.TG.20)

1. Keep Tags selected. Have session B call `tag_session` with a new PR. **Expected:** within about a second the chip appears; counts update; the tab keeps its text and selection.
2. Remove it with `remove`. **Expected:** the chip disappears (unless it is selected, then it stays pinned).
3. Switch to Tree, have a session tag something, switch back. **Expected:** the new tag is there (reload on selection).
4. Tag a new Linear team by full URL. **Expected:** after the reload, a session named with that key shows the name-derived chip.
5. Open Tags with many sessions. **Expected:** the UI stays responsive; the spinner shows while loading.

### TC.TG.45 — Selection survives scope change and tab switch — FR.24.12 (UC.TG.21)

1. Scope on in X; select `HSE-254`. Select D in Y. **Expected:** `HSE-254` stays selected, shown first; no sessions come from it.
2. Select A again. **Expected:** A and C listed again.
3. Turn 📁 off. **Expected:** unchanged selection; same sessions.
4. Switch to Tree and back. **Expected:** the selection is kept.
5. Restart the app. **Expected:** nothing is selected (not persisted).

### TC.TG.46 — Session list: look, click, collapsible groups — FR.24.12, FR.1.16 (UC.TG.22)

1. With `HSE-254` and `HSE-300` selected (📁 off). **Expected:** the list looks like the Recent tab: a header per Directory node (📂, label, chevron ▾), drafts first then newest first, rows with status dots, name, ✏, star, profile name, last prompt time, recency background.
2. Click header X. **Expected:** it collapses (chevron ▸); click again expands.
3. Collapse X, then change the selection. **Expected:** X stays collapsed.
4. Switch to Tree and back. **Expected:** the groups are not collapsed because of the switch; the manual state stays for the app run.
5. Click row A, select another session in the tree, click row A again. **Expected:** A is selected each time.

### TC.TG.47 — Chip look, selected state, order — FR.24.11, FR.24.12 (UC.TG.14, UC.TG.15)

1. Compare a chip in the Tags tab with one in a session's Tag row. **Expected:** the same look (shared style), in both themes; the Tag row is unchanged.
2. Select a chip. **Expected:** accent background with readable text in dark and light themes; hovering it keeps the selected state visible.
3. Order: select `windows-app` (repo) and `HSE-254`. **Expected:** selected first (`HSE-254`, `windows-app`), then the others by kind, newest activity, label.
4. Make a session newer than the others. **Expected:** after the next reload its tags move up within their kind.

### TC.TG.48 — Related tags (DEFERRED) — NOT BUILT — FR.24.14 (UC.TG.23)

No test is runnable. When the phase is approved, cases are to cover: selecting a Linear issue also suggests its pull requests (and a PR its issue) as extra chips, per the chosen source (co-occurrence, GitHub search, or Linear's GitHub integration). **Expected now:** selecting a chip adds no extra chips.

---

## Summary (tag search tools, FR.24.15)

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.TG.49 | `search_tags`: rows, order, scope, roll-up | AUTOMATED | UC.TG.25 | FR.24.15, FR.24.11, FR.24.13 |
| TC.TG.50 | `search_tags`: output text | AUTOMATED | UC.TG.25 | FR.24.15 |
| TC.TG.51 | `search_tags`: arguments and tool surface | AUTOMATED | UC.TG.25 | FR.24.15 |
| TC.TG.52 | `list_sessions` `tags` filter | AUTOMATED | UC.TG.24 | FR.15.13, FR.24.15, FR.24.12 |
| TC.TG.53 | Cost and lock | AUTOMATED | UC.TG.24, UC.TG.25 | FR.24.15, FR.15.13 |
| TC.TG.54 | Live check against the Tags tab | MANUAL | UC.TG.24, UC.TG.25 | FR.24.15 |

Tests are Core xUnit tests calling `AgentMcpServer.HandleToolCallAsync` with a real `SessionRegistry` over a temp settings dir (the style of `AgentMcpServerListSessionsTests` and `AgentMcpServerTagToolTests`), plus pure tests for any new builder or formatter. Helper data as in TC.TG.29.

### TC.TG.49 — `search_tags`: rows, order, scope, roll-up — FR.24.15, FR.24.11, FR.24.13 (UC.TG.25)

- **TC.TG.49-1 Project scope (default):** only tags of top-level sessions under the caller's Directory node; for a Worker Session caller, the Directory node it is nested under.
- **TC.TG.49-2 All:** `scope=all` returns the tags of every directory.
- **TC.TG.49-3 Matching is FR.24.11's:** `backend 303` matches `backend PR #303`; `#303` matches through the key; `HSE` is case-insensitive; `linear.app` matches nothing (URL not searched); blank `query` matches all.
- **TC.TG.49-4 Order:** kind first, then the newest Last Activity (session file last-write time) among the carriers, then label — the same result as `SessionTagSearch.Order` for the same data.
- **TC.TG.49-5 Roll-up:** a tag declared only by a Worker Session is counted for its top-level supervisor; the row lists the supervisor's short id, not the worker's.
- **TC.TG.49-6 Carriers:** newest Last Activity first, at most 5 ids, then `+<k>`; `1 session:` for one, `<n> sessions:` otherwise.
- **TC.TG.49-7 Representative copy:** label and URL are the index's (FR.24.13).

### TC.TG.50 — `search_tags`: output text — FR.24.15 (UC.TG.25)

- **TC.TG.50-1 You line:** identical to `list_sessions`' for the same caller.
- **TC.TG.50-2 Header:** `Tags in <label> (<path>) - <n> tags` (project), `Tags in all directories - <n> tags` (all); with a query `, matching "<query>"` is appended.
- **TC.TG.50-3 Row:** `<label>  <url>  <n> sessions: <id>, <id>` with Short Node Ids.
- **TC.TG.50-4 Footer:** `Rows <a>-<b> of <n>.`, `Next page: offset=<b>.` only when rows remain; hints `Sessions: list_sessions tags=["<url or key>"].` always and `Other projects: scope=all.` only in project scope.
- **TC.TG.50-5 Empty states:** `No tags in this directory` (project, none at all), `No tags yet` (all, none at all), `No matching tags` (otherwise); `No rows at offset=<o>; <n> in total.` for an offset past the end.
- **TC.TG.50-6 Text rules:** a label with a line break stays on one row; the tool's own text is ASCII.

### TC.TG.51 — `search_tags`: arguments and tool surface — FR.24.15 (UC.TG.25)

- **TC.TG.51-1 tools/list:** `search_tags` is listed with the FR.24.15 description wording and the arguments `query`, `scope` (enum `project`, `all`), `limit`, `offset`.
- **TC.TG.51-2 Scope parsing:** `galaxy`, `2` and `project,all` are tool errors; ` ALL ` works.
- **TC.TG.51-3 Paging:** `limit` clamped to 1–200; a negative `offset` counts as 0; wrong JSON types are tool errors naming the argument.

### TC.TG.52 — `list_sessions` `tags` filter — FR.15.13, FR.24.15, FR.24.12 (UC.TG.24)

- **TC.TG.52-1 Forms:** the Linear URL, `HSE-254`, a PR URL, `owner/repo#303` and `owner/repo` each keep exactly the carrying sessions.
- **TC.TG.52-2 OR:** two tags list the sessions carrying either.
- **TC.TG.52-3 Roll-up:** a top-level session that carries a tag only through its Worker Session is listed; with `includeWorkers` its workers are nested under it; a worker of a non-carrier is not listed.
- **TC.TG.52-4 Combines:** with `scope=all` a carrier in another directory is listed, with the default project scope it is not; `query`, `running` and paging apply on top.
- **TC.TG.52-5 Unknown Linear team:** `ABC-12` (team not in the workspace map) filters by its key and does not change the map.
- **TC.TG.52-6 Errors:** `xyz` gives `Not a tag: 'xyz' - not a Linear issue, GitHub pull request or repository.`; more than 30 entries is a tool error; an empty array or blank entries mean no filter.
- **TC.TG.52-7 No carrier:** `No sessions match.` with the usual hints.

### TC.TG.53 — Cost and lock — FR.24.15, FR.15.13 (UC.TG.24, UC.TG.25)

- **TC.TG.53-1 Lock:** while a fake `ISessionTagService.GetTags` runs for `search_tags` and for `list_sessions` with `tags`, another thread acquires `SyncRoot` within a timeout.
- **TC.TG.53-2 No content reads:** neither tool reads session-file contents (fake file service counts `ReadEntries`).

### TC.TG.54 — Live check against the Tags tab — MANUAL — FR.24.15 (UC.TG.24, UC.TG.25)

1. In a session, call `search_tags` with the same text as typed in the Tags tab, 📁 on. **Expected:** the same tags in the same order as the chips (none selected).
2. Repeat with `scope=all` against 📁 off. **Expected:** the same.
3. Call `list_sessions` with `tags=[<url of a tag>]` and `scope=all`. **Expected:** the same top-level sessions the Tags tab lists for that one selected tag (📁 off).
