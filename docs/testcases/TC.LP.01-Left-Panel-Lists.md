# TC.LP.01 — Left Panel and Session Lists

Related requirements: FR.1.18–FR.1.27, FR.9.10, FR.9.11, FR.24.17. Use cases: [UC.LP.01](/docs/usecases/UC.LP.01-Left-Panel-Lists.md).

**Legend.** AUTOMATED = unit or view-model test; E2E = WPF end-to-end (`tools\e2e\Run-WpfE2E.ps1`, NR.6); CORE = daemon test.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.LP.01 | Appearance offers exactly two date options | AUTOMATED | UC.LP.01 | FR.1.18 |
| TC.LP.02 | Absolute form is `yyyy-MM-dd`, no time | AUTOMATED | UC.LP.01 | FR.1.18 |
| TC.LP.03 | Relative form: `now`, `5m`, `3h`, `9d`, `107w`, no months or years; refresh moves it | AUTOMATED | UC.LP.01 | FR.1.18 |
| TC.LP.04 | Date or age, never both; both inside the recency box | AUTOMATED | UC.LP.01 | FR.1.18, FR.1.21 |
| TC.LP.05 | Worker count reads `N workers` inside the box; no `Nw` outside | AUTOMATED | UC.LP.02 | FR.1.21 |
| TC.LP.06 | Recency tiers 15 m / 1 h / 4 h / 12 h, draft keeps palest tier, 24 h dotted border | AUTOMATED | UC.LP.02 | FR.9.10 |
| TC.LP.07 | Recent tab row is full width, no project name, icon overlay reserves space | AUTOMATED | UC.LP.03 | FR.1.20 |
| TC.LP.08 | Dropdown row: one line, project right after the name, full width | AUTOMATED | UC.LP.03 | FR.1.20 |
| TC.LP.09 | Dropdown button has no accent fill when open; rows keep recency colour | AUTOMATED | UC.LP.03 | FR.9.11 |
| TC.LP.10 | Tab header cells are equal and sum to the panel width | AUTOMATED | UC.LP.04 | FR.1.19 |
| TC.LP.11 | No gap under the tree search; empty status line takes no space; Tags chips take the room | AUTOMATED | UC.LP.04 | FR.1.23 |
| TC.LP.12 | Tag scope: session scopes, directory/group/none do not; Show all drops it | AUTOMATED | UC.LP.05 | FR.1.24 |
| TC.LP.13 | `HSE-*` and `HSE*240` become word separators | AUTOMATED | UC.LP.05 | FR.1.24 |
| TC.LP.14 | Empty result text names the scope and the way out | AUTOMATED | UC.LP.05 | FR.1.24 |
| TC.LP.15 | Tags tab shows the chips of a real answer (292 chips) | AUTOMATED | UC.LP.05 | FR.24.17 |
| TC.LP.16 | Title rename: double-click, Enter, Esc, empty, refusal; Delete icon | AUTOMATED | UC.LP.06 | FR.1.25 |
| TC.LP.17 | Tree toolbar is one line at the bottom; header has Switch profile after Send later | E2E | UC.LP.06 | FR.1.25, FR.1.27 |
| TC.LP.18 | Choice dialog buttons span the dialog width | AUTOMATED | UC.LP.06 | FR.1.27 |
| TC.LP.19 | Resumable: nothing before the first sweep, baseline raises nothing, flips raise `session.changed`, one batch | CORE | UC.LP.07 | FR.1.26 |
| TC.LP.20 | Husk flag: workers only, mixed, user-created empty, nested | CORE | UC.LP.07 | FR.1.26 |
| TC.LP.21 | Profile on the row: pin, `-> next`, fallback to the stamped profile | AUTOMATED | UC.LP.02 | FR.11.14 |

## Cases
- **TC.LP.03** — Now minus 30 s gives `now`; 5 min `5m`; 3 h `3h`; 9 d `9d`; 13 d `13d`; 14 d `2w`; 749 d `107w`.
- **TC.LP.06** — Activity 14 min ago: tier 1; 16 min: tier 2; 61 min: tier 3; 5 h: tier 4; 13 h with a draft: palest tier; 13 h without: none; 23 h: dotted border; 25 h: no border.
- **TC.LP.12** — With a Tessyn session selected, `HSE` finds 0 (scope on); select a directory node: 87.

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.1.18 | TC.LP.01–04 |
| FR.1.19 | TC.LP.10 |
| FR.1.20 | TC.LP.07, 08 |
| FR.1.21 | TC.LP.04, 05 |
| FR.1.22 | TC.LP.19 |
| FR.1.23 | TC.LP.11 |
| FR.1.24 | TC.LP.12–14 |
| FR.1.25 | TC.LP.16, 17 |
| FR.1.26 | TC.LP.19, 20 |
| FR.1.27 | TC.LP.17, 18 |
| FR.9.10 | TC.LP.06 |
| FR.9.11 | TC.LP.09 |
| FR.11.14 | TC.LP.21 |
| FR.24.17 | TC.LP.15 |
