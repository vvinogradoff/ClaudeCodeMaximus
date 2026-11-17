# TC.RT.01 — Recent Tab

Related requirements: FR.1.15, FR.1.16 in [requirements.md](/docs/requirements.md). Related use cases: [UC.RT.01](/docs/usecases/UC.RT.01-Recent-Tab.md).

**Legend.** AUTOMATED = a Core unit test in `code/TessynDesktop.Tests` (xUnit); the intended test class is named in the case. MANUAL = a manual UI test in the WPF client (the steps were written for the previous desktop client; where WPF differs in a detail, the requirement wins and the step is corrected at the first run). Exact strings are expected results.

## Summary

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.RT.01 | `RecentSessionVisibilityService.IsGroupExpandedOnRecentTabSwitch` | AUTOMATED | UC.RT.03, UC.RT.04 | FR.1.16 |
| TC.RT.02 | Content and order | MANUAL | UC.RT.01 | FR.1.16 |
| TC.RT.03 | Rows | MANUAL | UC.RT.01 | FR.1.16 |
| TC.RT.04 | Click selects, including a repeat click | MANUAL | UC.RT.02 | FR.1.16 |
| TC.RT.05 | Collapse on switch with a selected session | MANUAL | UC.RT.03 | FR.1.16 |
| TC.RT.06 | No session selected: all expanded | MANUAL | UC.RT.04 | FR.1.16 |
| TC.RT.07 | Manual toggles hold until the next switch | MANUAL | UC.RT.05 | FR.1.16 |
| TC.RT.08 | Worker Session selected in the tree | MANUAL | UC.RT.06 | FR.1.16 |
| TC.RT.09 | Three tabs, start tab, dropdown | MANUAL | UC.RT.07 | FR.1.15 |
| TC.RT.10 | Rebuild on every selection of the tab | MANUAL | UC.RT.01 | FR.1.16 |

## FR → TC coverage

| FR | Test cases |
|---|---|
| FR.1.15 | TC.RT.09 |
| FR.1.16 | TC.RT.01–TC.RT.08, TC.RT.10 |

---

## Automated tests

### TC.RT.01 — `RecentSessionVisibilityServiceTests` — FR.1.16 (UC.RT.03, UC.RT.04)

`IsGroupExpandedOnRecentTabSwitch(groupDirectoryPath, selectedSessionDirectoryPath)`:
- **TC.RT.01-1 No selection:** `selectedSessionDirectoryPath == null` → `true` for any group path.
- **TC.RT.01-2 Same path:** the group path equals the selected session's path → `true`.
- **TC.RT.01-3 Same path, other case:** `C:\Proj\X` vs `c:\proj\x` → `true` (OrdinalIgnoreCase).
- **TC.RT.01-4 Another path:** a different path → `false`.
- **TC.RT.01-5 Prefix is not equality:** `C:\Proj\X` vs `C:\Proj\X2` and `C:\Proj\X\Sub` → `false`.

---

## Manual UI tests

Common set-up: directories X, Y and Z (Y inside a Group) each with top-level sessions; session S1 in X with a Worker Session W (and a sub-worker W2 under W); a session in Y with an unsent draft; one session with the favorite star set; one recently active.

### TC.RT.02 — Content and order — FR.1.16 (UC.RT.01)

1. Open Recent. **Expected:** every top-level session is listed, including those under the Group; W and W2 are not listed.
2. **Expected:** one header per Directory node (📂 and the Auto-derived label), in tree order.
3. In the group holding the draft session. **Expected:** it comes first; the others follow, newest Last Activity first.
4. Send a prompt in a session. Switch to Tree and back to Recent. **Expected:** it moves to the top of the non-draft part of its group.

### TC.RT.03 — Rows — FR.1.16 (UC.RT.01)

1. **Expected:** each row shows status dots, the name, the ✏ indicator for a draft, the star for a favorite, the profile name, the last prompt time, the recency background, and a dotted border for a recently active session.
2. **Expected:** no ✏ or star on rows without a draft or favorite.

### TC.RT.04 — Click selects, including a repeat click — FR.1.16 (UC.RT.02)

1. Click a row. **Expected:** the session is selected in the tree and opens.
2. Select a different session in the tree, open Recent, click the same row again. **Expected:** it is selected again (the click is not swallowed).
3. Click row P, then row Q, then P again within Recent. **Expected:** each click selects its session.

### TC.RT.05 — Collapse on switch with a selected session — FR.1.16 (UC.RT.03)

1. Select S1 (in X) in the tree; switch to Recent. **Expected:** group X is expanded (▾); Y and Z are collapsed (▸).
2. Select a session in Y in the tree; switch to Recent. **Expected:** Y expanded; X and Z collapsed.
3. Switch Tree → Recent repeatedly. **Expected:** the rule applies at every switch.

### TC.RT.06 — No session selected: all expanded — FR.1.16 (UC.RT.04)

1. Start the app with no session selected (or deselect). Open Recent. **Expected:** every group is expanded.

### TC.RT.07 — Manual toggles hold until the next switch — FR.1.16 (UC.RT.05)

1. Select S1; switch to Recent (X expanded, others collapsed).
2. Click header Y. **Expected:** Y expands (▾). Click header X. **Expected:** X collapses (▸).
3. Click a row in Y (switches selection; the panel stays on Recent or follows the existing behaviour). **Expected:** the other groups keep their manual state while on the tab.
4. Switch to Tree and back. **Expected:** the rule re-applies: only the selected session's directory is expanded.

### TC.RT.08 — Worker Session selected in the tree — FR.1.16 (UC.RT.06)

1. Select W2 (a sub-worker under W under S1 in X); switch to Recent. **Expected:** group X is expanded; Y and Z are collapsed.
2. **Expected:** W and W2 are not listed; S1 is.
3. Repeat with a Worker Session in a directory inside a Group. **Expected:** the group of the Directory node it is nested under is expanded.

### TC.RT.09 — Three tabs, start tab, dropdown — FR.1.15 (UC.RT.07)

1. Start the app. **Expected:** tabs are Tree, Recent, Tags in that order; Tree is selected.
2. Select Recent, restart. **Expected:** the app starts on Tree (not persisted).
3. On Recent or Tags, pick a session in the top-bar recent-sessions dropdown. **Expected:** the panel switches to Tree and the session is selected.

### TC.RT.10 — Rebuild on every selection of the tab — FR.1.16 (UC.RT.01)

1. Create a new session and type a draft in another. Switch to Recent. **Expected:** the new session is listed; the draft session is first in its group with ✏.
2. Rename a session; switch Tree → Recent. **Expected:** the new name shows.
