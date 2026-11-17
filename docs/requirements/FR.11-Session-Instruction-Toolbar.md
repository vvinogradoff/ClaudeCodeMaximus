> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.11 — Session Instruction Toolbar

The application header bar provides per-session instruction toggles that modify how the application interacts with Claude without polluting the visible conversation.

**FR.11.1 — Toolbar layout:** A horizontal row of icon toggle-buttons is positioned in the application title bar, to the right of the theme selector (day/night toggle). The toolbar contains five controls, left to right: Auto-Commit, New Branch, Auto-Document, Auto-Compact, and Clear. The buttons reflect the state of the **currently selected session** — switching sessions updates the button states. When no session is selected, the buttons are disabled.

**FR.11.2 — Instruction injection:** The application appends a session-preferences block to the prompt sent to the `claude` process whenever there is at least one active instruction to convey (Auto-Commit in the Commit or Do-not-commit state, New Branch, Auto-Document, or the FR.14.11 redirect). When Auto-Commit is in its "No preference" state and no other toggle is active, **no block is appended at all**. These instructions are:
- **Not shown** in the user message bubble in the output window
- **Not stored** in the session file (the session file records only the clean user prompt)
- Appended as a clearly delimited block after the user's message text when written to `claude` stdin, worded as a genuine preamble (not as a directive to conceal itself from the model)
- When `AgentToolsEnabled` is true **and** the session is in One-shot Mode, the block additionally includes the native-scheduling redirect defined in **FR.14.11**. As of **FR.19.10**, the redirect is **not** emitted when the session is in Streaming Session Mode (FR.19) — its rationale (the CLI process dies between turns) does not hold for a persistent process. The `NoWorkflowTool` redirect (session-orchestration tools) is unconditional on mode and continues to be emitted in both.

**FR.11.3 — Auto-Commit toggle (tri-state):**
- **Type:** Sticky three-state toggle (persists across prompts until the user changes it). Clicking cycles **Do-not-commit → Commit → No preference → Do-not-commit**.
- **Commit instruction:** `"Once you have completed the request, commit all your changes to git with a concise commit message."`
- **Do-not-commit instruction:** `"Do not commit any changes to git."`
- **No preference:** No commit-related instruction is emitted at all — the block is silent on committing (this state removes what was previously the only always-injected instruction).
- **Persistence:** Per-session; toggle state (`true` = Commit, `false` = Do-not-commit, absent/`null` = No preference) stored on the session node in `appsettings.json` so it survives app restarts. Different sessions may have different auto-commit states. New sessions default to Do-not-commit (`false`).
- **Icon:** Three distinct glyph/color states for Commit, Do-not-commit, and No preference (see FR.11.9).

**FR.11.4 — New Branch toggle:**
- **Type:** One-shot toggle (auto-unsets after the prompt is sent and its value consumed)
- **ON instruction:** `"Create a new git branch before committing your changes."`
- **Behavior:** When the prompt is sent, the toggle value is read, included in instructions, then the toggle automatically resets to OFF
- **Icon:** Git branch icon

**FR.11.5 — Auto-Document toggle:**
- **Type:** Sticky toggle (persists across prompts)
- **ON instruction:** `"After completing the request, update any relevant requirements documents and/or architecture documents in the project's /docs directory to reflect the changes you made. If any new domain terms were introduced or existing terms changed, also update /docs/glossary.md."`
- **Behavior:** The instruction is injected into the prompt but, like all instruction toggles, is neither shown in the output window nor stored in the session file
- **Persistence:** Per-session; stored on the session node in `appsettings.json`
- **Icon:** Document/pencil icon

**FR.11.6 — Auto-Compact toggle:**
- **Type:** One-shot toggle (auto-unsets after the compaction completes)
- **Empty-prompt shortcut:** Auto-Compact can be triggered with no input text — submitting with the toggle ON and an empty message skips the normal turn entirely and goes straight to the compaction prompt, resetting the toggle afterward.
- **Behavior:** When ON and Claude finishes responding to the user's prompt, the application automatically sends a **separate follow-up prompt** to Claude instructing it to compact the session. The follow-up prompt is:
  The compaction prompt instructs Claude to: preserve decisions and reasoning, architecture choices, user attribution, **all URLs (full or partial)**, **all secrets/API keys/tokens/credentials/connection strings** (always preserved verbatim, no exceptions), and **file names/paths when the surrounding context is kept** (only drop a file name if the entire segment referencing it is removed); remove transient debugging steps, meta-instructions, and redundant corrections; restructure user inputs by semantic grouping (merge related follow-ups, only split on topic change); **normalize terminology per the project glossary** (`docs/glossary.md` — attached to the prompt when available); flag new terms with `[NEW TERM]` and update the glossary; and output in session file format with `[timestamp] ROLE` headers.
