# UC.TO.01 — Tree order: new sessions last, keyboard moves

Related requirements: FR.1.30, FR.1.31. Test cases: [TC.TO.01](/docs/testcases/TC.TO.01-Tree-Order.md).

## UC.TO.01 — Create a session at the end of a project
**Actor:** the User. **Precondition:** a directory with a group and three sessions is in the tree.
**Main flow:** 1. The User selects the directory (or one of its sessions) and clicks New session. 2. They enter a name. 3. The new session appears as the **last** session of the directory, below the other three, selected and in view.
**Alternative flows:** **A1** — The User right-clicks a group and chooses Add Session: the session is the last one in the group. **A2** — On the web, with a session selected, the User creates a session: it goes right after the selected one. **A3** — On the web, with a directory selected: it goes last.
**Covers:** FR.1.30.

## UC.TO.02 — Reorder sessions with the keyboard
**Actor:** the User. **Precondition:** the tree has the focus; a directory has sessions A, B, C, D in that order.
**Main flow:** 1. The User selects C. 2. Ctrl+Up: the order is A, C, B, D; C stays selected. 3. Ctrl+Up again: C, A, B, D. 4. Ctrl+Up again: nothing changes. 5. Ctrl+Down three times: A, B, D, C. The order is kept after a restart.
**Alternative flows:** **A1** — C is running: nothing moves and the tree status line says why, as for a drag. **A2** — A worker, a group or a directory is selected: nothing happens. **A3** — A tree search is active: nothing happens. **A4** — C has workers: they stay under C. **A5** — The User holds the key down: a press during a move is ignored, so nothing moves twice by surprise.
**Covers:** FR.1.31.
