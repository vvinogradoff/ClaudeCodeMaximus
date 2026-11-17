> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.17 — Token Capture in Non-Daemon Path

**FR.17.1 — Stream-JSON usage extraction:** The `result` event in the claude `stream-json` format contains a `usage` object with `input_tokens` (int), `output_tokens` (int), and optionally `total_cost_usd` (double). `ClaudeProcessManager.ParseResultEvent` extracts these into new fields on `ClaudeStreamEvent`: `InputTokens`, `OutputTokens`, `CostUsd`.

**FR.17.2 — TurnResultModel extension:** `TurnResultModel` gains `InputTokens` (int), `OutputTokens` (int), and `CostUsd` (double) fields, populated by `SessionTurnService.HandleEvent` from the result event.

**FR.17.3 — Orchestration budget accrual:** When a worker turn completes inside `orchestrate_parallel` or `orchestrate_pipeline`, its `InputTokens + OutputTokens` are accrued against the active `OrchestrationBudget` (if one was registered with `budgetTokens`).

---