- **Transcript is embedded directly, not resumed:** The compaction prompt includes the full transcript to compact inline (`SESSION TRANSCRIPT TO COMPACT:` section) — USER/ASSISTANT entries from the text Session File since the last `[COMPACTION]` marker (or the whole file if none exists yet), each formatted as `[timestamp] ROLE` plus content. The compaction call itself is **stateless** (`sessionId: null`, no `--resume`) — Claude gets all conversation content from the embedded transcript, never from resuming a prior Claude session. This is deliberate: because a session's Claude Code JSONL history can be split or incomplete across Cross-Profile Session Adoption (FR.11.13) hops or mixed Anthropic/local-Ollama turns (Model Provider), the plain-text Session File — always complete regardless of profile or model-provider switches — is the only reliable source for what to compact. `--resume` is not used for compaction and no cross-profile lookup is needed here (unlike interactive sends and headless turns, which still need it — see FR.11.13).
- **Nothing-to-compact guard:** If the built transcript is empty (e.g. the empty-prompt shortcut fires before any turn exists yet), no CLI call is made — a `[Nothing to compact]` system message is shown instead.
- **Post-compaction:** The compacted text returned by Claude replaces the session file content (rewritten, not appended). The Messages collection in the output window is also updated to reflect the compacted content. **After compaction completes, the JSONL session is automatically detached (FR.11.8).** The next prompt will use the compacted text session as context via `BuildContextPreamble` instead of `--resume`.
- **Failure safety:** The compaction turn's stream events are monitored for any error (e.g. an invalid-credentials/reauth failure, a non-zero `claude` exit code, or any other error event) as well as for a wholly empty result. If either condition occurs, the session file and JSONL are **left completely untouched** — no rewrite, no detach — even if some assistant text happened to be captured before the error. A system message (`[Compaction failed — session left unchanged: <error>]` or `[Compaction returned no content — session left unchanged]`) is shown in the Output Panel so the failure is visible instead of silent. If the error is an invalid-credentials failure, `HasAuthError` is set so the Reauthenticate button appears, matching the behavior of a normal turn's auth failure (FR.12.16).
- **Auto-reset:** The toggle resets to OFF after the compaction prompt completes
- **Shared Core service (amended 2026-11-12, FR.25):** The compaction run (prompt composition, the stateless CLI call, the guards, the rewrite of the session file, the detach and the system messages) now lives in the Core service `ISessionCompactionService`, shared by the desktop's Auto-Compact and by Tessyn Web's **Compact session** (FR.25.26). Behaviour is unchanged. The service runs under the per-node turn lock (FR.14.9), sets the running flag (FR.1.8) for its duration, and raises `SessionFileRewritten` after a successful rewrite (FR.25.34).
- **Icon:** Compress/shrink icon

