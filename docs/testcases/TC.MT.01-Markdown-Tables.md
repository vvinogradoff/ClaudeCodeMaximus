# TC.MT.01 — Markdown tables

Related requirements: FR.2.14. Use case: [UC.MT.01](/docs/usecases/UC.MT.01-Markdown-Tables.md).

**Legend.** AUTOMATED = unit / script test; E2E = WPF end-to-end (NR.6); MANUAL = by hand.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.MT.01 | Session output CSS: table cells wrap at spaces only (`overflow-wrap:break-word`, `word-break:normal`), no 420 px cell cap, the table is its own horizontal scroller (`display:block; max-width:100%; overflow-x:auto`) | AUTOMATED | UC.MT.01 | FR.2.14 |
| TC.MT.02 | Preview page CSS: the same rules for `.md table` | AUTOMATED | UC.MT.01 | FR.2.14 |
| TC.MT.03 | A 5-column table with one long-text column in a narrow transcript column keeps the short columns at their longest-word width; a wide table scrolls inside its box, the entry does not | MANUAL | UC.MT.01 | FR.2.14 |
| TC.MT.04 | Focus column weights: each weight follows the cell text, never below the floor, a long-text column gets the larger share | AUTOMATED | UC.MT.01 | FR.2.14 |
| TC.MT.05 | A Focus card body with a table builds one star column per column, with the computed weights | AUTOMATED | UC.MT.01 | FR.2.14 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.2.14 | TC.MT.01–05 |