> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.12 — Input Command Bar & Model Selection

The input area includes a collapsible command bar for runtime configuration of the Claude process parameters.

**FR.12.1 — Settings toggle button:** The right side of the input area is split vertically into two equal-height buttons: the existing **Send** button on top and a **Settings toggle** (gear icon ⚙) on the bottom. The settings toggle controls visibility of the command bar.

**FR.12.2 — Command bar layout:** The command bar appears directly beneath the text input area (within the same input border). When visible, it reduces the available height for the text input. The command bar has a subtle background matching the chrome medium brush for visual separation.

**FR.12.3 — Model selection:** The command bar contains a model selector (ComboBox). The first entry is always "Default" (no `--model` flag — uses Claude Code's own default). Remaining entries are loaded dynamically at startup from two sources:
1. **Anthropic models** — fetched from the Claude CLI using a print-mode query; cached to disk for 24 hours. A built-in fallback list (Opus, Sonnet, Haiku) is used if the fetch fails.
2. **Ollama models** — fetched from the locally running Ollama instance (`GET {OllamaBaseUrl}/api/tags`) on each startup. If Ollama is unreachable the discovery fails silently and no Ollama models appear.

Anthropic models appear first, Ollama models after. Each entry displays the true model ID (e.g. `claude-opus-4-7`, `gemma4:26b`).

**FR.12.4 — Model persistence:** The selected model is persisted as a **model ID string** (`SelectedModelId` on `DirectoryNodeModel` and `AppSettingsModel`). When the user switches sessions under a different directory, the model selector reflects that directory's saved choice. If the saved ID is not present in the current model list the selection reverts to "Default". New directories default to "Default" (empty `SelectedModelId`).

**FR.12.5 — Model flag injection:** When a non-default model is selected, the `--model <id>` flag is appended to the `claude` CLI arguments for all process spawns (user messages, context retries, compaction, and mid-run corrections).

**FR.12.6 — Profile selection:** The command bar contains a profile selector (ComboBox) to the right of the model selector. Each profile uses a separate `CLAUDE_CONFIG_DIR` directory, giving it an isolated authentication context with the Claude CLI. The dropdown displays the account email as the display text. A "Default" entry (index 0, no env var override) uses the CLI's default authentication. A "New..." entry at the bottom of the list triggers profile creation (FR.12.7).

**FR.12.7 — Profile creation flow:** When "New..." is selected in the profile dropdown:
1. A unique profile ID is generated (`profile_1`, `profile_2`, etc.) and a corresponding config directory created under `%APPDATA%\TessynDesktop\profiles\<profileId>\`
2. A visible console window is spawned running `claude auth login` with `CLAUDE_CONFIG_DIR` set to the profile's config directory. On Windows, a temporary `.bat` file is written to the config directory that sets the env var via `set`, uses `call` to invoke the `.cmd` wrapper (ensuring cmd.exe waits for the full auth flow including the OAuth browser callback), and ends with a `pause` so the user sees the result. The `.bat` file is cleaned up after the process exits.
3. After the console process exits, `claude auth status` is queried (with `CLAUDE_CONFIG_DIR` set) to verify authentication succeeded and retrieve the account email. If the email query fails (auth was not completed), the profile is not added and a failure message is shown.
4. The profile is added to the persisted list with the email as display name
5. The new profile is automatically selected

The dropdown selection reverts to the previous value while auth is in progress, preventing the "New..." item from being persisted as the selected index.

**FR.12.8 — Profile persistence:** Profiles are stored in `appsettings.json` as a list of `ClaudeProfileModel` objects with `ProfileId` (string, used as the config subdirectory name) and `DisplayName` (string, typically the account email). The selected profile index is persisted per working directory (stored as `SelectedProfileIndex` on each `DirectoryNodeModel`). Index 0 = Default (no env var), indices 1..N map to stored profiles. New directories default to index 0. Each profile's auth state lives in `%APPDATA%\TessynDesktop\profiles\<ProfileId>\`.

**FR.12.9 — Profile config dir injection:** When a non-default profile is selected, the `CLAUDE_CONFIG_DIR` environment variable is set to the profile's config directory on all spawned `claude` CLI processes (user messages, context retries, compaction, and mid-run corrections). This isolates session IDs, auth tokens, and settings per profile.

**FR.12.10 — Default profile email resolution:** On first session load, the application queries `claude auth status` (no config dir override) to retrieve the default account email. If successful, the "Default" entry in the profile dropdown is updated to show the email address instead of the generic "Default" label.

**FR.12.12 — Effort level selection:** The command bar contains an effort selector (ComboBox) with the following options:
| Index | Label | CLI Flag |
|---|---|---|
| 0 | Default | (no `--effort` flag — uses Claude Code's own default) |
| 1 | Max | `--effort max` |
| 2 | High | `--effort high` |
| 3 | Medium | `--effort medium` |
| 4 | Low | `--effort low` |

The selected effort index is persisted per working directory (`SelectedEffortIndex` on `DirectoryNodeModel`). When a non-default effort is selected, the `--effort` flag is appended to the `claude` CLI arguments for all process spawns.

**FR.12.11 — Command bar visibility persistence:** The show/hide state of the command bar (toggled via the settings gear button) is persisted per working directory in `appsettings.json` (stored as `IsCommandBarVisible` on each `DirectoryNodeModel`). When the user switches to a session under a different directory, the command bar visibility reflects that directory's saved state. New directories default to hidden.

**FR.12.13 — Local model discovery (Ollama):** On each startup the application queries the local Ollama instance at `GET {OllamaBaseUrl}/api/tags` for installed models. The base URL is configurable as `OllamaBaseUrl` in `appsettings.json` (default: `http://localhost:11434`). Discovered Ollama models are appended to the model selector after Anthropic models, using their true Ollama IDs (e.g. `gemma4:26b`). If Ollama is unreachable or returns an error the discovery fails silently — no Ollama models appear and no error is shown to the user.

**FR.12.14 — Local model routing:** When a session uses an Ollama model (identified by `ModelProvider.Ollama`), the claude CLI subprocess is launched with:
- `ANTHROPIC_BASE_URL = {OllamaBaseUrl}` — the **bare host, no `/v1` suffix** — routes the Claude SDK to the local Ollama endpoint. The Anthropic SDK appends `/v1/messages` to this value itself; including `/v1` in `OllamaBaseUrl` here would double the path segment and break routing. (`ClaudeProcessLauncher.TryStartProcess` sets this via `ollamaBaseUrl.TrimEnd('/')`.)
- `ANTHROPIC_AUTH_TOKEN = ollama` — dummy token accepted by Ollama
- `ANTHROPIC_API_KEY = ""` — clears any real key

Profile authentication (`CLAUDE_CONFIG_DIR`) and HTTPS proxy settings are **not** injected for Ollama-routed sessions. Ollama models are also excluded from the Claude Assist fallback chain (FR.13.14) — assist calls (title generation, semantic search) always use Anthropic models only. Because the claude CLI is still the process being spawned, an Ollama-routed turn hands the model the CLI's full native tool surface via the Anthropic `/v1/messages` protocol (Ollama honors the `tools` parameter) — see FR.12.21 for the profile that curates this surface down for weaker local models.

**FR.12.15 — Mid-flight model change (not supported — design decision):** Changing the model or effort dropdown while a Claude response is in progress does **not** affect the currently-running invocation. The model and effort are captured at send time and passed to the CLI process; there is no mechanism in the Claude CLI protocol to hot-swap the model mid-stream. The new selection takes effect on the next user message automatically. Real mid-flight switching would require killing the running process and re-sending the same prompt, discarding the partial response — this is not implemented. Users who need to abort and restart with a different model should use the **Stop** button, then change the model and resend.

**FR.12.16 — Auth failure detection:** When a `result` or `system` error event's content contains the substring `"Invalid authentication credentials"` (the Claude CLI's 401 error text), the session sets `HasAuthError = true` in addition to normal error handling (FR.12.16 does not change how the error text itself is displayed — it only raises a flag). The flag is cleared when: a subsequent request succeeds, the user switches the profile selection, or a Re-authenticate/Remove action (FR.12.17/FR.12.18) completes.