**FR.11.7 — Terminate Session button:**
- **Type:** Action button (not a toggle); previously called "Clear"
- **Precondition:** Only active when the current session has a live `ClaudeSessionId`. Disabled otherwise.
- **Behavior:** When clicked, shows a **confirmation dialog** asking the user to confirm the intention to terminate the session.
- **On confirm:** The JSONL session is **immediately detached** (FR.11.8) — no prompt is sent to Claude. A system message `[Session detached — next prompt will use text session as context]` appears in the Output Panel.
- **On cancel:** No action taken.
- **Effect:** The `ClaudeSessionId` is moved to `PriorClaudeSessionIds` so the JSONL remains visible in JSONL view mode but is no longer used for `--resume`. The next prompt will use the text session as context via `BuildContextPreamble`.
- **Icon:** Broom/clear icon (&#x2672;)

**FR.11.8 — JSONL Session Detachment:**
- The JSONL session is detached by: moving `ClaudeSessionId` to `PriorClaudeSessionIds` and setting `ClaudeSessionId = null`.
- Detachment occurs in two situations: (1) after Auto-Compact completes, (2) when the user confirms the Terminate Session button.
- After detachment, the JSONL file still exists and remains accessible via the JSONL view toggle, but `--resume` is no longer used.
- The next prompt after detachment proactively uses `BuildContextPreamble` to feed the stored text session history to Claude as context (FR.11.10), unless Cross-Profile Session Adoption (FR.11.13) locates the JSONL under a different profile first — that path re-attaches the session and resumes it natively instead.

**FR.11.9 — Toggle state display:** Active toggles shall be visually distinct (e.g., highlighted background or accent border) so the user can see at a glance which instructions will be injected into the next prompt. Auto-Commit, being three-state, shows three distinct visuals — one each for Commit, Do-not-commit, and No preference — so the current state is unambiguous at a glance.

**FR.11.10 — Instruction block format:** The instruction block is appended to the user's message in `claude` stdin whenever at least one instruction is active (see FR.11.2 — it is omitted entirely when Auto-Commit is "No preference" and no other toggle is active). The format is:
```

---
Session preferences — apply these if relevant to our work here:
- <instruction 1>
- <instruction 2>
...
```
The block is separated from the user's message by a blank line and a `---` delimiter. The preamble reads as ordinary guidance to the model rather than an instruction to conceal itself from the user.

**FR.11.11 — Proactive context reload:** When `SendAsync` detects that the session file contains history but `ClaudeSessionId` is null (e.g., after detachment), the user's message is wrapped with `BuildContextPreamble` before being sent — without waiting for a "No conversation found" error. This ensures continuity after session detachment or compaction.

**FR.11.12 — Mid-run toggle corrections:** When the user toggles Auto-Commit, New Branch, Auto-Document, or Auto-Compact while Claude is actively processing a prompt (`IsBusy` is true):
- A system message is shown in the output panel: `[{ToggleName} was {enabled|disabled|set to no preference} for this run]`
- For New Branch and Auto-Document: a follow-up prompt is sent to the active Claude session with the new instruction (enable) or a correction telling Claude to ignore the previous instruction (disable). These correction prompts are fire-and-forget and do not appear in the session file or UI beyond the system status message.
- For Auto-Commit (tri-state): a follow-up correction prompt is sent reflecting whichever of the three states was just selected — Commit, Do-not-commit, or a correction telling Claude to disregard any earlier commit-related instruction (No preference).
- For Auto-Compact: no prompt is sent (compaction happens post-response). The mid-run state is tracked and used when deciding whether to compact after the response completes. If the user enables then disables auto-compact during a single run, the final state at response completion determines behavior.

**FR.11.13 — Cross-profile session adoption:** Each profile stores its Claude Code session JSONL files under its own isolated `CLAUDE_CONFIG_DIR` (`%AppData%\TessynDesktop\profiles\profile_N\projects\<slug>\`, or `~\.claude\projects\<slug>\` for the Default profile). A session created under one profile is therefore invisible to `--resume` when a different profile is active, even though its JSONL still exists — Claude Code reports "No conversation found" and, for Auto-Compact specifically, produces nothing to compact.
- **When it runs:** Before any `--resume` attempt that carries a `ClaudeSessionId` — interactive sends (FR.11.11's proactive path) and headless/orchestrated turns (FR.14.9). Auto-Compact does **not** use this — its compaction call is stateless and embeds the transcript directly instead of resuming anything (FR.11.6).
- **Lookup:** The application derives the project slug from the working directory (same algorithm as FR.13.2) and first checks whether `<slug>/<ClaudeSessionId>.jsonl` already exists under the currently active profile's session store. If not, it searches every other known profile — the Default profile's `~/.claude` plus every directory under the profiles root, including profiles marked removed (`_rem` suffix, see the Removed Profile glossary entry) — for a matching file.
- **Adoption is a move, not a copy:** When found under a different profile, the JSONL file is **moved** into the active profile's session store (creating the destination `projects/<slug>/` directory if needed). The session becomes owned by whichever profile most recently used it; no stale duplicate is left behind under the original profile, since a copy would immediately go stale the moment the active profile continues the conversation.
- **Fallback:** Only when the session cannot be found under any profile does the application fall through to the existing `BuildContextPreamble` text-reconstruction path (FR.11.11 for interactive turns).
- **Scope:** `ClaudeSessionId` itself is unchanged by adoption — only its backing JSONL file is relocated. `PriorClaudeSessionIds`, session titles, and all other node state are unaffected.

---

## Amendments and additions (batch 2026-11-17)

**FR.11.14 — Profile shown on a session row.** A session row (FR.1.20) shows, at the left of its second line, **the profile the session is pinned to** (`pinnedProfileName`; the Default profile reads `default`; a pin whose profile was removed reads `<name> (missing)`). While a profile switch is queued (it applies when the running turn ends) the row shows `<pinned> -> <next>` (`switchingToProfileName`). A session without a pin falls back to the profile stamped on its last prompt (`profileName`). The daemon publishes `session.changed` when the pin or the queued switch changes. Wire: `tdcp-spec` additions §A3.
