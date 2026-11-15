# UC.FD.01 — Focus Reply Drafts

Related requirements: FR.23.19 (also FR.23.14–FR.23.16) in [requirements.md](/docs/requirements.md). Related: [UC.FV.01](/docs/usecases/UC.FV.01-Focus-View.md).

## Actors

- **User** — types replies in Focus cards, closes and reopens the app.
- **Client** — the WPF app.
- **Tessyn** — the daemon that keeps the drafts store.

## Preconditions (common)

- The session has at least one open Attention Item with a reply box (FR.23.14).

---

## UC.FD.01 — Typed reply survives closing the app

**Main flow:**
1. The User types "use option B" in the reply box of item `a3` of session S, and does not send it.
2. After the debounce the Client saves the text as the reply draft of (S, `a3`).
3. The User closes the app. Any change not yet saved is flushed first.
4. The User starts the app and opens S in the Focus view.
5. The card of `a3` shows "use option B" in its reply box.

**Alternative flows:**
- **A1 — Several cards:** each card restores its own text; the composer draft of S is untouched.
- **A2 — Cleared box:** the User empties the box; the draft is removed, and a restart shows an empty box.

**Covers:** FR.23.19.

---

## UC.FD.02 — Sending or closing deletes the draft

**Main flow:**
1. The User has reply drafts for `a3` and `a5`.
2. The User presses Reply on `a3`. The prompt is sent (FR.23.15); the draft of `a3` is deleted. The draft of `a5` stays.
3. The User presses Dismiss on `a5`. The item closes; its draft is deleted.

**Alternative flows:**
- **A1 — Send answers:** every box that was sent loses its draft.
- **A2 — Session closes the item:** the session calls `resolve_attention` for `a5`; the draft is deleted and the card disappears.
- **A3 — Session deleted:** every reply draft of the session is deleted with it.
- **A4 — Orphan:** a draft whose item is gone or no longer open is removed when the items load.

**Covers:** FR.23.19, FR.23.15.

---

## UC.FD.03 — A remote client reads and writes a reply draft

**Main flow:**
1. A local client calls `draft.set { nodeId, itemId: "a3", text }`; the result is `{ revision }`.
2. Every local client receives `draft.changed { nodeId, itemId: "a3", revision }`.
3. `draft.get { nodeId, itemId: "a3" }` returns the text and revision.
4. `draft.delete { nodeId, itemId: "a3" }` returns `{ deleted: true }`.

**Alternative flows:**
- **A1 — No `itemId`:** the calls address the composer draft as before (older callers unchanged).
- **A2 — Unknown or closed item:** `-32602`.
- **A3 — Stale `baseRevision`:** `-32000` "Draft changed", as for the composer draft.
- **A4 — Web client:** the methods are not relayed.

**Covers:** FR.23.19.
