# TC.TH.01 — Theme at the first paint

Related requirements: FR.9.14. Use cases: [UC.TH.01](/docs/usecases/UC.TH.01-Theme-First-Paint.md).

**Legend.** AUTOMATED = unit / STA test; E2E = WPF end-to-end (NR.6); MANUAL = checked by the Architect on the real app.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.TH.01 | The window is styled for the stored theme (Light, Dark, System) whatever theme it was created under | AUTOMATED | UC.TH.01 | FR.9.14 |
| TC.TH.02 | Start the real app, maximised, on a system whose theme differs from the stored one (Light stored, dark system, and the reverse): the first paint is in the stored theme, with no switching back and forth | MANUAL | UC.TH.01 | FR.9.14 |
| TC.TH.03 | The theme button still flips the stored theme both ways in the built app | E2E | UC.TH.01 | FR.9.14 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.9.14 | TC.TH.01–03 |
