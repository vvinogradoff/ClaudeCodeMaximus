# TC.AW.01 — Active workers badge

Related requirements: FR.1.28, FR.9.16, TDCP 10.A6. Use cases: [UC.AW.01](/docs/usecases/UC.AW.01-Active-Workers-Badge.md).

**Legend.** AUTOMATED = unit / view-model / STA view test with real layout (WPF), daemon unit tests (Core), or end to end through `tools\e2e\Run-WpfE2E.ps1`.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.AW.01 | The tree row shows the badge left of the `N workers` total, hides it at 0 and follows `session.changed` live | AUTOMATED (STA) | UC.AW.01 | FR.1.28 |
| TC.AW.02 | The badge is a red number in a dark-red outlined rounded square, coloured from the palette | AUTOMATED (STA) | UC.AW.01 | FR.1.28, FR.9.16 |
| TC.AW.03 | The Recent tab row shows the badge, live, hidden at 0 | AUTOMATED (STA) | UC.AW.01 | FR.1.28 |
| TC.AW.04 | The Tags tab session list row shows the badge, live, hidden at 0 | AUTOMATED (STA) | UC.AW.01 | FR.1.28 |
| TC.AW.05 | The row of the open Recent dropdown shows the badge, hidden at 0 | AUTOMATED (STA) | UC.AW.01 | FR.1.28 |
| TC.AW.06 | The tree node and the list row read the count from the state, raise the change, are 0 for a directory; the tree follows `session.changed` without a refresh and the total does not move | AUTOMATED (VM) | UC.AW.01 | FR.1.28 |
| TC.AW.07 | The closed Recent dropdown button shows the open session's badge, live, hidden at 0 | AUTOMATED (STA) | UC.AW.01 | FR.1.28 |
| TC.AW.08 | The palette has the two entries in *Session status* with the Light defaults `#E00000` and `#8B0000` and distinct Dark values | AUTOMATED | UC.AW.01 | FR.9.16 |
| TC.AW.09 | End to end: a worker and a nested worker start and stop in the registry; the badge in the app appears, counts 1, 2, 1 and goes away | AUTOMATED (E2E) | UC.AW.01 | FR.1.28 |
| TC.AW.10 | The registry counts running workers direct and nested, not the session itself, once per worker, follows start and stop, survives a cycle | AUTOMATED (Core) | UC.AW.01 | FR.1.28 |
| TC.AW.11 | The state carries `runningWorkerCount` only while a worker runs (omitted at 0); the golden files show it | AUTOMATED (Core) | UC.AW.01 | FR.1.28 |
| TC.AW.12 | A worker starting or stopping publishes `session.changed` for it and every supervisor above, with the count | AUTOMATED (Core) | UC.AW.01 | FR.1.28 |
| TC.AW.13 | A burst of start/stop events is one `session.changed` per supervisor; a top-level session refreshes only itself | AUTOMATED (Core) | UC.AW.01 | FR.1.28 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.1.28 | TC.AW.01–07, 09–13 |
| FR.9.16 | TC.AW.02, 08 |
