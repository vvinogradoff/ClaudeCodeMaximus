> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.16 — Model Tier Governance

A session may only create or configure worker sessions using models at or below its own capability tier.

**FR.16.1 — Tier table:**
| Tier | Matches (substring in model ID) |
|---|---|
| 4 — Fable | `fable` |
| 3 — Opus | `opus` |
| 2 — Sonnet | `sonnet` |
| 1 — Haiku | `haiku` |
| 0 — Local | anything else (Ollama / unknown) |

Matching is case-insensitive substring search on the model ID string. If multiple substrings match, the highest-tier match wins.

**FR.16.2 — Enforcement:** The `ModelTierService.GetTier(string? modelId)` method computes the tier. Enforcement is applied in `AgentMcpServer` before processing `spawn_session`, `set_session_model`, `orchestrate_parallel`, and `orchestrate_pipeline`. A clear error is returned if `callerTier < requestedTier`. `send_to_session` does not take a model arg and therefore requires no tier check; the target session's tier is already fixed at spawn time.

**FR.16.3 — Per-session model persistence:** `SessionNodeModel` persists a `ModelId` (string, nullable). This is the model used for all turns (user-initiated, scheduled, and orchestrated) on that session. When `ModelId` is null or empty, the session inherits the effective model from the per-directory `SelectedModelId`, then the global app `SelectedModelId`.

**FR.16.4 — Caller tier resolution:** The caller node's tier is derived from its effective model: `callerNode.ModelId` → directory `SelectedModelId` → app `SelectedModelId`. An unconfigured session (all null/empty) falls back to `Constants.Agent.DefaultModelTier` (default: 2, Sonnet). This prevents unset sessions from being unable to spawn any workers.

**FR.16.5 — Turn model resolution (bug fix):** `SessionTurnService.RunTurnAsync` previously always passed `model: null` to `SendMessageAsync`, causing all headless turns (scheduled and orchestrated) to run on the CLI's default model regardless of the session's configured model. After this fix, `RunTurnAsync` resolves the effective model from `SessionNodeModel.ModelId` (falling back to directory/app selection), determines `ollamaBaseUrl` and `disableTools` from model provider, and passes these to `SendMessageAsync`.

---
