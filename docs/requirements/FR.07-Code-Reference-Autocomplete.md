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
