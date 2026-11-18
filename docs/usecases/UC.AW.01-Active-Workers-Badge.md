# UC.AW.01 — Active workers badge

Related requirements: FR.1.28, FR.9.16, FR.1.21, TDCP section 10.A6. Test cases: [TC.AW.01](/docs/testcases/TC.AW.01-Active-Workers-Badge.md).

## UC.AW.01 — See which sessions have workers running right now
**Actor:** the User. **Precondition:** a supervisor session has workers (an orchestration) and the left panel is shown.
**Main flow:** 1. A worker of the supervisor starts running. 2. The supervisor's row, in the tree, the Recent tab, the Tags tab list and the Recent dropdown, shows a framed red number: 1. 3. A second worker, or a worker of a worker, starts: the number becomes 2 (direct and nested count). 4. A worker finishes: the number drops. 5. The last one finishes: the badge disappears. 6. Next to it the row still says `104 workers` (the total).
**Alternative flows:** **A1** — The supervisor is the open session: the closed Recent dropdown button shows the same badge, left of its chevron. **A2** — A session with no running worker shows no badge and no gap. **A3** — The Architect changes the colours in Settings › Colours (Session status): the badge follows at once. **A4** — Workers start and stop in a burst: the number settles on the final value; the daemon sends one `session.changed` per supervisor per window.
**Covers:** FR.1.28, FR.9.16.
