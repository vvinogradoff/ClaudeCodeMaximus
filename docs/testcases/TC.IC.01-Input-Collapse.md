# TC.IC.01 — Collapsible Input Area

Related requirements: FR.12.29 in [requirements.md](/docs/requirements.md). Related use cases: [UC.IC.01](/docs/usecases/UC.IC.01-Input-Collapse.md).

**Legend.** VM = view-model test (`InputAreaViewModelTests`). VIEW = test of the real session pane on an STA thread (`InputAreaViewTests`). E2E = end-to-end test in the WPF E2E project (`WpfInputAreaE2ETests`), run only through `tools\e2e\Run-WpfE2E.ps1`.

## Summary

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.IC.01 | Default: Session view expanded | VM | UC.IC.02 | FR.12.29 |
| TC.IC.02 | Default: Focus view collapsed | VM, E2E | UC.IC.02 | FR.12.29 |
| TC.IC.03 | Toggle collapses and expands; height restored | VM, VIEW | UC.IC.01 | FR.12.29 |
| TC.IC.04 | Switching session resets to the landing view's default | VM | UC.IC.03 | FR.12.29 |
| TC.IC.05 | Switching view in one session applies the default of the view entered | VM | UC.IC.02 | FR.12.29 |
| TC.IC.06 | State is not persisted (the selection neither) | VM | UC.IC.03 | FR.12.29 |
| TC.IC.07 | Collapsed side column: one button, Send or Stop while a turn runs | VM, VIEW | UC.IC.01 | FR.12.29 |
| TC.IC.08 | Collapsed with command bar shown: bar only | VIEW | UC.IC.01 | FR.12.29, FR.12.11 |
| TC.IC.09 | Collapsed with command bar hidden: one editable text line | VIEW | UC.IC.01 | FR.12.29 |
| TC.IC.10 | Double click on the splitter toggles; drag from collapsed expands | VIEW | UC.IC.01 | FR.12.29 |
| TC.IC.11 | Collapsing keeps draft text and command bar visibility | VM | UC.IC.01 | FR.12.29 |
| TC.IC.12 | Settings toggle shows and hides the command bar; remembered across restarts | VM, VIEW, E2E | UC.IC.04 | FR.12.29, FR.12.11 |
| TC.IC.13 | Session/Focus switch: Focus replaces the transcript, one side checked | VIEW, E2E | UC.IC.02 | FR.23.12 |
| TC.IC.14 | Selection kept per session; reopening the open session changes nothing | VM | UC.IC.03 | FR.23.12, FR.12.29 |
| TC.IC.15 | Focus badge only on the Session view; an entry reference leaves Focus | VM | UC.IC.02 | FR.23.13, FR.23.18 |
| TC.IC.16 | The height the user dragged to is remembered across a collapse | VIEW | UC.IC.01 | FR.12.29 |

## Cases

- **TC.IC.03** — Expanded at height H (dragged); double click: collapsed, row at its content height; double click: expanded at H.
- **TC.IC.04** — In the Focus view of A expand by hand, select B (Session, new): expanded; select Focus in B: collapsed; return to A: Focus, collapsed. Repeat for the Session view with a manual collapse: expanded on return.
- **TC.IC.07** — Idle: Send only. Turn running: Stop only. Never two buttons. Expanded: Send, plus Stop while a turn runs.
- **TC.IC.10** — Double click toggles; one click does not. A drag with movement out of the collapsed state expands first; a drag without movement does not.
- **TC.IC.13** — Session selected: transcript visible, Focus view hidden. Select Focus: the reverse, and the area collapsed. Clicking the checked side keeps it checked.

## FR → TC coverage

| FR | Test cases |
|---|---|
| FR.12.29 | TC.IC.01 to 16 |
| FR.23.12 | TC.IC.13 to 15 |
