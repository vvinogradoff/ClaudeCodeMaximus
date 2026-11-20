# TC.RD.01 — Recent dropdown recency colour

Related requirements: FR.1.33, FR.9.13, FR.9.15, FR.9.10, FR.9.11. Use cases: [UC.RD.01](/docs/usecases/UC.RD.01-Recent-Dropdown-Recency.md).

**Legend.** AUTOMATED = unit / view-model / STA view test with real layout.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.RD.01 | The setting is on by default; a settings file without it reads as on | AUTOMATED | UC.RD.01 | FR.9.13 |
| TC.RD.02 | The Appearance checkbox saves, is reported as a change, and comes back after a reload | AUTOMATED | UC.RD.01 | FR.9.13 |
| TC.RD.03 | The closed button takes the open session's tier colour; turning the setting off or on changes it at once | AUTOMATED | UC.RD.01 | FR.9.13 |
| TC.RD.04 | The tint follows the open session (a session with no recent activity, and no session, are plain) | AUTOMATED | UC.RD.01 | FR.9.13 |
| TC.RD.05 | Hover and open of the closed button lay a faint overlay; no trigger replaces the recency background | AUTOMATED | UC.RD.02 | FR.9.15 |
| TC.RD.06 | The rows of the open list keep their own colour under the hover overlay | AUTOMATED | UC.RD.02 | FR.9.15, FR.9.11 |
| TC.RD.07 | The closed button shows the open session's profile name (pinned, else the profile) between the name and the active workers badge, in the secondary colour with the full name as tooltip; it equals the row's profile text in the open list | AUTOMATED | UC.RD.03 | FR.1.33.1, FR.1.33.2 |
| TC.RD.08 | No session open, or a session without a profile: no profile text and no gap | AUTOMATED | UC.RD.03 | FR.1.33.3 |
| TC.RD.09 | A pending switch shows `→ target` in the caution colour after the profile name; both update when it completes | AUTOMATED | UC.RD.03 | FR.1.33.4 |
| TC.RD.10 | Narrow button: the session name trims first, then `→ target`, then the profile name; the badge and the chevron keep their places | AUTOMATED (STA, real layout) | UC.RD.03 | FR.1.33.5 |
| TC.RD.11 | Opening another session, a profile pick or a finished switch updates the button at once | AUTOMATED | UC.RD.03 | FR.1.33.6 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.9.13 | TC.RD.01–04 |
| FR.9.15 | TC.RD.05–06 |
| FR.1.33 | TC.RD.07–11 |
