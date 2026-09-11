# UC.LC.01 — Offline coding with a local model using a curated tool profile

Related requirement: [FR.12.21 — Local Tool Profile](/docs/requirements.md#fr1221--local-tool-profile), [FR.12.22 — Command-bar selector compaction & left-truncated model label](/docs/requirements.md#fr1222--command-bar-selector-compaction--left-truncated-model-label). Related background: [Local-Models_Agentic-Coding-Findings](/docs/business-analysis/Local-Models_Agentic-Coding-Findings.md).

## Actors

- **User** — working offline or wanting to avoid Anthropic API usage, using a locally-installed Ollama model for a coding session.
- **Session** — a Tessyn session node whose Directory has a local (`ModelProvider.Ollama`) model selected.
- **claude CLI subprocess** — launched by `ClaudeProcessLauncher`, routed to the local Ollama endpoint (FR.12.14).

## Preconditions

- Ollama is running locally and has been discovered (FR.12.13); at least one local model is selectable in the command bar's Model selector.
- The session's Directory node has a local model selected.

---

## UC.LC.01.1 — Default lean profile applied automatically

**Trigger:** The User sends a message in a session whose Directory has never had a Local Tool Profile configured (`DirectoryNodeModel.LocalToolProfile` is `null`) and has a local model selected.

1. The claude CLI subprocess is launched with `--tools "Read Write Edit Bash Glob Grep LS"`.
2. No `--mcp-config` flag is passed (the default profile has `IncludeCmxMcp = false` and no external servers selected).
3. The local model completes the turn using only the lean tool set; git operations proceed via `Bash`.

**Outcome:** A local model gets a small, low-confusion tool surface with no explicit configuration required from the User.

## UC.LC.01.2 — User customizes the tool profile

**Trigger:** The User opens the Local Tool Profile selector (`Tools: ▾`) for a directory with a local model selected, and changes the selection — e.g. adds `WebFetch` and `TodoWrite`, and enables `IncludeCmxMcp`.

1. The selection is persisted to `DirectoryNodeModel.LocalToolProfile` in `appsettings.json`.
2. The next turn's claude CLI subprocess is launched with `--tools "Read Write Edit Bash Glob Grep LS WebFetch TodoWrite"`.
3. `--mcp-config` points at a config file containing only the `cmx` server entry; `--strict-mcp-config` is also passed.

**Outcome:** The User can widen or narrow the local model's tool surface per directory, and the CLI invocation reflects exactly that selection — nothing more, nothing less.

## UC.LC.01.3 — Anthropic model in the same directory is unaffected

**Trigger:** The User switches the Model selector (for the same directory as UC.LC.01.2) to an Anthropic model and sends a message.

1. The claude CLI subprocess is launched with the full native tool set and the full FR.20 merged `--mcp-config` (`cmx` plus every enabled external MCP server), exactly as it would without any Local Tool Profile ever having been configured.
2. The directory's saved `LocalToolProfile` value is left untouched, ready to apply again the next time a local model is selected in this directory.

**Outcome:** The Local Tool Profile is scoped strictly to local-model turns; switching back to Anthropic never leaves a session under-tooled.

## UC.LC.01.4 — Tool-incapable local model disables the selector

**Trigger:** The User selects a local model whose Ollama-reported capabilities mark it tool-incapable (`SupportsTools = false`, FR.12.13).

1. The Local Tool Profile selector is shown disabled (or hidden — matching however FR.12.13's existing tool-incapable handling surfaces in the command bar) since no tool configuration can take effect.
2. The claude CLI subprocess is launched with `--tools ""` and no `--mcp-config`, regardless of any Local Tool Profile saved for the directory.

**Outcome:** The User is not given a control that would silently do nothing; the model still runs, just without any tools, as it already did before this feature existed.

---

## Out of scope for this use case set

- Configuring per-session (rather than per-directory) tool profiles.
- Any change to Anthropic-routed tool/MCP behavior (FR.20 merge logic is unchanged).
