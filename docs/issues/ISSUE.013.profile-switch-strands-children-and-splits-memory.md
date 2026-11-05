# ISSUE.013: Switching a session's profile strands its children, splits project memory and changes tools mid-conversation

## Summary

On 2026-11-03 the user switched a long-running orchestrator session from one Tessyn profile
(Claude account A) to the Default profile (a different claude.ai account). Only the session's own
transcript moved. Its spawned workers stayed pinned to the old profile, the Claude Code auto-memory
(101 files of project rules and history) stayed in the old profile's config dir, the system prompt
kept naming the old memory path, and the MCP connector set changed under the running conversation.
Within an hour a second switched session wrote a new memory file into the (empty) Default-profile
memory folder, so project memory split in two. Today the user has to repair each of these by hand.

Profile is currently a property of *one session turn*, not of a session tree or a project. Everything
that is shared by design (workers, project memory, connectors) is stored per profile, so a switch
silently breaks the sharing.

## What happened (2026-11-03, incident record)

1. **Switch.** Orchestrator session `a71cfe6d-…` was switched from `profile_11` to the Default
   profile. Its Claude Code transcript (`<slug>/a71cfe6d-….jsonl`) ended up under `~/.claude`. A
   listing check afterwards: the `a71cfe6d-…` session *folder* (tool results etc.) exists under
   **both** profiles' `projects/<slug>/`; the `.jsonl` is only under Default.
