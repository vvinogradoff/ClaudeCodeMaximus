> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.7 — Code Reference Autocomplete

The input textbox provides interactive autocomplete for referencing code files and code symbols from the session's working directory codebase.

**FR.7.1 — Background Code Indexer:** A background indexer service scans source code files in the session's working directory and builds an in-memory index. The index is built asynchronously on first access and kept up-to-date via `FileSystemWatcher`. Indexes are shared across sessions that share the same working directory (reference-counted).

**FR.7.2 — File Reference Trigger (`##`):** When the user types `##` followed by a query string (e.g., `##Vis`), the application displays an autocomplete popup with matching source file names. The trigger must be preceded by whitespace or be at the start of a line.

**FR.7.3 — Code Symbol Reference Trigger (`#`):** When the user types `#` (single, not preceded by another `#`) followed by a query string (e.g., `#vari`), the application displays an autocomplete popup with matching code symbols (classes, enums, structs, records, interfaces, methods, properties).

**FR.7.4 — Search Result Ordering:** Both file and symbol search results are ordered in four priority tiers, deduplicated across tiers:
1. Name starts with query (case-sensitive)
2. Name starts with query (case-insensitive, excluding tier 1)
3. Name contains query (case-sensitive, excluding tiers 1-2)
4. Name contains query (case-insensitive, excluding tiers 1-3)

Maximum 15 results displayed.

**FR.7.5 — Symbol Display Format:** Each symbol suggestion displays:
- An icon indicating symbol kind (class, enum, struct, record, interface, method, property)
- The fully qualified type-nested name with the matched portion highlighted (e.g., `ParentType.InnerDTO.**Vari**antName`)
- The namespace in grey parenthesis (e.g., `(Datum.Shared.Types)`)

**FR.7.6 — File Display Format:** Each file suggestion displays the file name with the matched portion highlighted, and the relative path from the working directory as secondary text.

**FR.7.7 — Insertion Behavior:** When the user accepts a suggestion (via Tab or Enter):
- The trigger text (`#query` or `##query`) is removed from the input
- For files: the relative path from the working directory is inserted (e.g., `ViewModels/SessionViewModel.cs`)
- For symbols: the fully qualified name including namespace is inserted (e.g., `TessynDesktop.ViewModels.SessionViewModel`)

**FR.7.8 — Popup Behavior:**
- The popup appears above the input textbox (intellisense-style)
- Up/Down arrow keys navigate suggestions when popup is open
- Tab or Enter accepts the selected suggestion
- Escape dismisses the popup
- Clicking outside dismisses the popup
- Popup dismisses automatically when the trigger pattern is no longer present

**FR.7.9 — Indexed File Types:** The file index for `##` (file search) covers **all files** in the working directory tree. Directories `bin/`, `obj/`, `.git/`, `node_modules/`, `.vs/`, `.idea/` are excluded. Symbol extraction for `#` (code symbol search) is performed only on `*.cs` files.

**FR.7.10 — C# Symbol Parsing:** Code symbols are extracted from `*.cs` files using Roslyn syntax-only parsing (`CSharpSyntaxTree.ParseText`). No full compilation or semantic analysis is performed. Supported symbol kinds: class, enum, struct, record, interface, method, property.

**FR.7.11 — Index Lifecycle:** Each per-directory index is reference-counted. It is created lazily when a session with that working directory is first selected, and disposed when no sessions reference it. File system changes are debounced (300ms) before re-indexing the affected file.

