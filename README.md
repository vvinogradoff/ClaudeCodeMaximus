# Tessyn Desktop

Windows desktop GUI for managing Claude Code sessions, backed by the [Tessyn](https://github.com/sapph1re/tessyn) daemon for session indexing, search, and process management.

## Architecture

- **Frontend**: TessynDesktop.WPF (WPF, .NET 10) -- the only desktop client; `code/TessynDesktop` is the headless daemon host it launches (`TessynDesktop.exe --background`)
- **Backend**: Tessyn daemon -- indexes Claude Code JSONL session files into SQLite with FTS5 full-text search, manages Claude CLI processes, persists session metadata
- **Code Analysis**: Roslyn-based C# symbol indexing for autocomplete

## Features

- **Session Tree**: Hierarchical organization with directories, groups, and sessions
- **Conversation Display**: Markdown rendering with syntax highlighting and cross-block text selection
- **Full-Text Search**: Instant search across all sessions via daemon FTS5 engine
- **Session Import**: Import from Claude Code's JSONL files with global cross-project search
- **Code Autocomplete**: C# symbol and file path suggestions (trigger with `#` and `##`)
- **Instruction Toggles**: Per-session auto-commit, auto-document, auto-compact, new-branch
- **Themes**: Dark and light with customizable colors
- **Daemon Status**: Live connection and indexing state in the title bar

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Tessyn daemon](https://github.com/sapph1re/tessyn): `npm install -g tessyn`
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code): `npm install -g @anthropic-ai/claude-code`

## Setup

```bash
git clone https://github.com/sapph1re/Tessyn.git
cd Tessyn
dotnet build code/TessynDesktop.sln
```

Start the daemon:
```bash
tessyn start
```

Run the app (builds WPF and the host, copies them to `[Publish-WPF]` and starts the client from there):
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.un-wpf.ps1
```

The WPF client starts the daemon host itself and updates itself from newer builds under `SourceCodesLocation`.

## Disclaimer

Tessyn Desktop is an independent local tool. It is not affiliated with, sponsored by, or endorsed by Anthropic. It launches the `claude` CLI process already installed on your machine -- equivalent to a custom terminal or IDE plugin.
