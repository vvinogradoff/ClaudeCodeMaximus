# UC.CL.04 — Colours tab layout and Apply

Related requirements: FR.9.18 (amends FR.9.9), FR.9.19. Test cases: [TC.CL.20](/docs/testcases/TC.CL.20-Colours-Layout-and-Apply.md).

## UC.CL.04 — Edit colours in a compact two-column list and apply them
**Actor:** the Architect. **Precondition:** Settings › Colours is open on a wide window.
**Main flow:** 1. The theme row shows the Light/Dark switch, Reset all <Theme> colours and, at the far right, Apply. 2. The first group heading follows the theme row closely. 3. The colours of each group are in two columns; no swatch touches the scrollbar. 4. The Architect opens a picker, changes a colour and clicks Apply: the picker closes and the colour shows in the tree, the session output and every other window at once.
**Alternative flows:** **A1** — The window is narrowed below about 640 px: the list becomes one column. **A2** — The Architect edits the theme that is not active and clicks Apply: the colours are stored and show when that theme is active. **A3** — A long label: it trims with an ellipsis and its tooltip shows it in full.
**Covers:** FR.9.18.

## UC.CL.05 — Pick a colour calmly, by name or by harmony
**Actor:** the Architect. **Precondition:** Settings › Colours is open.
**Main flow:** 1. The Architect opens the picker of a row and drags across the colour area: only the preview changes; the application does not flicker. 2. They type `gold` in the Named colours filter and click `palegoldenrod`: it becomes the new colour and its name shows by the preview. 3. They look at the Complementary, Analogous and Triadic swatches and click one of the analogous ones. 4. They click outside: the row shows the pending colour with its dot. 5. They click Apply: the colour shows everywhere, and the dot goes away.
**Alternative flows:** **A1** — Esc in the picker: the row keeps its previous colour. **A2** — The Architect presses Save instead of Apply: the same as Apply. **A3** — They close Settings with a pending colour: they are asked whether to apply it. **A4** — Reset on a row: the default is pending until Apply.
**Covers:** FR.9.19.
