# FR.11 — Session Instruction Toolbar and profiles, addition (batch 2026-11-17)

> **Staging file, phase 1.** One new requirement, from report 442b (W5). It extends FR.11 (profiles) and ISSUE.013 (profile switch). New ID: **FR.11.14**.

**FR.11.14 — Profile shown on a session row.** A session row (FR.1.20) shows, at the left of its second line, **the profile the session is pinned to** (`pinnedProfileName`; the Default profile reads `default`; a pin whose profile was removed reads `<name> (missing)`). While a profile switch is queued (it applies when the running turn ends) the row shows `<pinned> -> <next>` (`switchingToProfileName`). A session without a pin falls back to the profile stamped on its last prompt (`profileName`). The daemon publishes `session.changed` when the pin or the queued switch changes. Wire: `tdcp-spec` additions §A3.
