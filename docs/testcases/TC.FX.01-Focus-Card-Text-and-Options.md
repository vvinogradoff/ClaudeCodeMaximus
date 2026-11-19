# TC.FX.01 — Focus card text and options

Related requirements: FR.23.25, FR.23.26, FR.23.28, FR.23.30. Use cases: [UC.FX.01](/docs/usecases/UC.FX.01-Focus-Card-Text-and-Options.md).

**Legend.** AUTOMATED = view-model tests (VM), STA view tests with real layout (WPF), or end to end through `tools\e2e\Run-WpfE2E.ps1`.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.FX.01 | A card's id, title, body (paragraphs, list, quote, table, code) and outcome are one selectable text: a selection from the title to the last paragraph covers all of it | AUTOMATED (STA) | UC.FX.01 | FR.23.25 |
| TC.FX.02 | Ctrl+A selects the card's text only; Ctrl+C copies it with paragraph breaks | AUTOMATED (STA) | UC.FX.01 | FR.23.25 |
| TC.FX.03 | A selection cannot extend into another card; options and reply box are not part of it | AUTOMATED (STA) | UC.FX.01 | FR.23.25 |
| TC.FX.04 | A press that ends a selection triggers no card action; a link click opens; open, Decided and closed cards behave the same | AUTOMATED (STA) | UC.FX.01 | FR.23.25 |
| TC.FX.10 | Options are laid out vertically, each button as wide as the card, gaps of 2 px; a short option does not wrap, a long one wraps and grows in height | AUTOMATED (STA) | UC.FX.02 | FR.23.26 |
| TC.FX.11 | The recommendation is the last button, text without prefix, accent outline of 1.5 px, tooltip `Recommended` | AUTOMATED (VM, STA) | UC.FX.02 | FR.23.26 |
| TC.FX.12 | A recommendation equal to an option (trimmed, case-insensitive) shows once, last | AUTOMATED (VM) | UC.FX.02 | FR.23.26 |
| TC.FX.13 | Clicking an option and the recommended button fill the reply box as before | AUTOMATED (VM) | UC.FX.02 | FR.23.26 |
| TC.FX.14 | End to end: an item with options and a recommendation shows the vertical list with the outlined last button, and its text is selectable as one | AUTOMATED (E2E) | UC.FX.01, UC.FX.02 | FR.23.25, FR.23.26 |
| TC.FX.20 | The recommended button uses the accent button style (accent background, on-accent text) and has no extra outline; other options keep the default style | AUTOMATED (STA) | UC.FX.02 | FR.23.28 |
| TC.FX.21 | The reply box sits in a 2 px accent border | AUTOMATED (STA) | UC.FX.02 | FR.23.28 |
| TC.FX.22 | Dragging the resize grip changes the reply box height, clamped to 56–600 px | AUTOMATED (STA) | UC.FX.02 | FR.23.28 |
| TC.FX.23 | Absolute and relative paths in a card's title and body are marked (dotted underline); words that are not paths are not | AUTOMATED (STA) | UC.FX.03 | FR.23.30 |
| TC.FX.24 | A path in the outcome line or a reply of a closed card is not marked; option buttons carry no path marking | AUTOMATED (STA) | UC.FX.03 | FR.23.30 |
| TC.FX.25 | Ctrl+left-click on a path raises the path request with the item id and the path; a plain click and a drag raise none and select as before | AUTOMATED (STA) | UC.FX.03 | FR.23.30, FR.23.25 |
| TC.FX.26 | UI Automation Invoke on a path raises the request | AUTOMATED (STA) | UC.FX.03 | FR.23.30 |
| TC.FX.27 | The request reaches the client as `file.preview` with `itemId` (not `entryId`); an open item opens the file with the item as source, through the router when preview windows are on, else the pane | AUTOMATED (VM) | UC.FX.03 | FR.23.30 |
| TC.FX.28 | A refusal by the daemon shows the dismissible notice and opens nothing | AUTOMATED (VM) | UC.FX.03 | FR.23.30 |
| TC.FX.29 | An expanded closed card's path opens the file with no source item | AUTOMATED (VM) | UC.FX.03 | FR.23.30, FR.23.29 |
| TC.FX.30 | Core: `file.preview` with `itemId` returns a handle for a path in the item's title or body (absolute, and relative to the working directory) | AUTOMATED (Core) | UC.FX.03 | FR.23.30 |
| TC.FX.31 | Core: a path that is not in the item, one only in the user's answer, a `..` path leaving the working directory, an unknown item, and both or neither of `itemId`/`entryId` are refused | AUTOMATED (Core) | UC.FX.03 | FR.23.30 |
| TC.FX.32 | End to end: Invoke on a path in a Focus card opens the file preview pane | AUTOMATED (E2E) | UC.FX.03 | FR.23.30 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.23.25 | TC.FX.01–04, 14 |
| FR.23.26 | TC.FX.10–14 |
| FR.23.28 | TC.FX.20–22 |
| FR.23.30 | TC.FX.23–32 |
