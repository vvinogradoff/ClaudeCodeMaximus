# UC.ST.01 — Close Settings with unsaved changes

Related requirements: FR.4.7, FR.9.19. Test cases: [TC.ST.01](/docs/testcases/TC.ST.01-Settings-Close-Guard.md).

## UC.ST.01 — Close Settings without losing an edit
**Actor:** the Architect. **Precondition:** Settings is open.
**Main flow:** 1. The Architect changes a font size in Appearance and picks a colour in Colours without applying it. 2. They press Close. 3. A dialog says `Unsaved changes in Appearance, Colours.` with Save, Discard and Cancel. 4. They choose Save: both are stored, the new colour shows everywhere, and the window closes.
**Alternative flows:** **A1** — Discard: nothing is stored; reopening Settings shows the old values. **A2** — Cancel: the window stays open with the edits. **A3** — No edits: Close closes at once. **A4** — Esc, the title bar close button or Alt+F4: the same dialog. **A5** — Save fails (for example the daemon is offline): the window stays open and shows the error. **A6** — The main window is closed while Settings has edits: the dialog appears, and Cancel keeps both windows open.
**Covers:** FR.4.7, FR.9.19.
