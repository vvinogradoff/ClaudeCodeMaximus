# UC.LS.01 — Line spacing

Related requirements: FR.9.12 (see `docs/requirements/`). Test cases: [TC.LS.01](/docs/testcases/TC.LS.01-Line-Spacing.md).

## UC.LS.01 — Tighten or loosen the text lines of the session output and the Focus view
**Covers:** FR.9.12.
1. The user opens Settings › Appearance and finds **Line spacing** below the font sizes (default 1.25).
2. The user types a value between 1.0 and 1.8 and saves.
3. The session output (every row and markdown block) and the Focus card text change their line height at once; the transcript keeps its scroll position and is measured again.
4. A value outside the range or a non-number is refused with a message; the stored value stays.
5. The value survives a restart.
