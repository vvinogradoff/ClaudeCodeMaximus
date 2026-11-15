# TC.IC.01 — Collapsible Input Area

Related requirements: FR.12.29 in [requirements.md](/docs/requirements.md). Related use cases: [UC.IC.01](/docs/usecases/UC.IC.01-Input-Collapse.md).

**Legend.** AUTOMATED = view-model test. E2E = end-to-end test in the chosen client (WPF E2E project, or manual in Avalonia, which has no UI tests). The client is not chosen yet.

## Summary

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.IC.01 | Default: Session view expanded | AUTOMATED | UC.IC.02 | FR.12.29 |
| TC.IC.02 | Default: Focus view collapsed | AUTOMATED | UC.IC.02 | FR.12.29 |
| TC.IC.03 | Toggle collapses and expands; height restored | AUTOMATED | UC.IC.01 | FR.12.29 |
| TC.IC.04 | Switching session resets to the landing view's default | AUTOMATED | UC.IC.03 | FR.12.29 |
| TC.IC.05 | Switching view in one session applies the default of the view entered | AUTOMATED | UC.IC.02 | FR.12.29 |
| TC.IC.06 | State is not persisted | AUTOMATED | UC.IC.03 | FR.12.29 |
| TC.IC.07 | Single side button: Send, or Stop while a turn runs | AUTOMATED | UC.IC.01 | FR.12.29 |
| TC.IC.08 | Collapsed with command bar shown: bar only | E2E | UC.IC.01 | FR.12.29, FR.12.11 |
| TC.IC.09 | Collapsed with command bar hidden: one editable text line | E2E | UC.IC.01 | FR.12.29 |
| TC.IC.10 | Double click on the splitter toggles; drag from collapsed expands | E2E | UC.IC.01 | FR.12.29 |
| TC.IC.11 | Collapsing keeps draft text, command bar visibility and model choice | AUTOMATED | UC.IC.01 | FR.12.29 |

## Cases

- **TC.IC.03** — Expanded at height H; toggle: collapsed; toggle: expanded at H.
- **TC.IC.04** — In the Focus view of A expand, select B (Focus): collapsed; return to A: collapsed. Repeat for the Session view with collapse: expanded on return.
- **TC.IC.07** — Idle: Send only. Turn running: Stop only, in both states. Never two buttons.
- **TC.IC.10** — Double click toggles. A drag from collapsed expands first, then resizes.

## FR → TC coverage

| FR | Test cases |
|---|---|
| FR.12.29 | TC.IC.01 to 11 |
