# UC.ME.01 — The session's model and effort

Related requirement: [FR.12.35](/docs/requirements/FR.12-Input-and-Composer.md). Test cases: [TC.ME.01](/docs/testcases/TC.ME.01-Session-Model-and-Effort.md).

## Actors

- **User** — picks a model or effort in the command bar of a session, on the desktop or the web.
- **Daemon** — keeps the session's settings and runs its turns.

---

## UC.ME.01 — The effort picked for a session stays

**Trigger:** the user picks **Max** in the Effort selector of a session and sends.

1. The turn runs with `--effort max`.
2. Tessyn stores Max as the session's effort.
3. Later the user restarts Tessyn, or the client updates itself, and opens the session.
4. The Effort selector shows **Max**. The user sends without touching it, and the turn runs with `--effort max`.
5. The session's other turns (scheduled turns, worker results, Telegram replies) also run with Max.

**Outcome:** the selector and the turns agree. The effort no longer falls back to the CLI default after a restart.

---

## UC.ME.02 — Back to Default

**Trigger:** the user picks **Default** as the model or effort of a session that has its own value or, for the model, inherits one from the directory or the app.

1. The send runs without `--model` (or `--effort`).
2. Tessyn stores Default as the session's choice. Later sends without a pick run without the flag, and the selector shows Default.

---

## UC.ME.03 — A session that has no effort of its own yet

**Trigger:** the user opens a session that existed before this change, or that was created without an effort.

1. The Effort selector shows Default, and sends run without `--effort`, as they did before.
2. The user picks Max once; from then on UC.ME.01 applies.
