# UC.CL.04 — Colours tab layout and Apply

Related requirements: FR.9.18 (amends FR.9.9). Test cases: [TC.CL.20](/docs/testcases/TC.CL.20-Colours-Layout-and-Apply.md).

## UC.CL.04 — Edit colours in a compact two-column list and apply them
**Actor:** the Architect. **Precondition:** Settings › Colours is open on a wide window.
**Main flow:** 1. The theme row shows the Light/Dark switch, Reset all <Theme> colours and, at the far right, Apply. 2. The first group heading follows the theme row closely. 3. The colours of each group are in two columns; no swatch touches the scrollbar. 4. The Architect opens a picker, changes a colour and clicks Apply: the picker closes and the colour shows in the tree, the session output and every other window at once.
**Alternative flows:** **A1** — The window is narrowed below about 640 px: the list becomes one column. **A2** — The Architect edits the theme that is not active and clicks Apply: the colours are stored and show when that theme is active. **A3** — A long label: it trims with an ellipsis and its tooltip shows it in full.
**Covers:** FR.9.18.
