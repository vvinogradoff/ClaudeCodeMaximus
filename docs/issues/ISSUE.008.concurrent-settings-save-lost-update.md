# ISSUE.008 — Concurrent `appsettings.json` Save Lost Update

**Status:** Resolved (2026-09-12)
**Affects:** Any orchestration (FR.15) with worker/sub-agent sessions, especially under load; most severe when the supervisor or a worker is in Streaming Session Mode (FR.19), which raises `AppSettingsService.Save()` call frequency far above the one-shot-per-turn baseline.

## Symptom

Orchestrated sub-agent session nodes silently vanish from the session tree. A supervisor session that is known to have spawned and run worker sessions (via `spawn_session`/`orchestrate_parallel`/`orchestrate_pipeline`) shows no children in the tree, or is not expandable at all — as if the workers had never been created.

## Investigation

The session tree (`DirectoryNodeModel`/`GroupNodeModel`/`SessionNodeModel`, owned by `SessionRegistry`) is held entirely in `AppSettingsModel`, a shared singleton (`AddSingleton`) mutated and persisted from many different threads:

- `SessionRegistry.CreateSessionForAgent` (and its sibling tree-mutation methods) — called from `AgentMcpServer`'s tool-call handlers, themselves invoked from whichever thread is servicing that MCP request.
- `SessionTurnService.RunTurnAsync` calls `_appSettings.Save()` on every turn completion, including every worker's own turn in an orchestration.
- The Streaming Session Mode (FR.19) event-reader background thread in `TurnController` also triggers Saves as turns complete.
- Assorted UI-thread toggles (favorite, auto-commit, etc.) call `_appSettings.Save()` directly.

Decisive evidence: inspecting `appsettings.json` for a supervisor node with five worker sessions known to have run showed **zero** `SupervisorNodeId` references anywhere in the file, and the worker session names were entirely absent — despite roughly 251 other cross-references (`ClaudeSessionId`/`ExternalId`/etc.) elsewhere in the same file persisting correctly. This ruled out a systematic serialization bug (the rest of the graph round-trips fine) and pointed at a race specific to concurrent writes landing at nearly the same time as the worker-creation Saves. Critically, this failure is **silent** — no exception, no warning, nothing relevant in the application logs — consistent with a last-write-wins race rather than a crash or a caught/swallowed error.

Root cause, confirmed against the code (`AppSettingsService.cs`, pre-fix):

```csharp
public void Save()
{
    var json = JsonSerializer.Serialize(Settings, SerializerOptions);
    var tempPath = _settingsFilePath + ".tmp";
    File.WriteAllText(tempPath, json);
    File.Move(tempPath, _settingsFilePath, overwrite: true);
}
```

`Save()` was completely unsynchronized. The temp-write + atomic-rename pattern only guarantees that any single Save produces a well-formed file — it does nothing to prevent two concurrent Saves from racing each other. If thread A's `CreateSessionForAgent` adds a new worker node and calls `Save()` at nearly the same instant thread B (a different worker's turn completion, or a Streaming Session Mode event) calls `Save()` from an in-memory snapshot that predates A's addition, thread B's serialize+rename can complete *after* A's — silently overwriting the file A just wrote, with no merge and no error. The result on disk is simply "the newer node never existed."

`SessionRegistry` already had its own private `_lock` around each tree mutation + its own `_appSettings.Save()` call, which prevents two `SessionRegistry` methods from interleaving with each other — but it did nothing to prevent a *different* caller (`SessionTurnService`, `TurnController`'s streaming reader, a UI toggle) from calling the unsynchronized `Save()` concurrently with a registry mutation's Save. That gap is exactly the race this issue reproduces.

## Root Cause

Unsynchronized concurrent `Save()` of the shared singleton `AppSettingsModel` graph. No lock existed that was shared between `SessionRegistry`'s tree mutations and every other caller of `AppSettingsService.Save()`. Exacerbated by Streaming Session Mode (FR.19), which calls `Save()` far more frequently than the one-shot-per-turn baseline, widening the race window per unit of orchestration activity.

## Resolution

- `IAppSettingsService.SyncRoot` (backed by `AppSettingsService`'s private `_gate`) now guards the **full bodies** of `Save()` and `Load()`.
- `SessionRegistry`'s tree-mutation methods (`CreateSessionForAgent`, `AddDirectory`, `AddSession`, etc.) now take `_appSettings.SyncRoot` instead of a private `_lock` of their own — the same lock `Save()`/`Load()` take internally. `lock` (`Monitor`) is reentrant on the same thread, so a registry method calling `_appSettings.Save()` while already holding `SyncRoot` does not deadlock. Because every mutation-plus-Save is now one atomic critical section against the *same* lock object every other `Save()`/`Load()` call also takes, a Save can never observe (or overwrite with) a stale snapshot relative to a concurrent registry mutation, and a registry mutation can never be torn by a concurrent serialize.
- Critical sections are kept tight: only the in-memory mutation and the Save/Load call itself are inside the lock — never an `await`, a subprocess launch, or unrelated file/network I/O.
- See `docs/requirements.md` NR.3 for the resulting specification.

## Prevention

`AppSettingsServiceConcurrencyTests.cs` adds regression coverage:

1. Many threads mutating `Settings.Tree` and calling `Save()` under `SyncRoot` (mirroring `SessionRegistry`'s pattern) — a fresh `Load()` afterward confirms every added node survived.
2. One thread mutating while several others call bare `Save()` concurrently, all under `SyncRoot` — asserts no `InvalidOperationException` from the shared list being enumerated mid-mutation.
3. Multiple threads calling `Save()` with **no** caller-side lock at all (mirroring `SessionTurnService`'s and UI toggles' actual call pattern) — isolates whether `Save()` is safe to call concurrently purely from its own internal lock.

Test 3 was manually verified to fail — reproducibly, with a real `IOException` ("The process cannot access the file... .tmp") — when the lock was temporarily removed from `Save()`, and to pass once restored. Tests 1 and 2 pass regardless of `Save()`'s own internal lock, since they already wrap the mutate+Save pair in the caller-side `SyncRoot` lock (exactly mirroring the now-fixed `SessionRegistry` pattern); test 3 is the one that specifically exercises `Save()`'s own synchronization in isolation.

## Known residual gaps (accepted, not blocking)

- A background-thread Save is only serialized against a *UI-thread* mutation if that UI code path also takes `SyncRoot` before mutating. Most UI toggles call `_appSettings.Save()` bare — this is now safe against torn writes or a lost *Save* (the file itself is never corrupted, and `Save()` calls are always fully serialized), but a UI toggle's own in-memory field mutation immediately before its bare `Save()` call is not itself lock-protected against a concurrent background mutation landing in between. This was not the mechanism behind the data loss reported here (that was registry-mutation-vs-Save, now fully closed) and is noted as a minor follow-up rather than fixed in this pass.
- Data already lost to this bug before the fix (the missing worker session nodes) is unrecoverable — the fix prevents recurrence, it does not reconstruct history.
