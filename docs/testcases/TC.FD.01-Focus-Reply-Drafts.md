# TC.FD.01 — Focus Reply Drafts

Related requirements: FR.23.19 in [requirements.md](/docs/requirements.md). Related use cases: [UC.FD.01](/docs/usecases/UC.FD.01-Focus-Reply-Drafts.md).

**Legend.** AUTOMATED = unit or integration test (xUnit; Core in `TessynDesktop.Tests`, view models in the client's test project). MANUAL = checked by hand in the client; E2E for WPF is added with the WPF phase.

## Summary

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.FD.01 | Store: round trip per (session, item) | AUTOMATED | UC.FD.01 | FR.23.19 |
| TC.FD.02 | Store: empty text deletes; composer draft independent | AUTOMATED | UC.FD.01 | FR.23.19 |
| TC.FD.03 | Store: item ids are case-insensitive; unsafe ids rejected | AUTOMATED | UC.FD.01 | FR.23.19 |
| TC.FD.04 | Store: concurrent writes to different items and to one item | AUTOMATED | UC.FD.01 | FR.23.19 |
| TC.FD.05 | Lifecycle: delete on item close (answered, resolved, dismissed) | AUTOMATED | UC.FD.02 | FR.23.19 |
| TC.FD.06 | Lifecycle: delete on session delete | AUTOMATED | UC.FD.02 | FR.23.19 |
| TC.FD.07 | Lifecycle: orphan removed on load | AUTOMATED | UC.FD.02 | FR.23.19 |
| TC.FD.08 | API: `draft.get/set/delete` with `itemId`; unchanged without it | AUTOMATED | UC.FD.03 | FR.23.19 |
| TC.FD.09 | API: unknown or closed item gives `-32602`; stale revision `-32000` | AUTOMATED | UC.FD.03 | FR.23.19 |
| TC.FD.10 | API: `draft.changed` carries `itemId` only for a reply draft; local only | AUTOMATED | UC.FD.03 | FR.23.19 |
| TC.FD.11 | API: golden shapes of the optional field | AUTOMATED | UC.FD.03 | FR.23.19 |
| TC.FD.12 | Client VM: typing saves, debounced | AUTOMATED | UC.FD.01 | FR.23.19 |
| TC.FD.13 | Client VM: restore after restart | AUTOMATED | UC.FD.01 | FR.23.19 |
| TC.FD.14 | Client VM: flush on app close | AUTOMATED | UC.FD.01 | FR.23.19 |
| TC.FD.15 | Client VM: delete on Reply, Send answers and Dismiss | AUTOMATED | UC.FD.02 | FR.23.19, FR.23.15 |
| TC.FD.16 | End to end: type, close, restart, text is back | MANUAL | UC.FD.01 | FR.23.19 |

## Cases

- **TC.FD.01** — Save "x" for (S, a3); load returns "x"; another item and the composer draft are empty.
- **TC.FD.02** — Saving "" removes the file; the composer draft of S keeps its text.
- **TC.FD.03** — `A3` and `a3` address one draft; an id with path characters is rejected and writes no file.
- **TC.FD.04** — Parallel saves to two items keep both; parallel saves to one item leave one complete value, never a torn file.
- **TC.FD.05** — With drafts for open items, closing one by each route removes only its draft.
- **TC.FD.06** — Deleting the session removes the composer draft and all reply drafts.
- **TC.FD.07** — A draft for an unknown or non-open item is removed when the items load.
- **TC.FD.08** — Set, get and delete round trip with `itemId`; without it the composer draft is addressed, results unchanged.
- **TC.FD.09** — Unknown or closed `itemId` gives `-32602`; stale `baseRevision` gives `-32000`.
- **TC.FD.10** — Event has `itemId` for a reply draft and none for a composer draft; it is not relayed.
- **TC.FD.11** — Golden JSON: the result and event shapes, with and without `itemId`.
- **TC.FD.12 to 15** — As the titles say, with a fake daemon client and a fake clock.
- **TC.FD.16** — Type in two cards, close the app, restart, open the Focus view: both texts are back; send one, restart: only the other is back.

## FR → TC coverage

| FR | Test cases |
|---|---|
| FR.23.19 | TC.FD.01 to 16 |
