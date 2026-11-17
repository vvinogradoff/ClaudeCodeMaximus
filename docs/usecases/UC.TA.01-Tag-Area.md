# UC.TA.01 — Tag Area

Related requirements: FR.24.16, FR.24.18 (see `docs/requirements/`), FR.24.7. Test cases: [TC.TA.01](/docs/testcases/TC.TA.01-Tag-Area.md).

## Actors
- **User**. **Tessyn** (WPF client).

## Preconditions (common)
- A session with more chips than fit one line; a second session with a few tags.

---

## UC.TA.01 — Expand and collapse
**Main flow:** 1. The session opens: one line of chips and a `+N` hint. 2. The User clicks empty space: the area grows to up to 10 lines, then scrolls. 3. The User double-clicks empty space: it collapses.
**Alternative flows:** **A1** — A click on a chip opens its link; the state does not change; a double-click on a chip does not collapse. **A2** — Double-click on the scrollbar changes nothing. **A3** — A session with no tags: nothing expands.
**Covers:** FR.24.16 (1–3, 7).

## UC.TA.02 — The state follows the session, not the app
**Main flow:** 1. The User expands the area and switches Session to Focus and back: still expanded. 2. The User opens another session: collapsed. 3. The User returns to the first session: collapsed again (every switch resets). 4. After a restart: collapsed.
**Covers:** FR.24.16 (4–6).

## UC.TA.03 — Expand by accessibility action
**Main flow:** an assistive tool invokes Expand on "Session tags"; the area expands (no effect without tags).
**Covers:** FR.24.16 (8).

## UC.TA.04 — Change the tag cloud font size
**Main flow:** 1. The User opens Settings › Appearance › **Tag cloud** and enters a number from 8 to 40. 2. Every visible chip, in the tag area and the Tags tab, changes size at once. 3. The collapsed area is still exactly one chip line high.
**Alternative flows:** **A1** — The area is expanded: it keeps up to 10 lines of the new chip height. **A2** — Restart: the size is kept. **A3** — A number outside 8–40, or text, is refused and nothing changes. **A4** — (WITHDRAWN 2026-11-17 (decision A47).) The one-time settings import (FR.32.4) does not exist any more.
**Covers:** FR.24.18.
