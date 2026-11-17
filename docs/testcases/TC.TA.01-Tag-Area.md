# TC.TA.01 — Tag Area

Related requirements: FR.24.16. Use cases: [UC.TA.01](/docs/usecases/UC.TA.01-Tag-Area.md). Replaces the proposal `TC.TAGS.16` of report 452.

**Legend.** AUTOMATED = view-model / STA view test (WPF) and vitest (web); E2E = WPF end-to-end (NR.6) and the web E2E project.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.TA.01 | Collapsed by default: one line, no scrollbar, `+N` hint | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.02 | Click on empty space expands to at most 10 lines, then scrolls | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.03 | Double-click on empty space collapses | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.04 | Chip click opens the link and keeps the state; scrollbar double-click does nothing | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.05 | The `+N` hint never covers a chip | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.06 | Session/Focus switch keeps the state | AUTOMATED | UC.TA.02 | FR.24.16 |
| TC.TA.07 | Switching session resets to collapsed; returning stays collapsed | AUTOMATED | UC.TA.02 | FR.24.16 |
| TC.TA.08 | Nothing persists: the client state file has no tag-area value; a restart starts collapsed | E2E | UC.TA.02 | FR.24.16 |
| TC.TA.09 | Expand through UI Automation; no effect without tags | E2E | UC.TA.03 | FR.24.16 |
| TC.TA.10 | Web client: same rules (click, double-click, switch, session change) | E2E (web) | UC.TA.01 | FR.24.16 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.24.16 | TC.TA.01–10 |
