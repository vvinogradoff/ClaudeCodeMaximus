# TC.TR.01 — Transcript Rows

Related requirements: FR.2.7–FR.2.11. Use cases: [UC.TR.01](/docs/usecases/UC.TR.01-Transcript-Rows.md).

**Legend.** AUTOMATED = unit (node script tests, view-model); E2E = WPF end-to-end over CDP (`CdpPage`, NR.6).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.TR.01 | Assistant row: no border, no background, no role word, time right, model line above | E2E | UC.TR.01 | FR.2.7 |
| TC.TR.02 | System row: assistant layout, `SystemRow` background (light `#BFE0F7`, dark `#1A3A50`), time right | E2E | UC.TR.01 | FR.2.8 |
| TC.TR.03 | `appearance` message carries `sys`; override and theme default | AUTOMATED | UC.TR.01 | FR.2.8, FR.9.9 |
| TC.TR.04 | User row: no role word; date over time at the left; profile at the right | E2E | UC.TR.01 | FR.2.9 |
| TC.TR.05 | Text selectable in every row type; a selection drag selects no input and opens no link | E2E | UC.TR.02 | FR.2.10 |
| TC.TR.06 | Scroll map: mapping, round trip, monotonic, thumb at the ends, remeasure does not move the target | AUTOMATED | UC.TR.03 | FR.2.11 |
| TC.TR.07 | Thumb follows the mouse within 1.5 px at every step on 100 entries of different heights | E2E | UC.TR.03 | FR.2.11 |
| TC.TR.08 | Drag to the extremes lands exactly at the bottom and the top; content stable after release | E2E | UC.TR.03 | FR.2.11 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.2.7 | TC.TR.01 |
| FR.2.8 | TC.TR.02, 03 |
| FR.2.9 | TC.TR.04 |
| FR.2.10 | TC.TR.05 |
| FR.2.11 | TC.TR.06–08 |
