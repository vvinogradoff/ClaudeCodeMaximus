# FR.14 — Scheduled Turns (batch 2026-11-17)

> **Staging file, phase 1.** New and amended text only (report 466b). New ID: **FR.14.13**. Wire: `tdcp-spec` additions §A4.

**FR.14.13 — Picks on a schedule (A14).** A schedule may carry `profileId`, `modelId` and `effort`, the values the user picked when the send was scheduled.
- **Validation at add time.** The same resolver as `session.send` checks them: an unknown model, effort or profile is rejected at once (`-32602`) and nothing is stored.
- **Firing.** The turn runs with **exactly** the stored picks; a field that is absent is **not sent**, so the session's own resolution applies to it. Mailbox deliveries never carry picks. Schedules stored before the fields existed have none and behave as before.
- **A pick that no longer resolves** (profile deleted, model left the catalog): the turn **still runs** on the session's own settings and the supervisor gets a mailbox note; the message is not lost. *(Decision flagged for the Architect: the alternative is to refuse the turn, as for a pin to a missing profile, ISSUE.013.)*
