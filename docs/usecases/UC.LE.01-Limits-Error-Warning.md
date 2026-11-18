# UC.LE.01 — Limits-error warning

Related requirements: FR.1.29, FR.9.17, TDCP section 10.A7. Test cases: [TC.LE.01](/docs/testcases/TC.LE.01-Limits-Error-Warning.md).

## UC.LE.01 — See which sessions stopped on a usage limit
**Actor:** the User. **Precondition:** the left panel is shown.
**Main flow:** 1. A session's turn ends with Claude's "You've hit your session limit · resets 5:20pm" message. 2. Its row, in the tree, the Recent tab, the Tags tab list and the Recent dropdown, shows a yellow warning triangle (left of the date; in the dropdown left of the project and profile). 3. The User hovers it and reads the shortened message. 4. After the limit resets the User sends a prompt and the turn succeeds: the triangle disappears.
**Alternative flows:** **A1** — A worker (sub-session) in the tree hits the limit: its triangle is all the way right on the worker's name line, above its date. **A2** — Another error (an auth failure, a server error) ends a turn: no triangle. **A3** — A limits error and then another error: the triangle stays until a turn succeeds. **A4** — The Architect changes the colour in Settings › Colours (Session status): the triangle follows at once.
**Covers:** FR.1.29, FR.9.17.
