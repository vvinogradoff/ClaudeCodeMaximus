# UC.RD.01 — Recent dropdown recency colour

Related requirements: FR.9.13. Test cases: [TC.RD.01](/docs/testcases/TC.RD.01-Recent-Dropdown-Recency.md).

## UC.RD.01 — See how recent the open session is from the closed dropdown
**Actor:** the User. **Precondition:** a session is open.
**Main flow:** 1. The User looks at the Recent dropdown button in the title bar. 2. The button shows the session's name on the recency colour of that session (the same colour its row has). 3. The User opens Settings › Appearance, unticks **Show recency colour in the closed Recent dropdown**; the button turns plain at once. 4. Ticking it again brings the colour back at once.
**Alternative flows:** **A1** — The session has no recent activity (or none is open): the button is plain. **A2** — A file saved by an older build has no value: the box is ticked. **A3** — Another session is opened: the colour changes with it.
**Covers:** FR.9.13.
