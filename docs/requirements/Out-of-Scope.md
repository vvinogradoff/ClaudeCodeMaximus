> Area file of the requirements (index: [README](README.md)).

## Out of Scope (Initial Version)

- Session sharing or sync across machines
- Diff or branching of session history
- Deletion of session files from within the UI
- Plugin system
- Bridging to or reading claude.ai's own MCP connector configuration (FR.20.8) — servers usable only through claude.ai (e.g. third-party connectors not owned by the user) are intentionally left out of the External MCP Server Registry
- A blocking "ask and wait" Telegram tool (FR.21.3) — ruled out as unreliable (connectivity, user availability); `notify_user` plus plain-text follow-up replies covers the same need without a stuck-turn risk
- Closing/archiving Telegram topics when a session is deleted or hidden, multiple bots/chats, and Telegram-side message editing/deletion sync — not needed for the initial bridge and may be revisited later
- A public webhook receiver / daemon↔web relay for Telegram (the S3 topology sketched in `docs/refactoring/tdcp-spec.md` §3) — long polling (FR.21.4) already meets the latency need with zero fleet infrastructure; explicitly parked as a later phase, not built alongside FR.21.9–FR.21.11. **(amended 2026-11-12, FR.25):** the S3 relay is now built for the web client (FR.25.01, FR.25.35); Telegram stays on long polling and does not use it.
- Rate limits or cooldowns on `notify_user`, quiet hours, presence-aware suppression (no phone ping while the user is active at the desktop), and redaction of secrets in mirrored Telegram text — considered and declined in ISSUE.012: quiet hours are handled in the phone's notification settings, and anything agents touch is treated as exposed
- The Focus view's cross-session Attention list, Telegram answering and `/attention` command, local-LLM item extraction, native `AskUserQuestion` capture and TDCP ledger methods — see the out-of-scope list at the end of FR.23
- The out-of-scope list of the web client — second user, second factor, phone push notifications, instruction toggles, profile management, tree editing, native iOS app — see FR.25.39
- Detecting Session Tags from conversation text, adding/removing tags by hand in the UI, Phase 2 status colours (FR.24.9), multiple focus panels, related tags between Linear issues and pull requests (FR.24.14), TDCP methods and owner-less PR references — see FR.24.10

---

## Dropped with the previous desktop client (2026-11-17)

On 2026-11-17 the Architect made WPF the only desktop client. The previous desktop UI is retired; the project `code/TessynDesktop` remains only as the daemon host (`TessynDesktop.exe --background`, FR.32.1). Nothing below was silently lost. **For the Architect to confirm** (the full list with reasons is in `.wpf-work/494-sunset-docs.md`):

- The **`--chrome` browser-control toggle** in the command bar (Claude drives Chrome through the Claude-in-Chrome extension). WPF has no such toggle; the replacement is the reserved embedded browser pane (FR.27, `docs/refactoring/browser-control-cdp-plan.md`). The Core still accepts the `Chrome` turn setting.
- **Takeover of a background host by a visible window** of the previous client, and the "second visible window is refused" check (parts of FR.32.2 and FR.32.6; UC.HS.02, UC.HS.03 A1–A2; TC.HS.08).
- **Key bindings without a WPF command**: they were dropped by the one-time import (FR.32.4). The import itself is WITHDRAWN 2026-11-17 (decision A47): the previous client's settings are no longer imported at all.
- **The second tag cloud default (11) and its apply-on-Save behaviour** (FR.24.18).
- **Placement of "Send answers (n)" left of the search box** and the previous header order (FR.23.12, FR.23.21; TC.FC.10, TC.FV.20).
- **Manual test cases written for the previous window**: TC.TA.10, TC.TA.14, TC.FC.10, TC.FV.20, TC.TG.19, TC.TG.20, TC.TG.21, TC.HS.08. Each ID is kept and marked withdrawn; where WPF has the same behaviour the case points to its WPF equivalent.
- **Self-update of the previous client** (FR.8.1–8.5 as written for it): the WPF client's rules are FR.32.3, see the note in FR.8.
