# tdcp-spec additions (batch 2026-11-17)

> **Staging text for `docs/refactoring/tdcp-spec.md`.** That file has uncommitted edits in the main tree (decision A38), so it is **not touched** in phase 1. Phase 2 puts each block into the section named. All additions are additive; older clients ignore the new members.

## A1 — Entry source (new subsection after §9.4) — FR.2.6
`SessionEntryDto` gains an optional `source` (`"scheduled" | "orchestrated" | "telegram" | "web"`), present on `session.entries`, `entries.appended` and every path that returns entries; omitted for entries typed by the desktop user and for non-USER roles. A USER entry stored without a source whose content starts with `[Scheduled]` is reported as `"scheduled"` (resolved on read from the full content; never written back).

## A2 — Resumable flag and worker-only groups (`SessionStateDto`, `TreeGroupDto`) — FR.1.26
| Field | Type | Meaning |
|---|---|---|
| `session.state.resumable` | bool | The Claude session can be resumed. Omitted until the first sweep has run or for a session the sweep did not see. Filled from one cached batch sweep per 60 s. |
| `group.holdsOnlyWorkers` | bool | Always present on groups. True when the group held at least one session, all of them Workers (so none is listed), and all its child groups are flagged too. A group the user created and left empty is never flagged. |
`session.changed` is raised for each session whose `resumable` flips; the first sweep sets the baseline and raises nothing. Golden files affected: `tree.snapshot`, `tree.workers`, `recent.list`.

## A3 — Pinned profile (`SessionStateDto`, also `session.changed`, `tree.snapshot`, `tree.workers`) — FR.11.14
| Field | Type | Meaning |
|---|---|---|
| `pinnedProfileId` | string | `"default"` or a stored profile id. Omitted while the session has no pin. |
| `pinnedProfileName` | string | Display name as `list_sessions` prints `profile=` (the Default profile is `"default"`; a pin whose profile was removed carries ` (missing)`). Omitted without a pin. |
| `switchingToProfileName` | string | The profile a queued `profile.switch` moves the session to when its running turn ends. Omitted when none is queued. |
`profileName` keeps its meaning (profile stamped on the last user entry) and is not updated by a switch. `session.changed` is published after `profile.switch` (for every session it touched, queued ones included), when a queued switch is applied or dropped, and when a turn records a different profile.

## A4 — `schedules.add` picks (§ Schedules) — FR.14.13
`schedules.add` params gain optional `profileId`, `modelId`, `effort` (same values and validation as `session.send`; `"default"` is the Default profile / no flag). When the schedule fires, the turn runs with exactly those picks; an absent field is not sent. An unknown model, effort or profile is `-32602` at add time. If a pick no longer exists at fire time the turn runs on the session's own settings and the supervisor is notified through the mailbox. `ScheduleDto` gains `profileId?`, `modelId?`, `effort?` (null = none picked).

## A5 — Settings administration (addition to "Settings, profiles and sounds (package J3, loopback only)") — FR.20.10, FR.21.25, FR.25.41, FR.12.34
Part 2 adds `mcp.list`, `mcp.upsert`, `mcp.remove`, `mcp.setEnabled`, `mcp.discover`, `mcp.authorize`, `telegram.status`, `telegram.setToken`, `telegram.clearToken`, `relay.status`, `relay.configure`, `models.refresh` and the notifications `mcp.changed` and `catalog.changed`. All are loopback-only; the relay answers `-32601` and never carries the two notifications. Capabilities: `mcp.registry`, `telegram.settings`, `relay.settings`, `models.admin`. Secrets (MCP header values, OAuth client secrets and tokens, the Telegram bot token, the relay link token) are write-only: results say `hasStaticHeaderValue`, `hasClientSecret`, `hasToken`; a server URL is shown without user info, query and fragment (`urlHasQuery`); stdio arguments only as `hasArguments`. In `mcp.upsert` a member left out keeps its value and an empty string clears it; changing a server's origin deletes its stored token and requires secrets again; `relay.configure` requires `linkToken` again when the URL moves to another origin. `mcp.discover` is stopped after 15 s and answers `timedOut: true` with what it found. `mcp.authorize` answers when the browser flow ends (up to 2 minutes), one flow per server. `settings.get` also serves the read-only section `avalonia-ui` (theme, colours, font sizes, key bindings, screensaver), version 1, no `settings.changed`; `settings.update` refuses it with `-32602`.

## A6 — Glossary terms (for `docs/glossary.md`, also uncommitted in the main tree)
*Write-only secret* — a value a client can set but never read; results carry a `has…` flag. *Origin binding* — a stored secret is dropped, or must be sent again, when the URL or token endpoint it was given for changes origin. *avalonia-ui section* — the read-only settings section for the one-time import of the Avalonia client's settings. *Recency box* — the tinted background of a session row (FR.9.10) that also holds its date text and worker count. *Husk group* — a Group that held only Workers (`holdsOnlyWorkers`, FR.1.26). *Tag area* — the chip row under the session header (FR.24.16). *Host (background host)* — the windowless daemon process (FR.32.1).
