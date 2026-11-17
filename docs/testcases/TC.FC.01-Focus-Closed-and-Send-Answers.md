# TC.FC.01 — Focus: Closed Items, Selectable Text, Send Answers

Related requirements: FR.23.20–FR.23.23. Use cases: [UC.FC.01](/docs/usecases/UC.FC.01-Focus-Closed-and-Send-Answers.md).

**Legend.** AUTOMATED = view-model / STA view test; E2E = WPF end-to-end (NR.6, class `WpfFocusE2ETests`); MANUAL = Avalonia (no UI tests).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.FC.01 | Closed ordering (`ClosedUtc`, `UpdatedUtc`, number), outcome lines, Decisions split | AUTOMATED | UC.FC.01 | FR.23.23 |
| TC.FC.02 | Empty state with only Decided or closed items; expanders still show | AUTOMATED | UC.FC.01 | FR.23.23 |
| TC.FC.03 | Expanders collapsed by default, kept on refresh, reset by Focus-selected and by a new session | AUTOMATED | UC.FC.01 | FR.23.23 |
| TC.FC.04 | Body toggle; an item that closes moves to Closed | AUTOMATED | UC.FC.01 | FR.23.23 |
| TC.FC.05 | An answered item moves to the collapsed Closed (1); expanding shows `Answered: …` | E2E | UC.FC.01 | FR.23.23 |
| TC.FC.06 | Text of open, Decided and closed cards is selectable; drag in a closed body does not collapse | AUTOMATED | UC.FC.02 | FR.23.20 |
| TC.FC.07 | Send answers is in the header, hidden in Session, not inside the Focus panel | AUTOMATED | UC.FC.03 | FR.23.21 |
| TC.FC.08 | Ctrl+Enter sends; empty boxes: nothing and no newline; Enter, Shift+Enter, Ctrl+Shift+Enter do not send | AUTOMATED | UC.FC.03 | FR.23.22 |
| TC.FC.09 | The header button marks the item Answered in the ledger (UIA name "Send answers") | E2E | UC.FC.03 | FR.23.21, FR.23.15 |
| TC.FC.10 | Avalonia: button left of the search box; Ctrl+Enter in Decisions cards; narrow window truncates the title first | MANUAL | UC.FC.03 | FR.23.21, FR.23.22 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.23.20 | TC.FC.06 |
| FR.23.21 | TC.FC.07, 09, 10 |
| FR.23.22 | TC.FC.08, 10 |
| FR.23.23 | TC.FC.01–05 |
