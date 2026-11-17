# TC.SH.01 — Window Chrome and Composer Layout

Related requirements: FR.6.5–FR.6.8, FR.12.30–FR.12.32, FR.14.13, NR.7. Use cases: [UC.SH.01](/docs/usecases/UC.SH.01-Window-Chrome.md).

**Legend.** AUTOMATED = unit / STA view test; E2E = WPF end-to-end (NR.6).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.SH.01 | The window, taskbar and tray icons are the web icon artwork (embedded resource check) | AUTOMATED | UC.SH.01 | FR.6.5 |
| TC.SH.02 | Version label is not focusable, not a tab stop; a press starts a drag | AUTOMATED | UC.SH.02 | FR.6.6 |
| TC.SH.03 | Title bar slots: instruction toolbar, Recent dropdown in the middle with a 48 px drag gap | AUTOMATED | UC.SH.03 | FR.6.7 |
| TC.SH.04 | Hamburger entries and order (Import, Settings, separator, Exit); opens by UIA invoke | E2E | UC.SH.03 | FR.6.7 |
| TC.SH.05 | Dropdown button text follows the open session; popup as wide as the button | AUTOMATED | UC.SH.03 | FR.6.7 |
| TC.SH.06 | Meters take the bar width and height; Profiles at the far right | AUTOMATED | UC.SH.03 | FR.6.8 |
| TC.SH.07 | Toggles use the regular button brush; only Send is accent | AUTOMATED | UC.SH.04 | FR.12.30 |
| TC.SH.08 | A margin separates Profile and Streaming | AUTOMATED | UC.SH.04 | FR.12.30 |
| TC.SH.09 | Long text: the box takes the free height, Send stays visible; side buttons icon-only | AUTOMATED | UC.SH.04 | FR.12.31, NR.7 |
| TC.SH.10 | Send later keeps only the picked model/effort/profile; one-shot toggles refuse | AUTOMATED | UC.SH.04 | FR.12.32, FR.14.13 |
| TC.SH.11 | Time answers: `3650d` accepted, `3650d1m` and huge numbers refused; DST gap and overlap | AUTOMATED | UC.SH.04 | FR.12.32 |
| TC.SH.12 | Schedule picks: round trip, validation at add (`-32602`), fire with picks only, legacy none, unresolvable pick still runs | AUTOMATED (Core) | UC.SH.04 | FR.14.13 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.6.5 | TC.SH.01 |
| FR.6.6 | TC.SH.02 |
| FR.6.7 | TC.SH.03–05 |
| FR.6.8 | TC.SH.06 |
| FR.12.30 | TC.SH.07, 08 |
| FR.12.31 | TC.SH.09 |
| FR.12.32 | TC.SH.10, 11 |
| FR.14.13 | TC.SH.10, 12 |