**Profile actions flyout:** Re-authenticate, Remove, Rename, and Delete (FR.12.17–12.19) live in the WPF client's profile flyout (`ProfileFlyout`), not directly in the command bar — they were crowding it. The **"Profile:"** label is a toggle (`Profile: ▾`); clicking it opens a small popup below the label containing the (up to four) applicable icon buttons, sized compactly (`Padding="4,2"`). The popup closes when the label is clicked again (no click-outside light-dismiss, so the toggle's checked state cannot desync from the popup's open state).

**FR.12.17 — Re-authenticate button:** When `HasAuthError` is true, a **key icon (🔑)** button appears in the profile actions flyout. Clicking it re-runs the same interactive auth login flow used for profile creation (FR.12.7), targeting the **currently selected profile's** config directory (or no `CLAUDE_CONFIG_DIR` override, if Default is selected). On success (`claude auth status` returns an email), `HasAuthError` is cleared and a confirmation message is appended to the session. On failure/cancellation, `HasAuthError` remains set and a failure message is appended. (Currently shown unconditionally rather than gated on `HasAuthError`, per an explicit interim request — see ISSUE.004.)

**FR.12.18 — Remove profile button:** When `HasAuthError` is true **and** a non-Default profile is selected, a **✕** button appears in the flyout alongside Re-authenticate. Clicking it asks for confirmation (destination directory is preserved, not deleted — see below), then:
1. Removes the profile's entry from `AppSettingsModel.Profiles` (the app no longer lists or selects it).
2. Renames the profile's config directory on disk from `<ProfilesRoot>\<ProfileId>\` to `<ProfilesRoot>\<ProfileId>_rem\` (e.g. `profile_4` → `profile_4_rem`). The directory itself, its Claude sessions, and its projects are **not deleted** — only marked removed and taken out of the active profile list, since they may hold sessions/projects still worth recovering later. If a directory with the `_rem` suffix already exists (e.g. from a previous removal that was never cleaned up manually), a numeric suffix is appended (`_rem2`, `_rem3`, ...) to avoid collisions.
3. Resets the profile selector to "Default" (index 0) and clears `HasAuthError`.

