# UC.IC.01 — Collapsible Input Area

Related requirements: FR.12.29 (also FR.12.1, FR.12.2, FR.12.11, FR.23.12) in [requirements.md](/docs/requirements.md). Related: [UC.FV.01](/docs/usecases/UC.FV.01-Focus-View.md).

## Actors

- **User** — works in a session, switches views and sessions.
- **Client** — the WPF app showing the session pane.

## Preconditions (common)

- Two sessions, A and B, each with a Session view and a Focus view.

---

## UC.IC.01 — Collapse and expand with the splitter

**Main flow:**
1. The User opens session A in the Session view. The input area is expanded.
2. The User double-clicks the splitter above the input area. The area collapses: with the command bar shown, only the command bar remains; with it hidden, one line of the input text.
3. The side column shows one button: Send.
4. The User double-clicks the splitter again. The area expands to the height it had.

**Alternative flows:**
- **A1 — A turn is running:** the single button of the collapsed area is Stop; the expanded area shows Stop beside Send.
- **A2 — Drag from collapsed:** dragging the splitter expands the area first, then resizes it.
- **A3 — Long text:** in the one-line state the text scrolls sideways and stays editable.
- **A4 — Keyboard:** with the splitter focused, Enter or Space toggles.

**Covers:** FR.12.29.

---

## UC.IC.02 — Defaults per view

**Main flow:**
1. The User opens session A in the Session view: expanded.
2. The User selects Focus in the header: the Focus view replaces the transcript and the area is collapsed.
3. The User selects Session: the transcript is back and the area is expanded.

**Covers:** FR.12.29, FR.23.12.

---

## UC.IC.03 — Switching sessions resets the state

**Main flow:**
1. The User is in the Focus view of A and expands the input area.
2. The User selects session B, which lands in its Session view (B was never on Focus): expanded. The User selects Focus in B: collapsed.
3. The User returns to A. A is still on its Focus view (the selection is kept per session) and the area is collapsed, the default, not expanded.
4. The same holds for the Session view: collapse it in A, go to B and back; A is expanded.

**Postconditions:** the collapsed state is never stored, and neither is the selection; a restart opens every session on the Session view, expanded.
**Covers:** FR.12.29, FR.23.12.

---

## UC.IC.04 — Settings toggle

**Main flow:**
1. In the expanded area the User presses the Settings toggle. The command bar hides; the text box stays.
2. The User collapses the area. The text box shows as one line, and the Settings toggle is not shown.
3. The User expands the area and presses Settings again. The command bar shows.

**Postconditions:** the command bar's visibility is remembered across restarts.
**Covers:** FR.12.29, FR.12.1, FR.12.11.
