# TC.IN.01 — Input Navigation

Related requirements: FR.2.6. Use cases: [UC.IN.01](/docs/usecases/UC.IN.01-Input-Navigation.md).

**Legend.** AUTOMATED = Core or node script tests; E2E = WPF end-to-end over CDP (NR.6, class `WpfInputNavigation`).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.IN.01 | Colour and label per source (scheduled, orchestrated, user) | E2E | UC.IN.01 | FR.2.6 |
| TC.IN.02 | Click selects (dotted border); link click, text drag, margin click do not; click elsewhere and Esc clear | E2E | UC.IN.02 | FR.2.6 |
| TC.IN.03 | My-only navigation skips automated inputs | E2E | UC.IN.02 | FR.2.6 |
| TC.IN.04 | All-kinds navigation from an automated input | E2E | UC.IN.03 | FR.2.6 |
| TC.IN.05 | Navigation reaches an input outside the loaded page; an empty page drops the move (no request loop) | E2E | UC.IN.02 | FR.2.6 |
| TC.IN.06 | No selection: Up/Down line scroll, PgUp/PgDn page scroll | E2E | UC.IN.04 | FR.2.6 |
| TC.IN.07 | Selection survives an append, a live row, a replaced entry and a reload of the same session; another session clears it | E2E | UC.IN.05 | FR.2.6 |
| TC.IN.08 | Ctrl+F, F3, find steps and reference jumps unaffected by a selection | E2E | UC.IN.05 | FR.2.6 |
| TC.IN.09 | Persistence: header round trip, legacy `[Scheduled]` resolved on read only, older-parser compatibility | AUTOMATED | UC.IN.01 | FR.2.6, tdcp §A1 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.2.6 | TC.IN.01–09 |