The Default profile cannot be removed (the Remove button never appears while Default is selected, even if `HasAuthError` is true).

**FR.12.19 — Profile Rename/Delete buttons:** Two icon buttons, **✏ Rename** and **🗑 Delete**, appear in the flyout whenever `CanManageSelectedProfile` is true (a stored, non-Default profile is currently selected — independent of `HasAuthError`, unlike FR.12.18's Remove button). They act on whichever profile is currently selected in the Profile dropdown. All four flyout buttons carry a `ToolTip.Tip` label for discoverability, since they're icon-only.
1. **Rename** prompts for a new display name (pre-filled with the current one) via the same input dialog used elsewhere in the app. Confirming updates the profile's stored `DisplayName` only — the `ProfileId` and its config directory are untouched.
2. **Delete** prompts for confirmation, then performs the same removal as FR.12.18 (removes the profile from `AppSettingsModel.Profiles`, renames its config directory with a `_rem` suffix, collision-safe). If the deleted profile was the one currently selected, selection falls back to Default; if a different (unselected) profile was deleted, the current selection is preserved (adjusting its index for the shift). Remove (FR.12.18) and Delete share the same confirm-then-delete code path — both act on the same underlying operation, just gated by different visibility conditions.
3. **Default entry styling:** the "Default" entry is prefixed `"[Sys] "` (e.g. `[Sys] user@example.com`) and rendered in dark grey (`ProfileListItemModel.IsSystem`), while stored profiles render in the normal (black) text color — visually distinguishing the base, non-isolated claude auth identity from actual stored profiles, since both can otherwise show the same account email.

**Design note:** FR.12.19 originally implemented Rename/Delete as a right-click context menu on individual Profile dropdown entries. This was replaced with plain command-bar buttons acting on the current selection, because a context menu opened from inside a combo box's own dropdown popup is a fragile nested-popup pattern — buttons avoid that risk entirely and reuse the same pattern as the Re-authenticate/Remove buttons. The WPF flyout keeps the buttons.

**FR.12.20 — Background ("small-fast") model routing:** The `claude` CLI uses a secondary, smaller model internally for background/utility work, distinct from the primary conversational model, controlled by the `ANTHROPIC_SMALL_FAST_MODEL` environment variable. Tessyn sets this only for Anthropic-routed sessions:
- **Anthropic-routed sessions:** `ANTHROPIC_SMALL_FAST_MODEL` is set to the curated catalog's Haiku entry (FR.18.7 — currently `claude-haiku-4-5-20251001`), taking background-task load off the session's primary selected model (Sonnet/Opus/Fable).
- **Ollama-routed sessions:** `ANTHROPIC_SMALL_FAST_MODEL` is deliberately **not** set. Setting it to a distinct, smaller local model would require Ollama to keep a second model resident in memory alongside the primary session model, increasing local resource pressure for hardware that already runs a single large local model (e.g. `gemma4:26b`/`gemma4:31b`) at full size for free — the opposite of the variable's intended effect. Ollama-routed sessions therefore continue to use only the single model selected in the command bar (FR.12.5) for all CLI-internal work.

**FR.12.21 — Local Tool Profile:** A per-directory selection of exactly which native tools and MCP servers the claude CLI subprocess is given, applied **only** when the directory's currently selected model is a local (`ModelProvider.Ollama`) model. For Anthropic-routed sessions, launch behavior is unchanged (full native tool set + full `--mcp-config` merge per FR.20) and this profile has no effect.

**Rationale:** Local Ollama models already run through the claude CLI (FR.12.13/FR.12.14) and are handed the CLI's full ~30-tool native surface plus the full merged MCP server set. Empirically, a weak, quantized, or abliterated local model gets confused by that large a tool surface and mis-calls tools (e.g. inventing a `read_file` tool instead of calling `Read`), even though the underlying Ollama-served Anthropic `/v1/messages` protocol correctly returns valid `tool_use` blocks. A smaller, curated tool surface measurably improves local-model tool-call reliability without touching Anthropic-routed sessions at all.

- **Data model:** `LocalToolProfileModel` — `List<string> NativeTools`, `bool IncludeCmxMcp`, `List<string> IncludedExternalMcpServerIds`. Persisted as a nullable `LocalToolProfile` property on `DirectoryNodeModel` in `appsettings.json`. `null` means "use the default lean profile" — existing directories with no saved profile are unaffected and behave exactly as before this feature (backward-compatible).
- **Default lean profile** (used whenever `LocalToolProfile` is `null`): `NativeTools = [Read, Write, Edit, Bash, Glob, Grep, LS]`; `IncludeCmxMcp = false`; no external MCP servers. Git operations are still available through `Bash`.
- **Selectable native tool catalog:** `Read`, `Write`, `Edit`, `Bash`, `Glob`, `Grep`, `LS` (the core set, included in the default lean profile) plus optional `WebFetch`, `WebSearch`, `NotebookEdit`, `TodoWrite`, `Task` (available to add, not included by default).
- **CLI mapping — native tools:** the selected native tool names are joined space-separated and passed as `--tools "<names>"`. An empty selection passes `--tools ""` (no tools); the claude CLI treats `"default"` as "all tools" but the Local Tool Profile never emits that literal — it always enumerates the selected names explicitly.
- **CLI mapping — MCP:** the `--mcp-config` file built for a local turn includes only the `cmx` in-process server (if `IncludeCmxMcp` is true) plus whichever `ExternalMcpServerModel` entries match `IncludedExternalMcpServerIds` (FR.20). `--strict-mcp-config` is additionally passed for local turns so no ambient/user-level MCP configuration leaks in beside the curated set. If neither `IncludeCmxMcp` nor any external server is selected, `--mcp-config` is omitted entirely for that turn.
- **Tool-incapable local models unaffected:** an Ollama model with `SupportsTools = false` (FR.12.13) still receives `--tools ""` and no `--mcp-config` regardless of any configured Local Tool Profile — the profile only governs *which* tools a tool-capable local model receives, not whether tool-incapable models are given tools at all.

**FR.12.22 — Command-bar selector compaction & left-truncated model label:** The Model, Profile, and Effort selector boxes in the command bar are each fixed to their own compact width via a local `Width` override on the shared `.cmxCompact` style: **Model ~120px, Profile ~100px, Effort ~60px** (Model needs the most room to keep a useful suffix legible after left-truncation below; Effort's option labels are short enough that 60px rarely truncates at all). Each selector's own dropdown flyout is unaffected by this and still opens at full/natural content width — only the closed box is narrowed.

- **Left-truncated model label:** unlike Profile and Effort (whose values truncate normally, trailing-edge, via the framework's standard trailing-ellipsis trimming), the Model box's selected value is right-aligned and truncated from the **left** — e.g. `claude-opus-4-8` displays as `…pus-4-8` — so the distinctive trailing suffix of a model ID (the part that actually distinguishes it from sibling models) stays visible instead of the shared `claude-` prefix. Implemented with leading-ellipsis trimming, sized to the box's actual available width rather than a fixed character count.
- **`:latest` tag stripped from the displayed model text only:** Ollama tags commonly carry a trailing `:latest` (e.g. `qwen3.6:35b-a3b:latest`). Both the Model box's closed-box label and its open dropdown list strip this trailing `:latest` from the text shown to the user — so the example above displays as `qwen3.6:35b-a3b` — via display converters. This is purely cosmetic: the underlying model ID string is completely unchanged everywhere it is actually used — the `--model` CLI flag, `SelectedModelId` persistence, and `LocalToolProfileResolver`'s model matching all continue to see the full ID including `:latest`.
- **Local Tool Profile control relocated (amended by FR.12.23):** the per-directory Local Tool Profile (FR.12.21) is no longer a selector in the Model/Profile/Effort row. It is now an icon-style toggle button — sized to match the existing 26×26 icon buttons (Chrome, Streaming Session Mode, Session Completion Sound) — positioned immediately to the right of the Session Completion Sound button (FR.22). It remains visible only when the directory's currently selected model is local (`ModelProvider.Ollama`), and remains disabled (with the existing tooltip explaining why) when the selected local model does not support tool calling. Its click/press-and-hold interaction and popup content are specified in FR.12.23.
- **No drop-down arrow chrome:** the three `.cmxCompact` boxes also suppress the theme's built-in drop-down arrow glyph and let the closed-box content span into the reclaimed space — clicking anywhere on the closed box still opens the dropdown, so the arrow was pure decoration and, on the 60px Effort box, ate about half the available width.

**FR.12.23 — Local Tools master enable/disable:** The relocated Tools icon button (FR.12.22) is not just a way to open the tool/MCP checklist — it is itself a master on/off switch for whether the Local Tool Profile applies at all.

- **FR.12.23.1 — `Enabled` flag & resolver bypass:** `LocalToolProfileModel` gains an `Enabled` (bool) field, defaulting to `true`. Profiles persisted before this field existed deserialize with `Enabled = true` (backward-compatible — no behavior change for existing saved profiles). When `Enabled` is `false` for a directory, `LocalToolProfileResolver.Resolve` returns `null` for that directory's local-model turns **regardless of the saved `NativeTools`/`IncludeCmxMcp`/`IncludedExternalMcpServerIds` selection** — the local model is then given the same unrestricted native tool set and full FR.20-merged `--mcp-config` an Anthropic-routed session gets. The saved selection itself is not cleared or modified by disabling — it is preserved untouched and automatically reapplied the next time the master switch is turned back on.
- **FR.12.23.2 — Interaction model:** the relocated Tools button (FR.12.22) has two distinct gestures:
  - **Short click** toggles the master switch (`Enabled`) and persists the new value immediately — no separate Save step, matching every other command-bar toggle in this app.
  - **Press-and-hold (~450ms)** opens a popup checklist instead of toggling the master switch — the same native-tools + `cmx` + registered-external-MCP-server checkbox list described in FR.12.21/FR.12.22's prior selector design.
  - While the popup is open: clicking away from it, or clicking the Tools button again, closes the popup **without** changing the master switch.
  - Clicking a tool's checkbox, or its label text, toggles that tool's inclusion in the saved selection and persists immediately; the popup stays open afterward so further edits can be made in the same interaction.
- **FR.12.23.3 — Visual state:** the button is shown visibly dimmed (reduced opacity) when the master switch is off, and at full opacity when on — consistent with the existing convention (FR.11.9) that active vs. inactive instruction toggles must be visually distinguishable at a glance.

**FR.12.24 — OAuth refresh-race mitigation (fixes a confirmed intermittent auth failure):** Two or more `claude` CLI processes sharing one `CLAUDE_CONFIG_DIR` (most commonly the Default profile, which uses no override and so shares the literal `~/.claude` credential store machine-wide, and orchestrated children that inherit their supervisor's profile — see `docs/issues/ISSUE.002.orchestrated-child-profile-inheritance.md`) can race a near-expiry OAuth token's refresh: whichever process's refresh request lands second presents an already-rotated refresh token and the CLI reports `"OAuth session expired and could not be refreshed"` — a transient failure, not an actually-revoked account. Two mitigations:

- **FR.12.24.1 — Per-config-dir spawn serialization:** `ClaudeProcessLauncher.Start` — the single chokepoint for One-shot Mode, Streaming Session Mode, and `RunPrintModeAsync` spawns — serializes the `Process.Start` call itself with a lock keyed by `profileConfigDir` (Default/null spawns share one key). The lock is held only around the launch call, not the process's subsequent lifetime, so turns sharing a profile still run fully in parallel once started; it narrows the window in which two launches' startup-time token checks can interleave.
- **FR.12.24.2 — Single silent auto-retry:** `TurnController` (interactive one-shot and Streaming Session Mode) and `SessionTurnService` (headless one-shot and the FR.19.13 streaming-redirect path) detect `Constants.Auth.OAuthRefreshRaceMarker` on a `result`/`system` error event, suppress the file write and `HasAuthError`/FR.12.16 flag for that event, wait `Constants.Auth.OAuthRefreshRaceRetryDelayMs` (1.5s), and retry the identical turn exactly once. A second failure with the same marker is not retried again — it is not distinguished from any other auth failure and falls through to normal FR.12.16 handling. Full incident writeup in `docs/issues/ISSUE.010.oauth-refresh-token-race.md`.

**FR.12.25 — Local model Claude Code compatibility check:** Ollama's `tools` capability flag (FR.12.13 `SupportsTools`) is not enough to know whether a local model can actually run under the claude CLI. The CLI places a `role: "system"` message *inside* `messages[]` (the environment block, after the first user turn). Ollama models that render through their own built-in Go **renderer** (Modelfile `RENDERER <name>`, e.g. all `ollama.com/library` Qwen 3.5/3.6 and Gemma 4 builds) accept this. Models pulled from `hf.co/...` (and many community re-uploads) instead render through the GGUF-embedded Jinja chat template, and the stock Qwen 3.x template raises `System message must be at the beginning`. Ollama then returns HTTP 500 on **every** turn, even with tools disabled. Background and evidence: `docs/business-analysis/Local-Models_Agentic-Coding-Findings.md`.

- **FR.12.25.1 — Static classification (always, free):** `OllamaModelService` already calls `POST /api/show` per model during discovery. From the same response it also derives `ClaudeCodeCompatibility`:
  - `Compatible` — the `modelfile` declares a `RENDERER`, **or** its `TEMPLATE` is a real Go template (anything other than the passthrough `{{ .Prompt }}`).
  - `Incompatible` — passthrough `TEMPLATE {{ .Prompt }}`, no `RENDERER`, and the Jinja `template` contains a `raise_exception(` guard on a system-message position (matched on the `System message must be at the beginning` text).
  - `Unknown` — anything else. Treated as compatible: no warning, nothing blocked.
  No model load and no inference are involved.
- **FR.12.25.2 — UI signal:** in the Model dropdown, `Incompatible` local models show a ⚠ marker. Its tooltip says the model's chat template rejects Claude Code's request layout and points to FR.12.26 repair when available. The model stays selectable; the indicator only warns.
- **FR.12.25.3 — On-demand agent probe:** a "Test with Claude Code" action on a local model sends one Claude-Code-shaped request directly to `{OllamaBaseUrl}/v1/messages`: a top-level system prompt, a mid-`messages` system message, a single `Read` tool, and a prompt that needs that tool. The result is one of:
  - `Fails` — a 5xx or template error.
  - `NoToolCall` — 200, but the model answered in prose.
  - `CallsTools` — the response contains a well-formed `tool_use` for `Read`.
  The probe loads the model, so it runs only when the user asks, never automatically. The result is cached in memory per model digest and shown in the same tooltip.

**FR.12.26 — One-click renderer repair for local models:** For an `Incompatible` (FR.12.25.1) model whose `/api/show` `model_info["general.architecture"]` appears in a curated map of Ollama built-in renderers (`Constants.Ollama.RendererByArchitecture`, initially `qwen35 → qwen3.5`, `gemma4 → gemma4`; extended as verified), the ⚠ tooltip/context offers **"Create Claude Code–compatible copy"**.
- Tessyn calls `POST {OllamaBaseUrl}/api/create` with `{ "model": "<alias>", "from": "<original id>", "renderer": "<r>", "parser": "<r>" }`. This is verified to work on Ollama 0.32.x, and the copy reuses the original's weight blobs, so it takes no extra disk space.
- The original model is never modified or deleted.
- The alias is `<repo-or-base-name>-cc:<original tag>` (e.g. `hf.co/HauhauCS/Qwen3.5-9B-Uncensored-HauhauCS-Aggressive:Q8_0` → `qwen3.5-9b-uncensored-hauhaucs-aggressive-cc:q8_0`), lower-cased and collision-checked.
- On success the model list is refreshed and the new alias is selected for the current directory.
- Architectures not in the map get no repair offer, only the FR.12.25.2 warning.

**FR.12.27 — Profile pins (ISSUE.013, decisions A3 and A18):** Reports: `.wpf-work/320-issue013-daemon.md`, `320b-issue013-fixes.md`, `320c-issue013-n1n2.md`.
- A session is *pinned* to a profile by `ProfileId`: `"default"` for the system profile, otherwise `ClaudeProfileModel.ProfileId`. The display name (`ProfileName`) is only a label and a legacy record. A pin is written by the profile choice of a turn, by `profile.switch` (FR.12.28), by `send_to_session` with `migrate` and by spawn inheritance (FR.15.14). Session nodes store it as `ProfileId` and `PendingProfileId` in the settings file.
- When the settings load, a pin stored by display name becomes an id (case-insensitive match; with a duplicate display name the first profile wins and the duplicate is logged). A pin that matches no stored profile is left as it is. `[Sys]` labels stay unpinned. Startup logs one summary line: N pinned by id, M legacy records that match no profile, K `[Sys]` labels.
- **Pinned means exact.** A turn whose pin (`ProfileId`) or explicit per-turn choice does not resolve (the profile was removed) fails with an error that names the profile. It never runs on Default, and nothing is written to the transcript.
- **Legacy label rule (A18).** A legacy record (a display name only, from an older version or from the transcript history) that matches no stored profile does not fail. The turn runs on Default and the user is told: `send_to_session` ends with `[profile=default; the recorded profile '<name>' no longer exists]` (also in async mode); Telegram sends an info message; a scheduled turn posts a mailbox note to the supervisor. A Warning is logged. The first turn then pins the session by id.
- A turn that is refused before it runs (`TurnResultModel.Refused`) is reported by Telegram ("Turn failed: ...") and by the scheduler (a log entry and a mailbox note to the target's supervisor; none for a top-level session or for a mailbox delivery, so there is no loop). Ordinary CLI errors and cancels are not reported this way.
- `session.get` and `session.headers` return `lastUsed.profileId` and `lastUsed.profileName` from the pin (a queued target wins), not from the history. A client sends a per-turn `profile` only when the user changed it. For the Default pin `profileName` is `[Sys] Default` unless the history already says Default.
- A queued send keeps the profile that was selected when Send was pressed (decision A14).

**FR.12.28 — Profile switch as a tree operation (ISSUE.013 R1, R3, R7):**
- `profile.switch { nodeId, profileId, includeDescendants? }` (loopback only; `docs/refactoring/tdcp-spec.md` section 9.8) moves a session, and with `includeDescendants` all its descendants (the SupervisorNodeId chain; cycle-safe), to another profile. A move takes the transcript (`<slug>\<id>.jsonl`) and merges the sibling `<id>\` folder from the Default home and every profile folder. The target's files win on a name conflict. If both stores hold a transcript, the target's is kept, the source copy stays and a Warning lists both copies. Same-account profiles are moved too. `PriorClaudeSessionIds` move with the session.
- The move scans the moved transcript for absolute paths under the old config dir (at most 50 distinct) and reports them in `oldConfigPaths`. It never rewrites them.
- A running session is never touched. Its switch is queued (`PendingProfileId`) and applied under the turn lock when its next turn starts. A switch back to the current profile cancels a queued switch. A queued switch survives a restart.
- When a switch is applied to a session that is not running, its live streaming process is stopped.
- A failed file move keeps the switch queued and is listed in `failed[]`. No pin is written when the transcript itself did not move. If only files of the session folder failed, the session is pinned (the transcript is in the new store) and the leftovers are retried.
- The adoption of a transcript from another profile (FR.11.13) runs under the turn lock and moves the `<id>\` folder too, so a queued message with another profile cannot move files under a running turn.
- Not built (ISSUE.013 stays open): R4 shared project memory, R6 connector diff, the R1 cascade dialog and the R5 tree badge in WPF. The WPF profile dropdown should read `ProfileId` from the node.

**FR.12.29 — Collapsible input area:** The input area of the session pane (the text box, the side column of buttons and, when shown, the command bar of FR.12.2) can be collapsed to save vertical space. The WPF client builds it; it adds three controls the pane did not have: a splitter above the input area, a settings toggle for the command bar, and the Session/Focus switch of FR.23.12. Use cases: [UC.IC.01](/docs/usecases/UC.IC.01-Input-Collapse.md). Test cases: [TC.IC.01](/docs/testcases/TC.IC.01-Input-Collapse.md).
- **Splitter.** A horizontal splitter between the transcript (or the Focus view) and the input area sets the height of the input area. Dragging it resizes the area; the height the user dragged to is remembered for the run of the app and used when the area expands again. Without a drag the area has the height of its content.
- **Settings toggle.** In the expanded state the side column has a **Settings** toggle (gear) that shows and hides the command bar (FR.12.1). The choice is remembered across restarts as one setting of the client (FR.12.11 keeps it per working directory in the daemon's settings; WPF has no such setting yet). The toggle is not shown in the collapsed state, so the command bar's visibility is changed only from the expanded state. A client without a saved choice shows the command bar.
- **Two states.** *Expanded* is the layout of FR.12.1/FR.12.2: instruction toolbar, notices, text box (wrapping, up to a natural maximum height), command bar when shown, and a side column with the file-mode toggle, the settings toggle and **Send**, plus **Stop** beside it while a turn runs. *Collapsed* shows the **command bar only** when it is shown, and **one line of the input text** when the command bar is hidden. In the one-line state the text stays editable, does not wrap and scrolls sideways. The instruction toolbar, the file-mode toggle and the settings toggle belong to the expanded state; notices and the file-copy progress stay visible.
- **Side button when collapsed.** The collapsed side column shows **exactly one** button: **Send**, or **Stop** while a turn is running (the state in which the expanded area shows Stop). It is never both.
- **Toggle.** A double click on the splitter collapses an expanded area and expands a collapsed one; with the splitter focused, Enter or Space does the same. A drag out of the collapsed state expands the area first, then resizes it.
- **Session/Focus switch.** The header has two buttons, **Session** and **Focus**, one always selected (FR.23.12). **Focus** replaces the transcript with the Focus view (FR.23.14); the file preview pane (FR.26) stays beside it. The Focus badge (FR.23.13) shows on the Focus button while Session is selected. The selection is kept per session in memory and never persisted; a session not yet opened starts on Session. Following an entry reference (FR.23.18) selects Session, so the entry shows.
- **Defaults.** The collapsed state is **not persisted**. A **Session view** always starts **expanded**. A **Focus view** always starts **collapsed**.
- **Reset on switching.** Selecting a different session selects the view that session was last on (Session when new) and sets the state to the default of that view: collapsed for Focus, expanded for Session. Returning to the first session resets it the same way; nothing is carried over, so a Focus view that was expanded by hand is collapsed again when the session is reopened. Switching between the Session view and the Focus view of the same session also applies the default of the view entered. Opening the session that is already open changes nothing.
- **Not affected.** Draft text, the command bar's visibility (FR.12.11), the selected model, profile and effort, and a running turn.

---

---

## Amendments and additions (batch 2026-11-17)

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.12.1 / FR.12.2 Input area and command bar | FR.12.30, FR.12.31 | Toggle colours, spacing, icon-only side buttons, the input box takes the free height. |
| FR.12.29 Collapsible input area | (note) | Its "Open point for the Architect" (which client builds it first) is closed: both clients have it. |

### New requirements

**FR.12.30 — Toggle and button colours.** Toggle buttons of the command bar (Streaming, Sound, Auto-Commit and the like; checked or not) use the **regular button colours** of the theme, with a distinct checked state of the same family. **Only Send** uses the accent colour. No other control of the composer is accent-filled. A **margin** separates the **Profile** selector from the **Streaming** toggle (the same gap as between the other selectors).

**FR.12.31 — Composer layout (WPF).**
- On the selector line, **Streaming and Sound are icons** (tooltip carries the text). The side buttons beside the input (File, Send, Stop, Schedules, Settings) are **icon-only**; **Send fills the height** of its column.
- **Stretching rule (NR.7).** The **input text box takes all the free vertical space** of the input area; the buttons keep their places. Send is never scrolled out of view, however long the text.
- The single side button of FR.12.29 (Send, or Stop while a turn runs) keeps its meaning.

**FR.12.32 — Send later and its picks.** *Send later* is in the session header (FR.1.27). The schedule it creates records the **model, effort and profile the user picked** at that moment (only the fields actually picked), and the schedule fires with exactly those picks (FR.14.13). One-shot toggles (New Branch, Auto-Compact) cannot be scheduled: the send is refused with a notice before the time question, and the text and flags stay. Time answers accept `5m`, `2h30m`, `1d`, a clock time, and are limited to **ten years ahead** (`3650d` accepted, `3650d1m` refused; a huge number gets the "too far ahead" message). A time in the spring-forward gap is read an hour later; an ambiguous autumn time is read as its first occurrence.

**FR.12.33 — Send answers key and place** are specified under FR.23.15 and FR.23.14.

**Note on FR.12.29.** It is correct as committed. Only the closing of its open point is new. The WPF Focus view of FR.12.29 means "the Focus column being shown" (FR.23.12).

**FR.12.34 — Model catalogue refresh.** A client can ask the daemon to refresh the model catalogue (local Ollama models) (`models.refresh`).
---

## Amendments and additions (2026-11-18)

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.12.4 Model persistence | FR.12.35 | The model is a setting of the session (`SessionNodeModel.ModelId`), kept from the user's pick. The directory's and the app's `SelectedModelId` are only the fallback. |
| FR.12.12 Effort level selection | FR.12.35 | The effort is a setting of the session (`SessionNodeModel.Effort`), kept from the user's pick. `DirectoryNodeModel.SelectedEffortIndex` is not read. |

### New requirements

**FR.12.35 — The session's model and effort.** A session's model and effort are **its own settings**, kept on the session node, so the command bar shows what a send really uses, also after a restart. This fixes the effort silently reverting to the CLI default: since the switch to the WPF client a pick lived only in the running client, and nothing read `SelectedEffortIndex`. Use cases: [UC.ME.01](/docs/usecases/UC.ME.01-Session-Model-and-Effort.md). Test cases: [TC.ME.01](/docs/testcases/TC.ME.01-Session-Model-and-Effort.md).
- **Storage.**
  - `SessionNodeModel.ModelId` (existing) and `SessionNodeModel.Effort` (new) are both `string?` in `appsettings.json`; `null` means not set.
  - `ModelId = "default"` means "no `--model` flag" and stops the fallback below.
  - For the effort, `default` and `null` both mean no `--effort` flag.
- **A turn's model:** the per-turn pick (`default` = no flag), else the session's `ModelId`, else the directory's `SelectedModelId`, else the app's `SelectedModelId`, else no flag.
- **A turn's effort:** the per-turn pick, else the session's `Effort`, else no flag.
- **One resolver.** One resolver (`ISessionModelSettings`) serves the engine turn (`turn.send`), every headless turn (scheduled, orchestrated, Telegram, web) and compaction. The caller's tier (FR.16.6) uses the same model rule.
- **Sticky picks.**
  - When an interactive send carries a model or effort pick that differs from what the session would use without it, the pick is stored on the session when the send is accepted, and the settings are saved.
  - Interactive sends are the desktop's `turn.send`, the web's `session.send` and `attention.reply`.
  - A pick equal to the session's current value changes nothing. A client that always sends its choice (the web, FR.25.24) therefore doesn't pin a session by accident.
  - Picks of a schedule (FR.14.13), `session.compact` options, Telegram turns and orchestrated turns are never stored.
- **Other writers.** `spawn_session` with `model` and `set_session_model` set `ModelId` (FR.15.10); `create_session` sets both (FR.15.15).
- **What the selectors show.**
  - `session.get` and `session.headers` return in `lastUsed.modelId` and `lastUsed.effort` what a turn without a pick uses: the rules above, `default` when there is no flag, and `default` for a model that isn't in the catalog. These values no longer come from the last ASSISTANT entry (tdcp-spec 10.A9).
  - The profile is unchanged (the pin, FR.12.27).
  - The WPF client still sends only the fields the user picked (tdcp-spec 9.8).
- **Existing sessions.** There is no migration.
  - A session without an `Effort` shows and runs **Default** until the user picks an effort once; that pick then stays.
  - A session without a `ModelId` shows the directory's or the app's model, which is the model its turns already run on.
- **Unchanged.** FR.19.8: the selectors stay disabled while a Persistent Session Process is alive, and a live process keeps the model and effort it was started with. FR.12.15: a change takes effect from the next send.
