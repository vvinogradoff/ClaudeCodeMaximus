# TC.LS.01 — Line spacing

Related requirements: FR.9.12. Use cases: [UC.LS.01](/docs/usecases/UC.LS.01-Line-Spacing.md).

**Legend.** AUTOMATED = unit / view-model / STA view test (`LineSpacingTests`); E2E = WPF end-to-end over CDP (NR.6).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.LS.01 | Default 1.25, tighter than 1.45, inside 1.0–1.8 | AUTOMATED | UC.LS.01 | FR.9.12 |
| TC.LS.02 | The Appearance tab stores the value and shows the stored one | AUTOMATED | UC.LS.01 | FR.9.12 |
| TC.LS.03 | A value outside 1.0–1.8 or a non-number is refused, nothing stored | AUTOMATED | UC.LS.01 | FR.9.12 |
| TC.LS.04 | A change raises the appearance change and reaches the page message as `lh` | AUTOMATED | UC.LS.01 | FR.9.12 |
| TC.LS.05 | The page has one `--lh` variable for every row; the host sets it (CSS test) | AUTOMATED | UC.LS.01 | FR.9.12 |
| TC.LS.06 | Focus card title, body and outcome follow the value and change live (STA) | AUTOMATED | UC.LS.01 | FR.9.12 |
| TC.LS.07 | The page applies the value to assistant, user and root text and re-measures the list on a change | E2E | UC.LS.01 | FR.9.12 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.9.12 | TC.LS.01–07 |
