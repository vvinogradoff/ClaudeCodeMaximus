# TC.CL.20 — Colours tab layout and Apply

Related requirements: FR.9.18, FR.9.19. Use cases: [UC.CL.04](/docs/usecases/UC.CL.04-Colours-Layout-and-Apply.md).

**Legend.** AUTOMATED = view-model tests (VM), STA view tests with real layout (WPF), or end to end through `tools\e2e\Run-WpfE2E.ps1`.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.CL.20 | Apply is in the theme row, its right edge at the tab's right content edge, right of Reset all | AUTOMATED (STA) | UC.CL.04 | FR.9.18 |
| TC.CL.21 | The gap between the theme row and the first group heading is at most 8 px with no messages shown | AUTOMATED (STA) | UC.CL.04 | FR.9.18 |
| TC.CL.22 | At 900 px width the rows of a group are in two equal columns (row order left then right); at 600 px in one column | AUTOMATED (STA) | UC.CL.04 | FR.9.18 |
| TC.CL.23 | The rightmost swatch / Reset ends at least 8 px before the vertical scrollbar | AUTOMATED (STA) | UC.CL.04 | FR.9.18 |
| TC.CL.24 | Apply commits the open picker's colour, closes the picker and re-applies the palette (application brushes and the transcript's appearance message) | AUTOMATED (VM) | UC.CL.04 | FR.9.18 |
| TC.CL.25 | Apply on the theme that is not active stores the colours and does not change the active theme's brushes | AUTOMATED (VM) | UC.CL.04 | FR.9.18 |
| TC.CL.26 | End to end: the Colours tab shows Apply and two columns; Apply after a change leaves the stored colour in the client state | AUTOMATED (E2E) | UC.CL.04 | FR.9.18 |
| TC.CL.30 | Moving in the colour area or the opacity slider changes only the preview: no store write and no brush change until Apply | AUTOMATED (VM) | UC.CL.05 | FR.9.19 |
| TC.CL.31 | Closing the picker leaves a pending colour on the row (swatch and dot); Esc drops the edit | AUTOMATED (VM, STA) | UC.CL.05 | FR.9.19 |
| TC.CL.32 | Apply stores all pending colours of the edited theme and re-applies brushes and the transcript appearance; Save does the same; Reset / Reset all are pending until then | AUTOMATED (VM) | UC.CL.05 | FR.9.19 |
| TC.CL.33 | Close with pending colours asks, as for other unsaved settings | AUTOMATED (VM) | UC.CL.05 | FR.9.19 |
| TC.CL.34 | The named colours are the 140 CSS names with their correct values; the filter matches part of a name; a click sets the new colour keeping opacity; a matching colour shows its name | AUTOMATED (VM) | UC.CL.05 | FR.9.19 |
| TC.CL.35 | Complementary, analogous and triadic swatches have the right hues (+180, ±30, +120/+240), keep saturation, brightness and opacity, follow the new colour, and set it on click | AUTOMATED (VM) | UC.CL.05 | FR.9.19 |
| TC.CL.36 | The picker popup with the new sections fits on the screen and its controls are laid out (STA, real layout) | AUTOMATED (STA) | UC.CL.05 | FR.9.19 |
| TC.CL.37 | End to end: change a colour in the picker, Apply, and the tree row / transcript uses the new colour | AUTOMATED (E2E) | UC.CL.05 | FR.9.19 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.9.18 | TC.CL.20–26 |
| FR.9.19 | TC.CL.30–37 |
