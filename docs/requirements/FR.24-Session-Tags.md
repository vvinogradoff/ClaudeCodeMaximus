# FR.24 — Session Tags (batch 2026-11-17)

> **Staging file, phase 1.** New and amended text only. Use cases: [UC.TA.01](/docs/usecases/UC.TA.01-Tag-Area.md), [UC.LP.01](/docs/usecases/UC.LP.01-Left-Panel-Lists.md). Test cases: [TC.TA.01](/docs/testcases/TC.TA.01-Tag-Area.md), [TC.LP.01](/docs/testcases/TC.LP.01-Left-Panel-Lists.md). New IDs start at **FR.24.16** (FR.24.16–FR.24.18).

> **About FR.24.16.** Report 452 *proposed* a "FR.24.16 — Tag area modes (mini/full), persisted". It was **never entered into the docs** (FR.24 ends at FR.24.15), and the Architect replaced that design, so the number is used for the new rule below. The proposal of 452 is **superseded** and must not be entered anywhere. Its test-case proposal (`TC.TAGS.16`) is replaced by `TC.TA.01`.

## Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.24.7 Tag row (UI) | FR.24.16 | The row's height behaviour is now the collapsed/expanded rule. |
| FR.24.11 Tags tab: search | FR.1.24, FR.24.17 | Scope, wildcards and empty-result text. |
| FR.24.7 / FR.24.16 chip size | FR.24.18 | Chip font size is a setting; the mini line follows it. |

## New requirements

**FR.24.16 — Tag area: collapsed by default, expand on click (both clients).**
1. **Collapsed by default.** The tag area of a session shows **one line** of chips; overflow is hidden without a scrollbar, with a `+N` hint at the right end while chips are hidden (the hint never covers a chip).
2. **Expand.** A **click on empty space** of the tag area (or on the `+N` hint) **expands** it to up to **10 lines**, then it scrolls vertically. A click on a chip keeps its meaning (opens its link at once).
3. **Collapse.** A **double-click on empty space** of the area collapses it. A press on the area's scrollbar neither collapses nor is swallowed.
4. **Session/Focus switch.** Switching between Session and Focus of the same session **keeps** the state.
5. **Switching to another session resets it to collapsed.**
6. **Nothing persists.** The state is saved neither in the client state nor in the daemon; a restart starts collapsed.
7. With no tags the area is empty and expanding does nothing.
8. The area exposes an expand/collapse action for accessibility (WPF: UI Automation ExpandCollapse, name "Session tags").

Applies to both clients named by the Architect, **WPF and Avalonia** (the two that have the tag font size setting, FR.24.18). *Whether Tessyn Web follows the same rule is not stated; phase 1 text had assumed so. To confirm.*

**FR.24.17 — Tags tab results.** The tab finds tags: the search rules, the scope and the empty text are FR.1.24. With the real index (hundreds of chips) the chips wrap and scroll; chips and the session list share the height as FR.1.23 says.

**FR.24.18 — Tag cloud font size (both clients).** A setting, **Tag cloud font size**, sets the text size of the tag chips. It exists in **both clients**: WPF under Settings › **Appearance**, Avalonia in its settings window.
- **Applies live.** Changing it restyles every visible chip at once, in the tag area (FR.24.16) and in the Tags tab chips (FR.24.11), with no restart and no reopening of the session.
- **The mini line follows the chip size.** The collapsed one-line area (FR.24.16 item 1) is exactly one chip line high at the chosen size, so a larger size never clips a chip and a smaller one leaves no empty band. The 10-line limit of the expanded area and the `+N` hint follow the same chip height.
- The value is a client setting (not per session, not in the daemon); each client keeps its own.
- The allowed range and the default are those of the setting in the client *(to be filled from the code in phase 2: not found in the main tree at the time of writing)*.
