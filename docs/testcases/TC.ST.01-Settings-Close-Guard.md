# TC.ST.01 — Close Settings with unsaved changes

Related requirements: FR.4.7, FR.9.19. Use cases: [UC.ST.01](/docs/usecases/UC.ST.01-Settings-Close-Guard.md).

**Legend.** AUTOMATED = view-model tests (VM), STA view tests (WPF), or end to end through `tools\e2e\Run-WpfE2E.ps1`.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.ST.01 | No unsaved changes: closing asks nothing and closes | AUTOMATED (VM) | UC.ST.01 | FR.4.7 |
| TC.ST.02 | Unsaved changes in one or more tabs (pending colours included): the dialog names those tabs | AUTOMATED (VM) | UC.ST.01 | FR.4.7 |
| TC.ST.03 | Save saves every changed tab and closes; a failed save keeps the window open with the error | AUTOMATED (VM) | UC.ST.01 | FR.4.7 |
| TC.ST.04 | Discard stores nothing and closes; Cancel keeps the window and the edits | AUTOMATED (VM) | UC.ST.01 | FR.4.7 |
| TC.ST.05 | Close button, Esc and the window's Closing event (title bar, Alt+F4) all go through the guard | AUTOMATED (STA) | UC.ST.01 | FR.4.7 |
| TC.ST.06 | The main window closing while Settings has edits shows the dialog; Cancel stops both from closing | AUTOMATED (STA) | UC.ST.01 | FR.4.7 |
| TC.ST.07 | End to end: edit a colour, close Settings, choose Save: the colour is stored in the client state | AUTOMATED (E2E) | UC.ST.01 | FR.4.7, FR.9.19 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.4.7 | TC.ST.01–07 |
