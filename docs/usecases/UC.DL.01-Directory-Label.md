# UC.DL.01 — Short directory labels in the session tree

Related requirement: [FR.1.3 — Directory node display name](/docs/requirements.md). Related: [UC.21.8](/docs/usecases/UC.21-Telegram-Bridge.md) (Telegram topic names use the same label).

## Actors

- **User** — browsing and organising sessions in the left-panel tree.
- **Session tree** — renders one Directory node per working directory.

## Preconditions

- The tree contains Directory nodes for one or more working directories, with or without a `.git` folder.

---

## UC.DL.01.1 — Directory node shows a short label

**Trigger:** The tree is displayed (app start, directory added, session created).

1. Each Directory node shows only the last segment of its path, e.g. `C:\Projects\oDesk\HardStop` → `HardStop`.
2. If the last segment is a well-known project folder (`Code`, `Docs`, `Data`), the parent's name is shown instead, repeatedly: `C:\Projects\oDesk\HardStop\Code` → `HardStop`.
3. Git is not consulted: a directory with or without `.git` is labelled by the same rule.

**Outcome:** The tree is compact and readable; the same label appears in the recent-sessions dropdown, import pickers, `list_sessions` and Telegram topic names.

## UC.DL.01.2 — User inspects the full path

**Trigger:** The User hovers over a Directory node.

1. A tooltip shows the node's full working-directory path.

**Outcome:** Directories with identical labels (e.g. `HardStop` and `HardStop\Code`) can be told apart; they are separate nodes because duplicates are prevented by path, not by label.

---

## Out of scope for this use case set

- Renaming Directory nodes or user-defined labels.
- Disambiguating duplicate labels automatically.
