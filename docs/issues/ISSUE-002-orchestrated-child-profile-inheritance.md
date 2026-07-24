# ISSUE-002: Orchestrated/spawned child sessions fail with 401 (profile not inherited)

## Symptom

A supervisor session driven by the user works fine, but every child session it spawns
via the orchestration MCP tools (`spawn_session`, `orchestrate_parallel`,
`orchestrate_pipeline`) fails on **every** turn with:

```
401 OAuth access token has been revoked
401 Invalid authentication credentials
```

The failure is **not** model- or tier-specific — probing confirmed it reproduces with an
Opus child, a Sonnet child, and a child with no model override (Default profile). The
supervisor itself keeps working throughout.

Observed 2026-07-15 on session "Research on CNN for the project", which spawned
"M2a Integration Plan (Opus)" plus two auth-probe children — all 401.

## Root Cause

Auth is selected entirely by `CLAUDE_CONFIG_DIR`, which is derived from the profile
resolved for the turn (`ClaudeProcessManager.TryStartProcess`). After ISSUE-001 was
fixed, `SessionTurnService.ResolveProfileConfigDir` resolves the profile by scanning the
**session's own file history** for the last user entry that recorded a `ProfileName`.

A freshly-spawned child session has **no history yet** at the time of its first turn, so
the scan finds nothing and returns `null` → the CLI falls back to the **Default profile**
(profile index 0). If the Default profile's OAuth token is revoked or logged out — which
is common when the user actively works under a non-default account — every child turn
401s, while the supervisor (whose history records a valid profile) authenticates fine.

In short: the profile-from-history fix (ISSUE-001) is correct for sessions that have
history, but spawned children start with none and had no mechanism to inherit their
supervisor's account.

## Fix (implemented)

Children now inherit the supervisor's resolved profile at spawn time:

1. `SessionNodeModel.ProfileName` (new, persisted) — profile display name pinned for a
   session's headless turns. Null = resolve from history, then Default (unchanged
   behavior for user-created sessions).
2. `ISessionTurnService.ResolveProfileName(node)` — returns `node.ProfileName` if set,
   otherwise the last profile recorded in the session's own history.
   `ResolveProfileConfigDir` now maps this name to a config dir. Because the pinned value
   takes precedence, inheritance also chains correctly through nested supervisors.
3. `AgentMcpServer.InheritSupervisorProfile(child, supervisor)` — called in all three
   spawn paths (`SpawnSessionAsync`, `RunWorkerAsync`, `RunPipelineItemAsync`) right after
   the child node is created and before its first turn runs, pinning
   `child.ProfileName = ResolveProfileName(supervisor)`.

The supervisor's profile resolves from its own history (interactive supervisor) or its
own pinned `ProfileName` (a supervisor that is itself a spawned child), so the correct
account propagates down the whole orchestration tree.

## Notes / Limitations

- This inherits the profile as it was resolved **at spawn time**. If the supervisor's
  profile later changes, already-spawned children keep the pinned one.
- If the account driving the supervisor is itself revoked, children will inherit the same
  revoked account and still 401 — by design (they mirror the supervisor). The remedy in
  that case is re-authenticating that profile in ClaudeMaximus.

## Status

Fixed — build clean, 45/45 tests pass. Related: ISSUE-001 (the history-scan resolution
this builds on).
