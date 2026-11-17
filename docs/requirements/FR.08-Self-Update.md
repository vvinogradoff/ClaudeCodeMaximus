> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.8 — Self-Update on Exit

**FR.8.1 — Source codes location:** The application stores a `SourceCodesLocation` setting (the solution root directory) in `appsettings.json`. On startup, if the setting is empty, the app auto-detects it by walking up the directory tree from its base directory looking for a `*.sln` file. If found, the path is persisted. The setting is also editable in the Settings window.

**FR.8.2 — Build output detection:** On startup, the app checks whether it is running from the project's build output directory (`bin/Debug/net9.0/` under the source location). If so, an internal `IsRunningFromBuildOutput` flag is set and the self-update is disabled for that session. A warning icon (⚠) is displayed in the title bar with a tooltip explaining that the app will not be updated on restart.

**FR.8.3 — Update on exit:** On application exit, if `SourceCodesLocation` is set and the app is NOT running from build output, the app checks `bin/Debug/net9.0/` for a newer `TessynDesktop.dll` (by comparing file timestamps against the running copy). If a newer build is found, a PowerShell script is spawned as a visible console process to copy the updated files into the running directory after the app exits.

**FR.8.4** The copy script retries with progressive backoff delays of 1, 2, 4, 8, 16, 32, and 64 seconds (7 attempts total). If all attempts fail, the script exits silently.

**FR.8.5** If `SourceCodesLocation` is empty and auto-detection fails, the update check is silently skipped.

---
