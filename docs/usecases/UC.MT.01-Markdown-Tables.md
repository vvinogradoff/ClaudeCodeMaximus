# UC.MT.01 — Read a Markdown table

Related requirements: FR.2.14. Test cases: [TC.MT.01](/docs/testcases/TC.MT.01-Markdown-Tables.md).

## Actors
- **User**. **Tessyn** (WPF client).

## Preconditions
- A session whose reply contains a table with five columns: three short (`#`, `Status`, `Owner`) and two long-text columns.

## UC.MT.01 — Read a table in the session output, a preview and a Focus card
**Main flow:** 1. The User opens the session. Every column of the table is at least as wide as its longest word; the long-text columns wrap at spaces. 2. The User narrows the window: the table does not squeeze columns to one or two characters; it grows a horizontal scroll bar **inside its own box** while the entry and the page stay still. 3. The User opens a `.md` file with the same table in the preview pane or a preview window: same result. 4. The User opens the Focus view with an item whose body has the table: the columns share the card width in proportion to their text.
**Alternative flows:** **A1** — A cell holds one very long URL: it may break inside the cell. **A2** — Selecting text across the table, Find and Copy as Markdown work as before.
**Covers:** FR.2.14, FR.2.13.