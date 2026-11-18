# TC.CL.20 — Colours tab layout and Apply

Related requirements: FR.9.18. Use cases: [UC.CL.04](/docs/usecases/UC.CL.04-Colours-Layout-and-Apply.md).

**Legend.** AUTOMATED = view-model tests (VM), STA view tests with real layout (WPF), or end to end through `tools\e2e\Run-WpfE2E.ps1`.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.CL.20 | Apply is in the theme row, its right edge at the tab's right content edge, right of Reset all | AUTOMATED (STA) | UC.CL.04 | FR.9.18 |
| TC.CL.21 | The gap between the theme row and the first group heading is at most 8 px with no messages shown | AUTOMATED (STA) | UC.CL.04 | FR.9.18 |
| TC.CL.22 | At 900 px width the rows of a group are in two equal columns (row order left then right); at 600 px in one column | AUTOMATED (STA) | UC.CL.04 | FR.9.18 |
| TC.CL.23 | The rightmost swatch / Reset ends at least 8 px before the vertical scrollbar | AUTOMATED (STA) | UC.CL.04 | FR.9.18 |
| TC.CL.24 | Apply commits the open picker's colour, closes the picker and re-applies the palette (application brushes and the transcript's appearance message) | AUTOMATED (VM) | UC.CL.04 | FR.9.18 |
| TC.CL.25 | Apply on the theme that is not active stores the colours and does not change the active theme's brushes | AUTOMATED (VM) | UC.CL.04 | FR.9.18 |
| TC.CL.26 | End to end: the Colours tab shows Apply and two columns; Apply after a change leaves the stored colour in the client state | AUTOMATED (E2E) | UC.CL.04 | FR.9.18 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.9.18 | TC.CL.20–26 |
