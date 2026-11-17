# FR.23 — Focus View (batch 2026-11-17)

> **Staging file, phase 1.** New and amended text only. Use cases: [UC.FC.01](/docs/usecases/UC.FC.01-Focus-Closed-and-Send-Answers.md). Test cases: [TC.FC.01](/docs/testcases/TC.FC.01-Focus-Closed-and-Send-Answers.md). New IDs start at **FR.23.20**. FR.23.19 (reply drafts, commit `bfc920f`) and FR.12.29 were checked against the reports 453a–453e: **no change needed**, except the status line below.

## Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| **FR.23.14** item 1 | FR.23.21 | **"Send answers (n)" moves out of the Focus list into the session header.** The list now starts with Open items; items renumber (Open 1, Decisions 2, Closed 3, Empty state 4). |
| FR.23.12 Placement | FR.23.21 | The Avalonia header order becomes `[Send answers (n)] [Search box] [Session][Focus <badge>] [MD] [JSONL]`, with Send answers shown only in Focus. WPF: left of the Session/Focus switch (its find bar is a separate row). |
| **FR.23.15** | FR.23.22 | Adds Ctrl+Enter. |
| FR.23.14 items 3–5, "Not built yet" status line | FR.23.23 | The WPF Closed history, the collapsed Decisions expander and the empty state are **built** (WPF, 2026-11-17); the "Not built yet" sentence for them is deleted. Still not built in WPF: a cross-session Focus list, the media-allowlist Settings screen. |
| FR.2.3.2 | FR.23.20 | Text selectability reaches the Focus cards. |

## New requirements

**FR.23.20 — Selectable Focus text, open and closed.** On every Focus card, **open, Decided and closed**, all rendered text is **selectable and copyable**: the id, the title, the body, the outcome line (`Answered: …`), the options' text and the reply box. A press that ends a selection does not trigger a card action. On a closed card the click that **expands the body** is on the header row; a text drag inside the body never collapses it.

**FR.23.21 — "Send answers (n)" in the header.** The button lives in the **session header**, visible **only while Focus is selected** (hidden in Session), always visible (it never scrolls with the list). It is enabled when n ≥ 1 reply boxes hold text and the session can send (the rule of the main Send button). Avalonia: immediately **left of the search box**. WPF: left of the Session/Focus switch. Its UI Automation name stays **"Send answers"**.

**FR.23.22 — Ctrl+Enter sends the answers.** **Ctrl+Enter in any card's reply box** (including Decisions cards) does exactly what the button does: the same command, the same mark-first order, the same enabling rule (nothing happens, and no newline is inserted, when n = 0). **Plain Enter inserts a newline**; Shift, Alt and Ctrl+Shift chords are not shortcuts.

**FR.23.23 — Closed, Decisions and the empty state in WPF.**
- *Decisions (n)* is a collapsed expander, shown only when n > 0, holding open Decided cards (full, answerable).
- *Closed (n)* is a collapsed expander, newest closed first (`ClosedUtc`, then `UpdatedUtc`, then number). Cards are compact: header row (kind chip, id, title, posted time) and the outcome line; the body is hidden until the header row is clicked. Body toggles survive refreshes.
- Both expanders **collapse for every session** and **each time Focus is selected**; a refresh leaves them alone.
- The **empty state** shows when no item other than Decided is open (`Nothing needs your attention in this session.`); the expanders still show.
- Header rows of open cards also show the **posted time**.