2. **Children stranded.** Workers created earlier with `spawn_session` stayed on `profile_11`. The
   user's words: the orchestrator "won't be able to continue with your subagents unless I step in
   and switch their profiles too." Workaround: the orchestrator asks the user (Telegram + chat) to
   switch the child and paste the text. The user is moving all work to the new profile ("we'll be
   using [it] starting from now on"), so a child left on `profile_11` keeps running on the old
   account even where a resume works technically (see Root cause). A peer orchestrator still on `profile_11` exchanges messages
   with this one via `send_to_session`; which profile a resumed target uses is undefined from the
   caller's side.
3. **Memory per profile.** Auto-memory lives at `<config dir>\projects\<slug>\memory\`. After the
   switch the Default folder was empty and the harness reported the old `MEMORY.md` as "no longer
   present". The 101-file memory stayed in `profile_11`, where other sessions kept writing. Another
   switched session then created a new file plus a 1-line `MEMORY.md` in the empty folder.
   Concrete risk: the same day the harness asked the session to add a `Co-Authored-By` trailer to a
   commit; the lost memory forbids it.
4. **Stale path in the prompt.** The switched session's system prompt still named `profile_11`'s
   memory directory while auto-load used the new profile's folder.
5. **Account and tools change.** The new profile is bound to a different claude.ai account, so the
   connector set changed mid-conversation: an SSH-servers connector and two analytics connectors
   disconnected, others appeared under different names, three connectors need authorization.
6. **Spawn refused.** `spawn_session` with `workingDir` = the Tessyn repo failed with
   "Could not create session under working dir"; a directory of another repo worked.
7. Related friction: see Appendix.

## Impact

| # | Impact |
|---|---|
| 1 | Low if migration is complete; today only the `.jsonl` is moved, the session folder is duplicated/orphaned and no absolute path inside is checked. |
| 2 | High. Delegation model breaks: workers hold context the orchestrator relies on (e.g. the worker that built 4 commits is the one that must fix review findings). Manual relay costs user time and a human in the loop for every follow-up. |
| 3 | High. Split-brain memory; owner rules (no Claude co-author trailer, test-machine rules, product scope) silently not loaded. Hard to notice, hard to merge later. |
| 4 | Medium. Model is told one memory path, harness uses another; writes can land in the wrong store. |
| 5 | Medium. A task in flight can lose a tool or find it under a new name, with no warning. |
| 6 | Low (workaround exists) but the error text does not say why. |

## Root cause (as far as the code shows)

Facts verified in `code/` (these refine the incident record where noted):

- **Profile = display name recorded per user turn.** `TurnController` writes the selected profile's
  display name (the account label) into each user entry and onto the node
  (`TessynDesktop/ViewModels/TurnController.cs:408-423`). `SessionTurnService.ResolveProfileName`
  returns the pinned `node.ProfileName` else the last profile in the session's own file history
  (`TessynDesktop.Core/Services/SessionTurnService.cs:456-466`); `ResolveProfileConfigDir` maps that
  name to a config dir and **silently falls back to Default if the name no longer matches**
  (`SessionTurnService.cs:474-488`).
- **Children are pinned at spawn and never re-pinned.** `InheritSupervisorProfile` copies the
  supervisor's profile into `child.ProfileName` once (`TessynDesktop.Core/Services/AgentMcpServer.cs:187-196`,
  designed in ISSUE.002, whose "Notes" already say: later supervisor changes do not reach
  children). Nothing iterates a node's descendants when a profile changes: there is no cascade.
  `SessionContentViewModel.cs:381/455` and `TurnController.cs:423` only update the node being used.
- **Contradiction with the incident record (item 2).** `send_to_session` resumes a child through
  the same `SessionTurnService` path, which resolves the child's *pinned* profile
  (`SessionTurnService.cs:117`), i.e. `profile_11`. The code has no rule that blocks a cross-profile
  resume, and it does not need the user to switch the child. So the child *should* resume on its
  old profile as long as `profile_11` still exists and is logged in. Why the user and orchestrator
  believed otherwise is **not established by the code** (candidates: `profile_11` is being retired
  or its token was revoked; `ResolveProfileConfigDir` fell back to Default after a rename/removal;
  a live streaming process; or the claim is a tool-visible symptom we did not capture). Needs
  reproduction (see Open questions).
- **Migrate-on-resume exists, but only for the transcript.** `TryAdoptSessionFromOtherProfile`
  (`Services/ClaudeSessionStatusService.cs:39-75`, FR.11.13) `File.Move`s the single
  `<slug>/<id>.jsonl` into the active profile's store. It does not touch the sibling
  `<id>\` folder, memory, or any absolute path stored in the transcript. It is called from
  `SessionTurnService.cs:161-169` only when a turn runs. `list_sessions` marks a session
  `resumable` if the jsonl exists under *any* profile (`SessionRegistry.cs:588-589`), so it cannot
  tell the caller which profile the resume will use.
- **Memory is entirely Claude Code's, not Tessyn's.** `grep` finds no auto-memory handling in
  `code/`. The CLI derives `<CLAUDE_CONFIG_DIR>/projects/<slug>/memory` from the env var Tessyn sets
  per process (`Services/ClaudeProcessLauncher.cs:121`), so memory is per profile by construction.
- **Stale prompt path (item 4), likely cause.** A streaming-mode process is spawned once per node
  with its `ProfileConfigDir` fixed (`ClaudeStreamingSessionManager.cs:49`, `IsAlive`/`Stop` at
  90-114); a later profile change does not restart it. The running CLI keeps the config dir and
  system prompt it started with, while the next-turn launch (or the UI) uses the new one. Not
  yet reproduced; the one-shot path rebuilds the prompt on every launch.
- **Connector set** comes from the claude.ai account of the profile's credentials (and Tessyn's own
  `--mcp-config`); Tessyn does not compare before/after (`AgentMcpServer.cs` builds only the cmx +
  registry entries).
- **Spawn refusal (item 6).** `SessionRegistry.CreateSessionForAgent` returns `null` unless the
  working dir equals, or is prefixed by, a directory already registered in the Tessyn tree
  (`Services/SessionRegistry.cs:377-398`); `SpawnSessionAsync` turns `null` into the generic
  "Could not create session under working dir" (`AgentMcpServer.cs:590-593`). So the Tessyn repo
  was simply not a registered tree directory for that instance (not verified against the user's
  settings file). This looks intended (sessions must live under a project node) but the message is
  misleading, and the tool schema (`AgentMcpServer.cs:1153`) does not mention the rule. Also note
  the prefix match is a raw `StartsWith` with no path-separator check, so `C:\a\b` would match
  `C:\a\bc`.

## Proposed changes

**R1. Switching a profile is a tree operation.** When the user (or an MCP caller) changes the profile
of a session that has descendants (`SupervisorNodeId` chain), show a dialog: "Also switch N
descendant sessions?" with the list (name, running/idle, current profile). Default = yes for idle
descendants; running ones are listed and queued, not interrupted. Re-pin `child.ProfileName` and
migrate each (R3). The choice "switch only this session" stays available and is recorded.

**R2. Cross-profile resume semantics.** `send_to_session` / `list_sessions` must state the profile
the target will run on. A target is resumed on its own pinned profile (today's behaviour) unless it
is unusable (profile missing, logged out): then return a clear, actionable error
("child X is pinned to profile P, which is logged out/removed; call `migrate_session` to move it to
your profile or re-authenticate P"), never a silent Default fallback. Add an optional
`migrate: true` on `send_to_session`, or a `migrate_session` tool restricted to the caller's own
descendants, that performs R3 in one step. The silent Default fallback in `ResolveProfileConfigDir`
becomes an error when a *pinned* name does not resolve.

**R3. Complete migration on switch.** Moving a session between profiles moves: the transcript jsonl,
the `<id>\` session folder (merge if it exists in both), and records the new config dir; then scans
the transcript for absolute paths under the old config dir (tool-results, memory paths) and reports
or rewrites them. Move, not copy, as in FR.11.13, and with a log line listing what moved.

**R4. Project memory keyed by project, not profile.** Tessyn owns a shared memory store per project
slug (e.g. `%AppData%\TessynDesktop\shared\<slug>\memory`) and links each profile's
`projects\<slug>\memory` to it (directory junction on Windows, symlink elsewhere) the first time a
profile is used in that project. If a profile already has a real folder:
- identical or empty: replace by link;
- different: no-loss merge. Copy non-conflicting files; for conflicts keep both
  (`name.<profile>.md`), union the `MEMORY.md` index lines, keep pre-merge backups, and show the
  user a summary before applying.
This is the manual fix already applied (see Workarounds) made automatic and safe. Opt-in per
project, since some users will want separate memory per account.

**R5. Show profile/account per session.** Tree node badge and tooltip with profile name and
(masked) account; `list_sessions` output gains `profile=<name>` and `depth`/`parent` (depth/parent is covered by FR.15.13's nesting and `worker of` flag; the profile column is still open). Warn on the
tree when a supervisor and its descendants are on different profiles.

**R6. Connector/tool diff on switch.** Before confirming a switch, list the connector set of the
target profile versus the current one (disconnected / new / renamed / needs authorization), plus
Tessyn's own `--mcp-config` entries. After the switch inject one system note into the session
("tools changed: lost X, Y; new: Z") so the model does not keep calling vanished tools.

**R7. Regenerate the prompt's memory path.** On a profile change, stop any live streaming process
for the node so the next launch builds its system prompt with the new `CLAUDE_CONFIG_DIR`
(`ClaudeStreamingSessionManager.Stop`); with R4 the path would be identical anyway.

**R8. `list_sessions` filters and paging.** Parameters: `parentNodeId`/`tree` (descendants of a
node, default = caller's own subtree), `group`, `running`, `resumable`, `profile`, `limit`/`cursor`;
default output bounded (for example caller's subtree, max 100 rows). See Appendix. Split out and specified as FR.15.13 (2026-11-03): default scope is the caller's project with the caller's own workers nested under its row, not the caller's subtree.

**R9. Spawn error clarity.** Replace the generic message with: "'<dir>' is not under a project in
the Tessyn tree. Registered: …. Add it in Tessyn or use `parentNodeId`." Document it in the
`workingDir` schema text. Fix the `StartsWith` prefix check to respect path separators. Decide
whether `spawn_session` may register a new directory (open question).

## Acceptance criteria

1. **Cascade.** Given orchestrator O with idle children C1, C2 (pinned to P1), when O is switched
   from P1 to P2 and the user accepts the descendant prompt, then C1 and C2 have `ProfileName=P2`,
   their jsonl and session folder are under P2's store, nothing remains under P1, and
   `send_to_session C1` succeeds without user action.
2. **Decline.** When the user declines the cascade, `list_sessions` shows O on P2 and C1/C2 on P1;
   `send_to_session C1` runs on P1 and the result says so.
3. **Dead profile.** Given a child pinned to a logged-out/removed profile, `send_to_session` returns
   the actionable error naming the profile and the migrate option, and does not run on Default.
   With `migrate: true` it moves the child to the caller's profile and runs.
4. **Memory.** Given P1 has 101 memory files and P2 has none, when a session in project X first
   runs under P2 with shared memory enabled, then P2's memory folder resolves to the shared store
   with 101 files and the same `MEMORY.md`. A write from either profile appears for both.
5. **Merge.** Given P1 and P2 both have memory with one same-named differing file, enabling sharing
   keeps both versions, union-merges the index, leaves backups, and loses no file.
6. **Tool diff.** Switching to a profile with fewer connectors shows the diff before confirm and
   appends the "tools changed" note to the session afterwards.
7. **Prompt path.** After a switch with a live streaming process, the next turn's system prompt names
   the new profile's (or the shared) memory dir.
8. **Visibility.** Every session in the tree and every `list_sessions` row shows its profile.
9. **list_sessions.** With 2,000+ sessions in the tree, `list_sessions` (no args) returns only the
   caller's subtree and < 20,000 characters; `profile=` and `running=true` filters work; paging
   returns the rest. (Moved to FR.15.13 / TC.15.7.)
10. **Spawn.** `spawn_session` with an unregistered dir returns the explanatory message; a path
    like `C:\a\bc` does not match registered `C:\a\b`.

## Workarounds in place

- The Default profile's `projects\<slug>\memory` was replaced by a **directory junction** to
  `profile_11`'s memory folder (the one new file merged first; pre-merge copies kept), so both
  profiles share one memory for that project.
- Old workers stay pinned to `profile_11` and are resumed there directly, which works by the code.
  The user chose this over a manual relay (orchestrator pings the user, user switches the child).
  New workers start on the new profile.

## Open questions

- Why exactly did the orchestrator expect `send_to_session` to old children to fail? By the code it
  runs them on their pinned profile. Reproduce: pin child to P1, switch parent to P2, send; capture
  the actual error. Was `profile_11` logged out or renamed (silent Default fallback)?
- Should shared memory be the default or opt-in per project/profile? Which wins on conflicts?
- Profile identity is the account display name (`ProfileName` = label). Should pins use `ProfileId`
  (stable, `ClaudeProfileModel.ProfileId`) so renames do not silently re-route to Default?
- May `spawn_session` register a new project directory, or must the user always add it?
- Should cascade be automatic when the caller is the supervisor (the orchestrator can switch its own
  children) or always user-initiated?
- Same-account profiles: if two profiles share an account, can R3 be skipped?

## Appendix: related friction

- `list_sessions` returned 341,807 characters / 2,677 lines (every session since March, every
  project). It exceeded the tool output limit and the caller had to grep a saved file. Cause:
  `ListSessionsAsync` formats every summary in the tree, one line each, with no filter
  (`AgentMcpServer.cs:~175-185`, `SessionRegistry.BuildAgentSessionSummaries`). See R8.
- A worker's background job dies when its turn ends (the CLI is killed after the turn), so a
  long test run was killed and left containers behind. Separate issue candidate: either keep the
  process group alive until children exit, or document and enforce "detached process" in the
  worker instructions.

Status: Open (analysis only, nothing implemented).
