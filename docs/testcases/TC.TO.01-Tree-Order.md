# TC.TO.01 — Tree order: new sessions last, keyboard moves

Related requirements: FR.1.30, FR.1.31, FR.1.32. Use cases: [UC.TO.01](/docs/usecases/UC.TO.01-Tree-Order.md).

**Legend.** AUTOMATED = daemon unit/integration tests (Core), view-model tests (VM), STA view tests with real layout (WPF), or end to end through `tools\e2e\Run-WpfE2E.ps1`.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.TO.01 | `tree.createSession` with a directory anchor puts the session after the directory's last session (also when the directory has groups and workers) | AUTOMATED (Core) | UC.TO.01 | FR.1.30 |
| TC.TO.02 | `tree.createSession` with a group anchor puts it after the group's last session; an empty group or directory gets it as its only session | AUTOMATED (Core) | UC.TO.01 | FR.1.30 |
| TC.TO.03 | `tree.createSession` with a session anchor still puts it right after that session; a worker anchor resolves to its supervisor | AUTOMATED (Core) | UC.TO.01 | FR.1.30 |
| TC.TO.04 | The `SessionAdded` event carries the right index and preceding session, and the desktop tree shows the row last without a refresh | AUTOMATED (Core, VM) | UC.TO.01 | FR.1.30 |
| TC.TO.05 | New session from the toolbar with a directory or a session selected: the row is last in the container, selected and scrolled into view | AUTOMATED (VM, STA) | UC.TO.01 | FR.1.30 |
| TC.TO.10 | Ctrl+Up / Ctrl+Down on a top-level session call `tree.moveNode` with the neighbour's index (up and down, in a directory and in a group, with groups before the sessions) | AUTOMATED (VM) | UC.TO.02 | FR.1.31 |
| TC.TO.11 | First session + Ctrl+Up, last session + Ctrl+Down: no call | AUTOMATED (VM) | UC.TO.02 | FR.1.31 |
| TC.TO.12 | A worker, a group or a directory selected, or a search active: no call | AUTOMATED (VM) | UC.TO.02 | FR.1.31 |
| TC.TO.13 | A running session: no move, the drag's refusal text in the status line | AUTOMATED (VM) | UC.TO.02 | FR.1.31 |
| TC.TO.14 | A press while a move is in flight is ignored | AUTOMATED (VM) | UC.TO.02 | FR.1.31 |
| TC.TO.15 | After the move the session is still selected, in view, and the tree keeps the keyboard focus; the key event is handled | AUTOMATED (STA) | UC.TO.02 | FR.1.31 |
| TC.TO.16 | Workers stay under their supervisor after a move | AUTOMATED (Core, VM) | UC.TO.02 | FR.1.31 |
| TC.TO.17 | End to end: New session lands last; Ctrl+Up twice and Ctrl+Down once give the expected order, which survives a tree refresh | AUTOMATED (E2E) | UC.TO.01, UC.TO.02 | FR.1.30, FR.1.31 |
| TC.TO.20 | `tree.moveNode` with a group and its current parent reorders it among the parent's groups (index after removal, clamped); sessions and sub-groups move with it; saved once, `tree.changed` raised | AUTOMATED (Core) | UC.TO.03 | FR.1.32 |
| TC.TO.21 | `tree.moveNode` with a group and another parent is refused with a reason; a group holding a running session may be moved | AUTOMATED (Core) | UC.TO.03 | FR.1.32 |
| TC.TO.22 | Ctrl+Up / Ctrl+Down on a group call `tree.moveNode` with the neighbour group's index; first/last group, a directory, or a search active: no call; a press in flight is ignored | AUTOMATED (VM) | UC.TO.03 | FR.1.32 |
| TC.TO.23 | After a group move the group is still selected and in view, the tree keeps the keyboard focus, and the groups stay above the sessions | AUTOMATED (STA) | UC.TO.03 | FR.1.32 |
| TC.TO.24 | End to end: a group moved through `tree.moveNode` shows in its new place in the app after `tree.changed` | AUTOMATED (E2E) | UC.TO.03 | FR.1.32 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.1.30 | TC.TO.01–05, 17 |
| FR.1.31 | TC.TO.10–17 |
| FR.1.32 | TC.TO.20–24 |
