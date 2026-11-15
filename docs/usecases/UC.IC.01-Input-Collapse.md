# UC.IC.01 — Collapsible Input Area

Related requirements: FR.12.29 (also FR.12.1, FR.12.2, FR.12.11, FR.23.12) in [requirements.md](/docs/requirements.md). Related: [UC.FV.01](/docs/usecases/UC.FV.01-Focus-View.md).

## Actors

- **User** — works in a session, switches views and sessions.
- **Client** — the app showing the session view.

## Preconditions (common)

- Two sessions, A and B, each with a Session view and a Focus view.

---

## UC.IC.01 — Collapse and expand with the splitter

**Main flow:**
1. The User opens session A in the Session view. The input area is expanded.
2. The User double-clicks the splitter above the input area. The area collapses: with the command bar shown, only the command bar remains; with it hidden, one line of the input text.
3. The side column shows one button: Send.
4. The User double-clicks the splitter again. The area expands to its former height.

**Alternative flows:**
- **A1 — A turn is running:** the single button is Stop, in both states.
- **A2 — Drag from collapsed:** dragging the splitter expands the area first.
- **A3 — Long text:** in the one-line state the text scrolls and stays editable.

**Covers:** FR.12.29.

---

## UC.IC.02 — Defaults per view

**Main flow:**
1. The User opens session A in the Session view: expanded.
2. The User switches A to the Focus view: collapsed.
3. The User switches A back to the Session view: expanded.

**Covers:** FR.12.29, FR.23.12.

---

## UC.IC.03 — Switching sessions resets the state

**Main flow:**
1. The User is in the Focus view of A and expands the input area.
2. The User selects session B, which lands in its Focus view: collapsed.
3. The User returns to A: collapsed (the default), not expanded.
4. The same holds for the Session view: collapse it in A, go to B and back; A is expanded.

**Postconditions:** nothing about the state is stored; a restart gives the defaults.
**Covers:** FR.12.29.
