# UC.IN.01 — Input Navigation

Related requirements: FR.2.6 (see `docs/requirements/`). Test cases: [TC.IN.01](/docs/testcases/TC.IN.01-Input-Navigation.md).

## Actors
- **User**. **Tessyn** (WPF transcript).

## Preconditions (common)
- A session with the user's own inputs and automated ones (scheduled, supervisor).

---

## UC.IN.01 — Distinguish automated inputs
**Main flow:** the User opens a session with scheduled and supervisor prompts; they appear teal and labelled SCHEDULED or SUPERVISOR.
**Covers:** FR.2.6 (2, 3).

## UC.IN.02 — Walk my inputs
**Main flow:** the User clicks one of their own inputs (thin dotted border) and presses Up/Down; only their own inputs are visited, scrolled into view, including inputs outside the loaded page.
**Alternative flows:** **A1** — Click on a link or a text drag: no selection. **A2** — Click elsewhere or Esc clears.
**Covers:** FR.2.6 (4, 5).

## UC.IN.03 — Walk all inputs
**Main flow:** the User selects an automated input; Up/Down visit every input kind.
**Covers:** FR.2.6 (5).

## UC.IN.04 — Scroll with no selection
**Main flow:** the User clicks the empty transcript; Up/Down scroll a line, PgUp/PgDn a page.
**Covers:** FR.2.6 (6).

## UC.IN.05 — Selection survives updates
**Main flow:** an input is selected; a turn appends entries and the page re-renders; the selection stays. Another session clears it.
**Covers:** FR.2.6 (7).
