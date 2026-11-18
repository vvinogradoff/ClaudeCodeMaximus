# TC.CM.01 — Copy as Markdown

Related requirements: FR.2.13. Use cases: [UC.CM.01](/docs/usecases/UC.CM.01-Copy-as-Markdown.md).

**Legend.** AUTOMATED = unit tests of the converter (JS run in the test page or C#), STA / WebView2 view tests (WPF), or end to end through `tools\e2e\Run-WpfE2E.ps1`.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.CM.01 | The fragment converter gives Markdown for headings, bold/italic, inline code, fenced code with language, nested bulleted and numbered lists, quotes, links, tables, both rules and line breaks | AUTOMATED | UC.CM.01 | FR.2.13 |
| TC.CM.02 | A selection covering whole entries copies their original Markdown source, entries separated by a blank line | AUTOMATED | UC.CM.01 | FR.2.13 |
| TC.CM.03 | Path chips copy as their text, link chips as `[text](url)`, images and video previews as their Markdown link | AUTOMATED | UC.CM.01 | FR.2.13 |
| TC.CM.04 | Right-click with a selection opens the menu with Copy and Copy as Markdown; without a selection, no menu; on a link, the link's own behaviour | AUTOMATED (E2E) | UC.CM.01 | FR.2.13 |
| TC.CM.05 | Copy puts plain text on the clipboard; Copy as Markdown and Ctrl+Shift+C put the Markdown | AUTOMATED (E2E) | UC.CM.01 | FR.2.13 |
| TC.CM.06 | Works in user, assistant and system rows, in markdown and plain modes | AUTOMATED | UC.CM.01 | FR.2.13 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.2.13 | TC.CM.01–06 |
