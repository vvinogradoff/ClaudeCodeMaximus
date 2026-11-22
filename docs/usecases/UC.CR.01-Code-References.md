# UC.CR.01 — Code references: session suggestions for `#`

Related requirements: [FR.7.13, FR.7.14](/docs/requirements/FR.07-Code-Reference-Autocomplete.md), [FR.4.8](/docs/requirements/FR.04-Application-Configuration.md). Test cases: [TC.CR.01](/docs/testcases/TC.CR.01-Code-References.md).

## Actors

- **User** — writes a message in the message box of a session and refers to another session.
- **Daemon** — answers `autocomplete.suggest` from the session registry and the code index.

---

## UC.CR.01 — Refer to a session with `#`

**Precondition:** Code references is OFF (the default). The current session is in the group *Perf* of the project *Tessyn*; other sessions exist in the same group, in other groups of *Tessyn*, and in the project *Web*.
**Trigger:** the user types `see #ste` in the message box.

1. The popup shows sessions whose name contains `ste`, case-insensitively: first those in the group *Perf*, then other sessions of *Tessyn*, then (only because fewer than 10 were found) sessions of other projects. Within a tier, names that start with `ste` come before names that contain it, then favourites, then tree order. At most 15 rows.
2. Each row shows the name with `ste` in bold, the kind `session`, and the project and group path.
3. The user presses Down, then Enter. The text `#ste` is replaced and the box holds `see session "Stage 2 tests" (3f9a12bc)`.
4. The user sends. The agent reads the Short Node Id and can call `read_session` with it.

**Outcome:** the user refers to a session by picking it from a list; no code index is built or waited for.

---

## UC.CR.02 — Names with spaces

**Trigger:** the user types `#stage 2` (a space in the query).

1. The popup keeps showing sessions whose name contains `stage 2`.
2. The user types more characters so that no session name matches: the popup closes. Deleting characters until a session matches opens it again.
3. A line break between the `#` and the caret closes the popup. Esc closes it. A bare `#` shows nothing.

**Alternative:** `##stage` is a file search (UC unchanged), never a session search.

---

## UC.CR.03 — Which sessions are never offered

**Trigger:** the user types `#` followed by text that matches the current session's own name and the names of worker sessions.

1. The current session is not in the list.
2. Worker sessions (spawned by a supervisor, or at orchestration depth above 0) are not in the list.

---

## UC.CR.04 — Turn Code references ON

**Precondition:** Code references is OFF.
1. The user opens Settings > General, ticks **Code references** and presses Save. The hint under the box says it applies at the next suggestion.
2. Without restarting anything, the user types `#Vis` in a session whose directory holds C# code: the popup offers C# symbols (the "Building the code index" state may show first while the index builds), exactly as before.
3. The user clears the box again and saves. The next `#` suggests sessions. An index that already holds symbols keeps them for up to 5 minutes of idleness and is not used for `#`.

---

## UC.CR.05 — Files and paths do not depend on the setting

**Trigger:** with Code references ON or OFF the user types `##logo` or `C:\Users\`.

1. `##logo` lists files of any type under the session's working directory (images, videos, `.md`, `.env`, `.csv`, `.html`, `.txt`, code), as before.
2. `C:\Users\` lists the directory entries, as before.

**Covers:** FR.7.13, FR.7.14, FR.4.8, FR.7.2, FR.7.9, FR.7.12.
