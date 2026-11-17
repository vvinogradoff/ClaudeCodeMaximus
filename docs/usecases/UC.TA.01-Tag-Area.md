# UC.TA.01 — Tag Area

Related requirements: FR.24.16 (see `docs/requirements/`), FR.24.7. Test cases: [TC.TA.01](/docs/testcases/TC.TA.01-Tag-Area.md).

## Actors
- **User**. **Tessyn** (WPF client and Tessyn Web).

## Preconditions (common)
- A session with more chips than fit one line; a second session with a few tags.

---

## UC.TA.01 — Expand and collapse
**Main flow:** 1. The session opens: one line of chips and a `+N` hint. 2. The User clicks empty space: the area grows to up to 10 lines, then scrolls. 3. The User double-clicks empty space: it collapses.
**Alternative flows:** **A1** — A click on a chip opens its link; the state does not change. **A2** — Double-click on the scrollbar changes nothing. **A3** — A session with no tags: nothing expands.
**Covers:** FR.24.16 (1–3, 7).

## UC.TA.02 — The state follows the session, not the app
**Main flow:** 1. The User expands the area and switches Session to Focus and back: still expanded. 2. The User opens another session: collapsed. 3. The User returns: collapsed. 4. After a restart: collapsed.
**Covers:** FR.24.16 (4–6).

## UC.TA.03 — Expand by accessibility action
**Main flow:** an assistive tool invokes Expand on "Session tags"; the area expands (no effect without tags).
**Covers:** FR.24.16 (8).
