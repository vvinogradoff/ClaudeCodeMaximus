# TC.LE.01 — Limits-error warning

Related requirements: FR.1.29, FR.9.17, TDCP 10.A7. Use cases: [UC.LE.01](/docs/usecases/UC.LE.01-Limits-Error-Warning.md).

**Legend.** AUTOMATED = unit / view-model / STA view test with real layout (WPF), daemon unit tests (Core), or end to end through `tools\e2e\Run-WpfE2E.ps1`.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.LE.01 | The text patterns match the real messages (session, weekly, 5-hour, usage limit reached) on a result error and do not match auth, server or tool errors | AUTOMATED (Core) | UC.LE.01 | FR.1.29 |
| TC.LE.02 | Assistant text is never matched by text; only the structured stamp counts | AUTOMATED (Core) | UC.LE.01 | FR.1.29 |
| TC.LE.03 | The stream parser reads `error` and `quotaLimits.status` of a real assistant line | AUTOMATED (Core) | UC.LE.01 | FR.1.29 |
| TC.LE.04 | A stamped assistant message, a result error and a system error with a limit text set the limit error (not the auth flag); a repeated text changes once | AUTOMATED (Core) | UC.LE.01 | FR.1.29 |
| TC.LE.05 | A later successful turn clears it and announces the change; another error does not clear it; streaming turns behave the same | AUTOMATED (Core) | UC.LE.01 | FR.1.29 |
| TC.LE.06 | The state carries `limitError` only while set (omitted otherwise) | AUTOMATED (Core) | UC.LE.01 | FR.1.29 |
| TC.LE.07 | A change publishes `session.changed` with the text, then without | AUTOMATED (Core) | UC.LE.01 | FR.1.29 |
| TC.LE.08 | A repeated identical limit publishes nothing new | AUTOMATED (Core) | UC.LE.01 | FR.1.29 |
| TC.LE.10 | The tree node and the list row read the text from the state, raise the change; a worker row puts it on the name line; a directory never shows it | AUTOMATED (VM) | UC.LE.01 | FR.1.29 |
| TC.LE.11 | The palette entry exists with a yellow default for Light and for Dark | AUTOMATED | UC.LE.01 | FR.9.17 |
| TC.LE.12 | Tree: a top-level row has the triangle left of the date; a worker row has it all the way right on its name line above the date; tooltip is the text | AUTOMATED (STA) | UC.LE.01 | FR.1.29 |
| TC.LE.13 | Tree: the triangle appears and goes away with `session.changed`, with no refresh | AUTOMATED (STA) | UC.LE.01 | FR.1.29 |
| TC.LE.14 | Recent tab: left of the date, live, hidden without it | AUTOMATED (STA) | UC.LE.01 | FR.1.29 |
| TC.LE.15 | Tags tab list: left of the date, live, hidden without it | AUTOMATED (STA) | UC.LE.01 | FR.1.29 |
| TC.LE.16 | Recent dropdown row: left of the project and profile block, live | AUTOMATED (STA) | UC.LE.01 | FR.1.29 |
| TC.LE.17 | The triangle is a glyph in the palette's yellow | AUTOMATED (STA) | UC.LE.01 | FR.9.17 |
| TC.LE.18 | End to end: a limit error on a worker and on a session makes the triangle appear in the app and clearing it removes it | AUTOMATED (E2E) | UC.LE.01 | FR.1.29 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.1.29 | TC.LE.01–08, 10, 12–16, 18 |
| FR.9.17 | TC.LE.11, 17 |
