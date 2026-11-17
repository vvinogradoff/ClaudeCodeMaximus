# TC.LK.01 — Inline Paths and Links

Related requirements: FR.26.5, FR.26.6. Use cases: [UC.LK.01](/docs/usecases/UC.LK.01-Inline-Paths-and-Links.md).

**Legend.** AUTOMATED = unit tests, renderer tests and node script tests (`InlinePathMarkerTests`, `PathSlotsScriptTests`, `SessionPaneNavigationTests`); E2E = WPF end-to-end over CDP (NR.6, class `WpfInlinePathsE2ETests`, and `WpfFocusE2ETests` for the preview of a path).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.LK.01 | A bare path, a path in backticks, a path in a code block and a UNC path are marked in place; text around them is unchanged; no chip row | AUTOMATED, E2E | UC.LK.01 | FR.26.5 |
| TC.LK.02 | Paths inside links, in user entries and in entries without paths are not marked; the output adds no element that loads a resource | AUTOMATED | UC.LK.01 | FR.26.5 |
| TC.LK.03 | Ctrl+left-click on a markdown and on a text path opens the preview pane through `file.preview` | E2E | UC.LK.02 | FR.26.5, FR.26.6 |
| TC.LK.04 | A plain click on a path opens nothing | E2E | UC.LK.02 | FR.26.6 |
| TC.LK.05 | Only Ctrl + left button opens: Shift or Alt with it, the middle and right buttons and non-click events do not | AUTOMATED | UC.LK.02 | FR.26.6 |
| TC.LK.06 | A drag over a path selects its text and opens nothing | E2E | UC.LK.03 | FR.26.6 |
| TC.LK.07 | The pointer cursor on a path, a link and an image only while Ctrl is held; the path has a dotted underline | E2E | UC.LK.02 | FR.26.5, FR.26.6 |
| TC.LK.08 | An image path shows the image directly under it, fitted to the column (max 360 px high), from the preview cache only | AUTOMATED, E2E | UC.LK.04 | FR.26.5 |
| TC.LK.09 | The first occurrence of an image path gets the slot; svg and non-image files get none | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.10 | An image source outside the preview-cache host never becomes an `<img>` | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.11 | Dragging from the path down past the image selects the image; a plain click on it opens nothing; Ctrl+click on it opens the preview | E2E | UC.LK.03, UC.LK.04 | FR.26.5, FR.26.6 |
| TC.LK.12 | A file the access rules refuse shows "Image not allowed", a missing one "Image unavailable"; neither reads the file | AUTOMATED, E2E | UC.LK.04 | FR.26.5 |
| TC.LK.13 | The inline image asks the daemon like a click (`file.preview`), is copied to the preview cache once per entry and path, and does not open the preview pane | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.14 | A path that is not path-shaped never reaches the daemon | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.15 | After the image loaded the entry height is the measured one (re-measure) | E2E | UC.LK.04 | FR.26.5 |
| TC.LK.16 | A web link: plain click posts nothing, Ctrl+click posts it to the host; the host keeps the http/https policy | E2E | UC.LK.05 | FR.26.3, FR.26.6 |
| TC.LK.17 | The page's CSP names only the app's own virtual hosts (the media proxy and the preview cache for images) | AUTOMATED | UC.LK.04 | FR.26.5 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.26.5 | TC.LK.01–02, 03, 07–15, 17 |
| FR.26.6 | TC.LK.03–07, 11, 16 |
