# TC.TA.01 — Tag Area

Related requirements: FR.24.16, FR.24.18. Use cases: [UC.TA.01](/docs/usecases/UC.TA.01-Tag-Area.md). Replaces the proposal `TC.TAGS.16` of report 452.

**Legend.** AUTOMATED = view-model / STA view test (WPF); E2E = WPF end-to-end (NR.6). Tessyn Web is not covered by FR.24.16.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.TA.01 | Collapsed by default: one line, no scrollbar, `+N` hint | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.02 | Click on empty space expands to at most 10 lines, then scrolls | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.03 | Double-click on empty space collapses; a double-click on a chip or on the scrollbar does not | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.04 | Chip click opens the link and keeps the state; scrollbar double-click does nothing | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.05 | The `+N` hint never covers a chip | AUTOMATED | UC.TA.01 | FR.24.16 |
| TC.TA.06 | Session/Focus switch keeps the state | AUTOMATED | UC.TA.02 | FR.24.16 |
| TC.TA.07 | Every session switch resets to collapsed, including returning to the session just left; closing the session collapses | AUTOMATED | UC.TA.02 | FR.24.16 |
| TC.TA.08 | Nothing persists: the client state file gets no tag-area value (an old `tagAreaExpanded` is read and dropped, never written back); a restart starts collapsed | E2E | UC.TA.02 | FR.24.16 |
| TC.TA.09 | Expand through UI Automation; no effect without tags | E2E | UC.TA.03 | FR.24.16 |
| TC.TA.10 | **WITHDRAWN 2026-11-17.** Manual check of the collapse behaviour in the previous desktop client; covered for WPF by TC.TA.01–09 | WITHDRAWN | UC.TA.01 | FR.24.16 |
| TC.TA.11 | WPF: Settings › Appearance › Tag cloud; changing it restyles chips, the `+N` hint and the Tags tab chips at once | AUTOMATED | UC.TA.04 | FR.24.18 |
| TC.TA.12 | The mini line is one chip line high at the smallest, default and largest size (no clipped chip, no empty band); expanded keeps the 10-line limit | AUTOMATED | UC.TA.04 | FR.24.18 |
| TC.TA.13 | The size survives a restart | E2E | UC.TA.04 | FR.24.18 |
| TC.TA.14 | **WITHDRAWN 2026-11-17.** Manual check of the tag cloud font size setting in the previous desktop client; covered for WPF by TC.TA.11–13 | WITHDRAWN | UC.TA.04 | FR.24.18 |
| TC.TA.15 | The range is 8 to 40: 8 and 40 accepted; 7, 41, empty and non-numeric text refused, nothing stored | AUTOMATED | UC.TA.04 | FR.24.18 |
| TC.TA.16 | The default is 14 (the second default, 11, is WITHDRAWN 2026-11-17) | AUTOMATED | UC.TA.04 | FR.24.18 |
| TC.TA.17 | The one-time settings import leaves the WPF tag cloud size unchanged | AUTOMATED | UC.TA.04 | FR.24.18, FR.32.4 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.24.16 | TC.TA.01–10 |
| FR.24.18 | TC.TA.11–17 |
