# UC.RD.01 — Recent dropdown recency colour

Related requirements: FR.9.13, FR.9.15. Test cases: [TC.RD.01](/docs/testcases/TC.RD.01-Recent-Dropdown-Recency.md).

## UC.RD.01 — See how recent the open session is from the closed dropdown
**Actor:** the User. **Precondition:** a session is open.
**Main flow:** 1. The User looks at the Recent dropdown button in the title bar. 2. The button shows the session's name on the recency colour of that session (the same colour its row has). 3. The User opens Settings › Appearance, unticks **Show recency colour in the closed Recent dropdown**; the button turns plain at once. 4. Ticking it again brings the colour back at once.
**Alternative flows:** **A1** — The session has no recent activity (or none is open): the button is plain. **A2** — A file saved by an older build has no value: the box is ticked. **A3** — Another session is opened: the colour changes with it.
**Covers:** FR.9.13.

## UC.RD.02 — Hover and open the dropdown without losing the colour
**Actor:** the User. **Precondition:** a session with a recency colour is open and the colour setting is on.
**Main flow:** 1. The User moves the pointer over the closed Recent dropdown button; a faint overlay appears and the recency colour stays under it. 2. The User opens the list; the button keeps its colour (still with the faint overlay). 3. The User moves over a row of the open list; the row keeps its own recency colour under a faint overlay.
**Alternative flows:** **A1** — No tint (tier 0, or the setting is off): the overlay is the only change, as before.
**Covers:** FR.9.15, FR.9.11.

## UC.RD.03 — See the open session's profile from the closed dropdown
**Actor:** the User. **Precondition:** a session with a profile is open. *Status: approved (A36, FR.1.33).*
**Main flow:** 1. The User looks at the Recent dropdown button in the title bar. 2. The button shows the session's name, then its profile name in the secondary colour, then the active workers badge (when workers run), then the chevron. 3. The User opens the list; the open session's row shows the same profile name. 4. The User opens another session; the button's profile name changes with it.
**Alternative flows:** **A1** — No session is open, or the session has no profile: no profile text, no gap. **A2** — A profile switch is waiting: the profile name is followed by `→ target` in the caution colour, and both update when the switch completes. **A3** — The title bar is narrow: the session name trims first; the badge and the chevron keep their places.
**Covers:** FR.1.33, FR.1.28, FR.1.20.
