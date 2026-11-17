> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.3 — Session Files

**FR.3.1** Each session is backed by a single plain-text file. Session files are stored in a configurable Session Files Root directory (see FR.4).

**FR.3.2 — Session file naming:** Files are named using the pattern:
```
YYYY-MM-dd-HHmm-{6_random_lowercase_alpha}.txt
```
Example: `2026-03-12-1430-xkqbzf.txt`

The name encodes creation time and a random suffix to avoid collisions. It carries no user-visible semantic meaning; the human-readable session name is stored separately in `appsettings.json`.

**FR.3.3 — Session file format:** Newline-delimited entries. Each entry begins with a single header line followed by the message body:
```
[YYYY-MM-ddTHH:mm:ssZ] ROLE
<message body lines>

```
Where `ROLE` is one of: `USER`, `ASSISTANT`, `SYSTEM`.

**FR.3.4 — Compaction separator:** When Claude Code emits a compaction event, append a separator entry:
```
[YYYY-MM-ddTHH:mm:ssZ] COMPACTION
```
Subsequent messages continue after the separator. The full pre-compaction history above the separator is always preserved and displayed.

**FR.3.5** Each append to a session file is flushed to disk immediately (no buffered writes).

**FR.3.6** Session files are intentionally human- and AI-readable without special tooling, enabling Claude Code and other tools to ingest them directly.

---
