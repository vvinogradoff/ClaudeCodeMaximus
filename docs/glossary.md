# ClaudeMaximus — Glossary

## Domain Terms

| Term | Definition |
|------|-----------|
| **Session** | A single continuous conversation with Claude Code. Represented as a Session node in the tree. Backed by one Session File on disk; its human-readable name is stored in `appsettings.json`. |
| **Session File** | A plain-text file storing the full message history of one Session. Named `YYYY-MM-dd-HHmm-{6_random_lowercase_alpha}.txt`. Located under the Session Files Root. |
| **Session Files Root** | The base directory where all Session Files are stored. Configurable in the Settings window. Default: `<AppData>/ClaudeMaximus/sessions/`. |
| **Directory Node** | A top-level tree node backed by a real filesystem directory. Its display name is auto-derived from the path relative to the nearest `.git` root. Not renameable. Defines the working directory for all Sessions nested beneath it. |
| **Group Node** | A virtual intermediate tree node that exists only in `appsettings.json`. Has a user-defined name, no session, and no working directory of its own. Used to organise sessions within a Directory node. Renameable. |
| **Session Node** | A terminal tree node representing one Session. Its name is stored in `appsettings.json` bound to the Session File path. Renameable. |
| **Working Directory** | The filesystem path of the Directory Node that owns a Session, passed as the working directory when launching the `claude` process for that Session. |
| **Compaction** | An event emitted by Claude Code when it compresses earlier conversation context. ClaudeMaximus appends a `[COMPACTION]` separator entry to the Session File; all pre-compaction history above the separator is preserved and remains visible in the UI. |
| **appsettings.json** | The single JSON configuration file for the application. Persists the full tree structure (Directory, Group, and Session nodes with their metadata), Settings values, and window state. Written atomically on every save. |
| **stream-json** | The Claude Code CLI output format flag (`--output-format stream-json`) that causes `claude` to emit newline-delimited JSON events, which ClaudeMaximus parses to render structured conversation output. |
| **git root** | The nearest ancestor directory (at or above the working directory) containing a `.git` folder. Used to compute the auto-derived display name of a Directory node. |
| **Auto-derived label** | The display name of a Directory node, computed as the path segment from the git root directory name downward. Never overridden by the user. |
| **Session Files Root** | User-configurable base directory where all Session Files are written. Stored in `appsettings.json`. |
