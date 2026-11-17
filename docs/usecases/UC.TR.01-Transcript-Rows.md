# UC.TR.01 — Transcript Rows

Related requirements: FR.2.7–FR.2.11 (see `docs/requirements/`), FR.2.3.x. Test cases: [TC.TR.01](/docs/testcases/TC.TR.01-Transcript-Rows.md).

## Actors
- **User**. **Tessyn** (WPF transcript page).

## Preconditions (common)
- A session with user, assistant, tool-call (system) rows and more than 100 entries of different heights.

---

## UC.TR.01 — Read the conversation without role words
**Main flow:** 1. The User opens the session. 2. User rows show the date above the time at the left, the profile at the right, no word "USER". 3. Assistant rows sit on the plain background, the time on the right, the model line in small italics above. 4. System rows are pale blue with the time on the right.
**Alternative flows:** **A1** — Dark theme: the system rows use `#1A3A50`. **A2** — The User changes the `SystemRow` colour in the Colours tab: rows change at once.
**Covers:** FR.2.7–FR.2.9, FR.9.9.

## UC.TR.02 — Copy any text
**Main flow:** 1. The User drags over text in any row type and copies it. 2. Releasing the mouse does not select an input or open a link.
**Covers:** FR.2.10, FR.2.6.

## UC.TR.03 — Drag the scrollbar through a long session
**Main flow:** 1. The User drags the thumb. 2. The thumb stays under the mouse; the content shows the entry the thumb stands for. 3. At the ends the thumb is exactly at the ends.
**Alternative flows:** **A1** — A page of older entries arrives mid-drag: no jump.
**Covers:** FR.2.11.
