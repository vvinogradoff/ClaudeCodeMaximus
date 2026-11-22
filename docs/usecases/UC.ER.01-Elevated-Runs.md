# UC.ER.01 — Elevated runs requested by an agent

Related requirements: [FR.15.16–FR.15.20](/docs/requirements/FR.15-Agent-Orchestration.md), [FR.23.31, FR.23.32](/docs/requirements/FR.23-Focus-View.md), [FR.32.16](/docs/requirements/FR.32-Client-Hosting.md); wire: tdcp-spec 10.A13. Test cases: [TC.ER.01](/docs/testcases/TC.ER.01-Elevated-Runs.md).

## Actors

- **Agent** — a session (top-level or worker) that needs a command run as administrator.
- **User** — the person at this PC; approves each run.
- **Daemon** — keeps the request, builds the launch description, watches the run folder, delivers the outcome.
- **WPF client** — shows the card and starts the approved run through the Windows administrator prompt.
- **Remote user** — the same person on Tessyn Web or Telegram, away from the PC.

## Preconditions

- `AgentToolsEnabled` is on; the daemon runs normally (not elevated); WPF is connected on this PC. The Tessyn project `C:\Projects\Personal\Tessyn` is registered; the worktree `C:\Projects\Personal\Tessyn-wt-recent` exists.

---

## UC.ER.01 — Run the WPF E2E tests as administrator

**Trigger:** the Agent (top-level, working in the worktree) has built the solution and wants the WPF E2E suite.

1. The Agent calls `request_elevated_run` with `command` `powershell -NoProfile -ExecutionPolicy Bypass -File tools\e2e\Run-WpfE2E.ps1 --no-build --filter "FullyQualifiedName~Recent"`, `workingDir` the worktree, `reason` "WPF E2E for the Recent tab", `timeoutMinutes` 90. The call returns `Elevated run 3f9a12bc requested (pending)...` and the Agent ends its turn.
2. The User gets a desktop toast and a Telegram text. In the session's Focus panel an Ask card appears: reason, working directory, the command in a monospace block (whole, scrollable), and the buttons **Run as administrator** and **Decline**.
3. The User reads the command, clicks **Run as administrator**. The card says "Waiting for the Windows administrator prompt..."; the Windows prompt appears; the User confirms.
4. The card shows "Running as administrator since 14:02 (0m 12s, limit 90 min)" with **Stop** and an Output expander.
5. The run ends with exit code 0 after 41 minutes. The card closes with `Completed: exit code 0, 41m 05s`.
6. The Agent gets a new turn: `[Elevated run 3f9a12bc completed] ...` with the exit code, the duration, the log path and the last 200 lines. It reports the result.

**Outcome:** the Agent ran a command that needs administrator rights without the User typing it, and the User saw and approved exactly that command.

---

## UC.ER.02 — A worker asks; the User watches the top-level session

**Trigger:** a Worker Session (spawned by the top-level session *Supervisor*) calls `request_elevated_run`.

1. The card appears in the worker's Focus view **and** in the Supervisor's, where it says "Requested by worker *<name>* (<id>) of this session". The notification names the worker.
2. The User approves from the Supervisor's card. Both cards follow the same run and both close with the same outcome.
3. The outcome turn goes to the **worker**. The worker reports to its Supervisor as usual (a Mailbox Delivery).

**Alternative:** the Supervisor was deleted from the tree: only the worker's card exists.

---

## UC.ER.03 — The User declines

**Trigger:** the User clicks **Decline**, optionally after typing a note.

1. The card closes with `Declined`. Nothing runs, no Windows prompt appears.
2. The Agent gets `[Elevated run 3f9a12bc declined by the user]` with `Note: <the note>`. It must not request the same run again without the User asking.

---

## UC.ER.04 — The Windows prompt is cancelled, or the start fails

**Trigger:** the User clicked **Run as administrator** but cancels the Windows prompt (or the start fails, for example the program cannot be started).

1. The card closes with `Declined at the Windows prompt` (or `Failed to start: <reason>`).
2. The Agent gets the outcome turn with that status. To try again it makes a **new** request (a request is never run twice).

---

## UC.ER.05 — Stop a running run; the time limit

**Trigger:** a run is going and the User clicks **Stop**, or `timeoutMinutes` pass.

1. **Stop:** the daemon asks the wrapper (a marker file, since a normal WPF can't signal an elevated process); within seconds the whole process tree is killed. The card closes with `Cancelled (stopped by the user)`; the Agent gets the status, the exit code (the killed process's), the duration and the output so far.
2. **Time limit:** the wrapper kills the process tree. The card closes with `Timed out after 90 min`; the Agent gets `timed out` and the output so far.

---

## UC.ER.06 — The Agent withdraws; a request expires

1. The Agent calls `cancel_elevated_run` for a pending request: the card closes with `Cancelled (withdrawn by the agent)`. The Agent gets no outcome turn.
2. A pending request nobody approves for 24 hours becomes `expired`; the card closes with `Expired` and the Agent gets the outcome turn.
3. A request that is already `launching` or `running` can't be withdrawn: the Agent is told the User can stop it from the card.

---

## UC.ER.07 — Away from the PC

**Trigger:** the User is on the phone (Tessyn Web or Telegram) when the request arrives.

1. The Telegram message carries the reason, the command (cut at 300 characters, marked) and "Approve it on the PC". It has no button.
2. The Web client's Focus tab shows the card read-only with the note **Approve on the PC**. No Run or Decline there.
3. The User comes back to the PC and approves from the WPF card; or the request expires after 24 hours.

---

## UC.ER.08 — The daemon restarts

1. **Pending:** the request is still pending after the restart, with its original expiry; the card is back.
2. **Running:** the daemon finds `started.json` and its wrapper still running and resumes watching; or finds `result.json` and delivers the outcome; or finds neither and reports `failed to start` ("The daemon restarted before the outcome was known").

---

## UC.ER.09 — Limits and refusals

1. A fourth open request from one session is refused with the message that says 3 are waiting.
2. The Agent asks for a working directory outside its tree and outside every project and worktree: refused with the list of allowed places.
3. The daemon runs elevated already (FR.32.10): the Agent is told to run the command directly; no card is made.
4. Asking for the same command and folder twice while the first is pending returns the first request's id.

**Covers:** FR.15.16–FR.15.20, FR.23.31, FR.23.32, FR.32.16.
