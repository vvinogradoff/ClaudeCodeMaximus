# TC.CL.01 — Colours Settings

Related requirements: FR.9.9, FR.9.10, FR.2.8. Use cases: [UC.CL.01](/docs/usecases/UC.CL.01-Colours-Settings.md).

**Legend.** AUTOMATED = unit / view-model / STA view test; E2E = WPF end-to-end (NR.6).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.CL.01 | Catalogue holds every palette key with a Light and a Dark default (Avalonia values) | AUTOMATED | UC.CL.03 | FR.9.9 |
| TC.CL.02 | A row shows a swatch and no hex text; the swatch opens the picker | AUTOMATED | UC.CL.01 | FR.9.9 |
| TC.CL.03 | Picking a colour updates the application resource at once | AUTOMATED | UC.CL.01 | FR.9.9 |
| TC.CL.04 | Reset per row and Reset all (of the theme shown) | AUTOMATED | UC.CL.01 | FR.9.9 |
| TC.CL.05 | Values are stored per theme; a default value is not stored; themes never inherit | AUTOMATED | UC.CL.02 | FR.9.9 |
| TC.CL.06 | Changing theme applies that theme's values (tree and transcript `appearance` message) | AUTOMATED | UC.CL.02 | FR.9.9, FR.2.8 |
| TC.CL.07 | Values survive a restart | E2E | UC.CL.02 | FR.9.9 |
| TC.CL.08 | Focus chips and count pills use the palette keys | AUTOMATED | UC.CL.03 | FR.9.9 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.9.9 | TC.CL.01–08 |
