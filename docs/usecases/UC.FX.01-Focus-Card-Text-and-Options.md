# UC.FX.01 — Focus card text and options

Related requirements: FR.23.25, FR.23.26. Test cases: [TC.FX.01](/docs/testcases/TC.FX.01-Focus-Card-Text-and-Options.md).

## UC.FX.01 — Select a whole Focus item's text
**Actor:** the User. **Precondition:** the Focus view shows an Ask item whose body has three paragraphs, a list and a code block.
**Main flow:** 1. The User drags from the title down to the last paragraph: the selection runs across all of it. 2. Ctrl+C. 3. Pasted, the text keeps its paragraph breaks.
**Alternative flows:** **A1** — The drag goes on into the next card: the selection stops at the end of the first card. **A2** — Ctrl+A inside the card text: the card's whole text is selected, nothing else. **A3** — A closed card: the same. **A4** — A click on a link in the body: it opens.
**Covers:** FR.23.25.

## UC.FX.02 — Pick an option or the recommendation
**Actor:** the User. **Precondition:** an Ask item has three options, one long, and a recommendation.
**Main flow:** 1. The options are a vertical list of full-width buttons with small gaps; the long one wraps only because it is wider than the card. 2. The last button shows the recommendation text with an accent outline, without a prefix. 3. The User clicks it: the reply box holds `Accepted your recommendation: <text>`.
**Alternative flows:** **A1** — The recommendation equals one of the options: that option appears once, as the last, outlined button. **A2** — No recommendation: only the options. **A3** — The User clicks a plain option: its text goes in the reply box.
**Covers:** FR.23.26.