**FR.7.12 — Filesystem Path Autocomplete:** When the user types a Windows drive letter pattern (e.g., `C:\`, `D:\`, `E:\`), the application displays an autocomplete popup listing files and directories at the typed path. As the user continues typing the path, the suggestions update to show matching entries in the current directory level. Accepting a directory suggestion appends `\` so the user can continue drilling down. Accepting a file suggestion inserts the full path. The trigger is detected when the text before the caret contains a drive-letter pattern (`X:\`) preceded by whitespace or at the start of a line. This feature operates independently of the code index — it reads the filesystem directly.

---

## Amendments and additions (batch 2026-11-19)

| Existing | Amended by | Change |
|---|---|---|
| FR.7.3 Code symbol trigger (`#`) | FR.7.13, FR.7.14 | `#` suggests C# code symbols only while **Code references** is ON (FR.7.13). While it is OFF, `#` suggests sessions (FR.7.14). |
| FR.7.1 Background indexer, FR.7.9 Indexed file types, FR.7.10 C# symbol parsing | FR.7.13 | The symbol part of the index is optional: with Code references OFF, new indexes do not parse symbols. The file index is kept. |

**FR.7.13 — Code references setting (decision of the Architect, 2026-11-19).** The daemon setting `CodeReferencesEnabled` (`appsettings.json`, **default false**) switches what the single `#` trigger offers. It is shown in Settings > General as the checkbox **Code references** with a hint (FR.4.8).
- **ON.** `#` behaves exactly as FR.7.3, FR.7.5, FR.7.7 describe: C# code symbols from the symbol index.
- **OFF.** `#` suggests sessions (FR.7.14); no code symbol is offered.
- **Index while OFF.**
  - A code index created while the setting is OFF **does not parse C# symbols** (no Roslyn pass over `*.cs`). Its file index is built and kept as before, because `##` depends on it.
  - An index that already parsed its symbols (created while the setting was ON) keeps them until it is released (5 minutes without a request, FR.7.11). The setting does not drop or rebuild indexes; a new index created after the flip follows the setting at that time.
  - The symbol data are not offered while OFF even if an index still holds them.
- **Unchanged in both states.** `##` file suggestions (FR.7.2, FR.7.6, FR.7.9: all file types, for example images, videos, `.md`, `.env`, `.csv`, `.html`, `.txt`) and drive-path suggestions (FR.7.12) work the same, ON or OFF.
- **Applies at the next suggestion.** The daemon reads the setting on each `autocomplete.suggest` request; no restart of the daemon or of the client is needed. A popup that is already open keeps its items until the next keystroke.
- **Wire.** The field `codeReferencesEnabled` in the `general` settings section (tdcp-spec 10.A12).

**FR.7.14 — Session suggestions for `#` (Code references OFF; decision of the Architect, 2026-11-19).** While Code references is OFF, `#` followed by text suggests Tessyn sessions the user may refer to in the message.
- **Trigger and query (confirmed by the Architect, Focus A2).**
  - The trigger is a single `#` at the start of a line or after whitespace, not `##` (that is FR.7.2). The query runs **from after the `#` to the caret on the same line**, so it **may contain spaces** (`#my ses` searches for `my ses`).
  - An **empty query** (just `#`) gives no suggestions.
  - The popup closes (no suggestions) when no session name matches, on Esc, when the caret leaves the line (a line break between the `#` and the caret), or when the `#` is deleted.
  - Because the query may contain spaces, a `#` earlier on the line keeps the trigger active while the user types words after it, for as long as some session name matches.
- **Matching.** Case-insensitive **substring** of the session name. The match in the name is the highlighted part of the row label.
- **Excluded.**
  - **Worker sessions**: `SupervisorNodeId` is set or `OrchestrationDepth` > 0 (glossary: Worker Session).
  - **The current session** (the one whose composer holds the text).
- **Ranking, in tiers** (a session appears once, in the first tier it fits):
  1. Sessions in the **same group** as the current session. Sessions directly under the Directory Node without a group count as one group (the "loose" group of that Directory Node).
  2. Other sessions of the **same project** (same Directory Node).
  3. Sessions of **all other projects** known to Tessyn, **only if fewer than 10 results** were found in tiers 1 and 2. The tier is not consulted at all when 10 or more were found.
  - **Within a tier:** names that **start with** the query before names that merely **contain** it, then **favourites** (`IsFavorite`), then **tree order**.
  - **At most 15** results in total (`Constants.CodeIndex.MaxSuggestions`, as FR.7.4).
- **Popup row.** Label = the session name with the match in bold; kind = `session`; detail = the project label (Directory Node label) and, when the session is in a group, the group path (for example `Tessyn / Perf / Stage 2`).
- **Insertion.** Accepting (Tab or Enter, FR.7.8) replaces the `#query` trigger with `session "<name>" (<shortid>)`, caret behind it, no trailing space added. `<shortid>` is the **Short Node Id** that `list_sessions` prints (glossary; at least 8 characters, lengthened until no other session shares it), so an agent that reads the message can call `mcp__cmx__read_session` with it.
- **No index.** The suggestions come from the session registry in the daemon. They never wait for a code index, never start one and never show the "Building the code index" state; they are instant for the first request too.
- **Unchanged.** FR.7.8 (popup behaviour) and the Up/Down/Tab/Enter/Esc keys apply to session rows as to the others.
- **Wire.** `autocomplete.suggest` answers `mode: "session"` and items of `kind: "session"` (tdcp-spec 10.A12). The web client has no autocomplete and is unaffected.

---
