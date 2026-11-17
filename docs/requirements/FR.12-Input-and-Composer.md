# FR.12 — Input Command Bar and Model Selection, composer layout (batch 2026-11-17)

> **Staging file, phase 1.** New and amended text only. Use cases: [UC.SH.01](/docs/usecases/UC.SH.01-Window-Chrome.md) (composer part). Test cases: [TC.SH.01](/docs/testcases/TC.SH.01-Window-Chrome.md). New IDs start at **FR.12.30** (FR.12.29, collapsible input area, is already committed and unchanged; see the note at the end).

## Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.12.1 / FR.12.2 Input area and command bar | FR.12.30, FR.12.31 | Toggle colours, spacing, icon-only side buttons, the input box takes the free height. |
| FR.12.29 Collapsible input area | (note) | Its "Open point for the Architect" (which client builds it first) is closed: both clients have it. |

## New requirements

**FR.12.30 — Toggle and button colours.** Toggle buttons of the command bar (Streaming, Sound, Auto-Commit and the like; checked or not) use the **regular button colours** of the theme, with a distinct checked state of the same family. **Only Send** uses the accent colour. No other control of the composer is accent-filled. A **margin** separates the **Profile** selector from the **Streaming** toggle (the same gap as between the other selectors).

**FR.12.31 — Composer layout (WPF).**
- On the selector line, **Streaming and Sound are icons** (tooltip carries the text). The side buttons beside the input (File, Send, Stop, Schedules, Settings) are **icon-only**; **Send fills the height** of its column.
- **Stretching rule (NR.7).** The **input text box takes all the free vertical space** of the input area; the buttons keep their places. Send is never scrolled out of view, however long the text.
- The single side button of FR.12.29 (Send, or Stop while a turn runs) keeps its meaning.

**FR.12.32 — Send later and its picks.** *Send later* is in the session header (FR.1.27). The schedule it creates records the **model, effort and profile the user picked** at that moment (only the fields actually picked), and the schedule fires with exactly those picks (FR.14.13). One-shot toggles (New Branch, Auto-Compact) cannot be scheduled: the send is refused with a notice before the time question, and the text and flags stay. Time answers accept `5m`, `2h30m`, `1d`, a clock time, and are limited to **ten years ahead** (`3650d` accepted, `3650d1m` refused; a huge number gets the "too far ahead" message). A time in the spring-forward gap is read an hour later; an ambiguous autumn time is read as its first occurrence.

**FR.12.33 — Send answers key and place** are specified under FR.23.15 and FR.23.14.

**Note on FR.12.29.** It is correct as committed. Only the closing of its open point is new. The WPF Focus view of FR.12.29 means "the Focus column being shown" (FR.23.12).
