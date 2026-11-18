# UC.CM.01 — Copy as Markdown

Related requirements: FR.2.13. Test cases: [TC.CM.01](/docs/testcases/TC.CM.01-Copy-as-Markdown.md).

## UC.CM.01 — Copy part of a response as Markdown
**Actor:** the User. **Precondition:** a session is open with an assistant response containing a heading, a list, a table and a code block.
**Main flow:** 1. The User selects from the heading to the middle of the code block. 2. They right-click the selection: a menu with Copy and Copy as Markdown opens. 3. They choose Copy as Markdown. 4. Pasted into an editor, the text reads as Markdown: `## Heading`, `- item`, the table rows with `|`, and a fenced code block with its language.
**Alternative flows:** **A1** — The User selects two whole entries: the clipboard gets their original Markdown, separated by a blank line. **A2** — Copy: plain text, as Ctrl+C. **A3** — Right-click without a selection: no menu. **A4** — Ctrl+Shift+C with a selection: the same as Copy as Markdown. **A5** — The selection includes a path chip and a link: they copy as their text and `[text](url)`.
**Covers:** FR.2.13.
