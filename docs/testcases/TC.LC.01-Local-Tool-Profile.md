# TC.LC.01 — Local Tool Profile

Related requirement: [FR.12.21](/docs/requirements.md#fr1221--local-tool-profile), [FR.12.22](/docs/requirements.md#fr1222--command-bar-selector-compaction--left-truncated-model-label). Related use cases: [UC.LC.01](/docs/usecases/UC.LC.01-Local-Tool-Profile.md).

CLI argument assembly is tested by asserting on the built `ProcessStartInfo`/argument list (mirrors the existing `ClaudeProcessLauncher`/`ClaudeProcessManager.BuildArguments` test pattern) — no real `claude` or Ollama process is required for these cases.

## Integration tests (xUnit, `TessynDesktop.Tests`)

### TC.LC.01.1 — Default lean profile applied for a local model
- `BuildArguments_LocalModel_NoSavedProfile_UsesDefaultLeanNativeTools` — `LocalToolProfile` is `null` on the directory; asserts `--tools "Read Write Edit Bash Glob Grep LS"` (order and exact set) is passed, and no `--mcp-config` flag is present (UC.LC.01.1).
- `BuildArguments_LocalModel_NoSavedProfile_OmitsMcpConfig` — explicit assertion that `--mcp-config`/`--strict-mcp-config` are absent when the default profile has no MCP servers selected.
- `LocalToolProfileModel_Null_ResolvesToDefaultLeanProfile` — unit test on whatever resolver method backs "null → default profile" (e.g. a static `LocalToolProfileResolver.Resolve(model)`), asserting the exact default `NativeTools` list and `IncludeCmxMcp = false`.

### TC.LC.01.2 — User customizes tools → correct CLI args
- `BuildArguments_LocalModel_CustomProfile_UsesSelectedNativeTools` — a saved `LocalToolProfileModel` with a non-default `NativeTools` list (e.g. core set + `WebFetch` + `TodoWrite`); asserts `--tools` reflects exactly that list, space-separated (UC.LC.01.2).
- `BuildArguments_LocalModel_CustomProfile_IncludeCmxMcp_WritesMcpConfigWithCmxOnly` — `IncludeCmxMcp = true`, no external servers; asserts the generated `--mcp-config` file contains only the `cmx` server entry and `--strict-mcp-config` is passed.
- `BuildArguments_LocalModel_CustomProfile_IncludedExternalServerIds_MergesOnlyThoseServers` — `IncludedExternalMcpServerIds` naming a subset of `AppSettingsModel.ExternalMcpServers`; asserts the merged config includes exactly those entries (plus `cmx` if enabled) and excludes every other registered external server.
- `BuildArguments_LocalModel_CustomProfile_EmptyToolSelection_PassesEmptyToolsFlag` — all native tools deselected; asserts `--tools ""`.

### TC.LC.01.3 — Anthropic model unaffected
- `BuildArguments_AnthropicModel_IgnoresDirectorysLocalToolProfile` — directory has a non-default `LocalToolProfile` saved, but the session's selected model is Anthropic; asserts the full native tool set (no `--tools` restriction / CLI default) and the full FR.20-merged `--mcp-config` are used, identical to a directory with no `LocalToolProfile` at all (UC.LC.01.3).
- `BuildArguments_SwitchingModelBackToAnthropic_DoesNotMutateSavedLocalToolProfile` — asserts the directory's persisted `LocalToolProfile` value is unchanged after a turn runs against an Anthropic model.

### TC.LC.01.4 — Tool-incapable local model
- `BuildArguments_ToolIncapableLocalModel_PassesEmptyToolsAndNoMcp_RegardlessOfSavedProfile` — `SupportsTools = false` on the selected `ClaudeModelInfo`, with a saved `LocalToolProfile` that selects several tools and MCP servers; asserts `--tools ""` and no `--mcp-config`, confirming the profile is not consulted at all for tool-incapable models (UC.LC.01.4).
- `LocalToolProfileSelectorViewModel_ToolIncapableModel_IsDisabled` — the selector's bound `IsEnabled`/`IsVisible` property is false when the selected model reports `SupportsTools = false`.

### TC.LC.01.5 — Selector visibility & command-bar compaction (FR.12.22)
- `LocalToolProfileSelectorViewModel_AnthropicModelSelected_IsHidden` — selector visibility is false whenever the resolved model's `Provider` is `Anthropic`.
- `LocalToolProfileSelectorViewModel_ToolCapableLocalModelSelected_IsVisible` — selector visibility is true for a tool-capable Ollama model.
- `LeftEllipsisConverterTests` — `Convert` returns the unmodified string when shorter than the max length; returns a left-truncated `…`-prefixed suffix (e.g. `claude-opus-4-8` → `…pus-4-8`) when longer; boundary case at exactly max length.

## Manual / E2E verification (requires a running local Ollama instance — cannot run in CI)

1. **UC.LC.01.1** — With no Local Tool Profile ever configured for a directory, select a tool-capable local model (e.g. `qwen2.5-coder:32b`), send a prompt requiring a file edit, and confirm the turn completes using only Read/Write/Edit/Bash/Glob/Grep/LS (no attempted call to a non-selected tool).
2. **UC.LC.01.2** — Open the `Tools: ▾` popup, add `WebFetch`, enable the `cmx` MCP checkbox, send a prompt that needs a URL fetched; confirm the fetch succeeds and no external MCP servers beyond `cmx` are reachable from that turn.
3. **UC.LC.01.3** — With the customized profile from step 2 still saved, switch the Model selector to an Anthropic model in the same directory and confirm full tool/MCP access (e.g. a `Task` sub-agent call succeeds) — then switch back to the local model and confirm the customized profile is still applied unchanged.
4. **UC.LC.01.4** — Select a local model known to report no tool support; confirm the `Tools: ▾` selector is disabled/hidden and the turn runs with no tools.
5. **FR.12.22 visual check** — Confirm the Model box shows a left-truncated value (e.g. `…pus-4-8`) when the full model ID overflows ~120px, while Profile/Effort truncate from the trailing edge as before, and that each selector's own dropdown still lists full-width option text.
