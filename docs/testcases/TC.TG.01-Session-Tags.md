# TC.TG.01 — Session Tags

Related requirements: FR.24.1–FR.24.10 in [requirements.md](/docs/requirements.md). Related use cases: [UC.TG.01](/docs/usecases/UC.TG.01-Session-Tags.md).

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
