> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.10 — Output Search

**FR.10.1** The session view header shall include a search text box positioned to the left of the Markdown toggle button. The search box searches within the currently displayed output messages.

**FR.10.2 — Search navigation:** Pressing `Enter` in the search box starts a search (or advances to the next match). Pressing `Ctrl+Enter` navigates to the previous match. Pressing `Escape` dismisses the search (hides the results overlay) but preserves the search text in the box.

**FR.10.3 — Results overlay:** A small floating panel is displayed in the top-right corner of the output area when a search is active. The panel has a yellow semi-transparent fill and yellow stroke. It shows:
- Match status text: "N of M matches" (e.g., "1 of 5 matches") or "no matches"
- Previous (`<`) and Next (`>`) navigation buttons
- A Close (`X`) button that dismisses the search (same as Escape)

**FR.10.4 — Match highlighting:** Matched messages are scrolled into view when navigating. The search is case-insensitive and matches against message content text. While a search is active, all occurrences of the search term within message content are highlighted with a yellow background (semi-transparent `#B4FFFF00`). The currently selected match message uses an orange background (`#DCFFA500`) to distinguish it from other matches. Highlighting applies to:
- User message text (plain text)
- Assistant message text (both plain text and markdown rendering modes, including within code blocks and inline code)
- System message text

**FR.10.5 — Precise scroll positioning:** When navigating to a match, the output area scrolls to position the matched text at approximately 25% from the top of the viewport, rather than merely scrolling the message bubble into view. This ensures the matched text is visible with context above and more content below.

**FR.10.6 — Re-search on text change:** When the user modifies the search text while a search is active and presses Enter, a new search is performed with the updated text instead of navigating within the old search results.

Highlighting splits the text at match boundaries and renders matches with the yellow background, in plain-text and markdown modes alike. Highlighting is cleared when the search is dismissed.

**FR.10.5 — Dismissal behavior:** Closing the search overlay (via `X` button or `Escape` key) hides the overlay and clears any match highlighting, but does **not** clear the search text from the search box.

---
