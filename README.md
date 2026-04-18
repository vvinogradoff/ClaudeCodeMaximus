# Tessyn Desktop

Cross-platform desktop GUI for managing Claude Code sessions, backed by the [Tessyn](https://github.com/sapph1re/tessyn) daemon for session indexing, search, and process management.

## Architecture

- **Frontend**: Avalonia (.NET 9) -- native UI on macOS, Windows, and Linux
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
- **macOS Shortcuts**: Cmd+Delete, Cmd+Left/Right, Option+Left/Right, Option+Delete via NativeTextBox

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Tessyn daemon](https://github.com/sapph1re/tessyn): `npm install -g tessyn`
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code): `npm install -g @anthropic-ai/claude-code`

## Setup

```bash
git clone https://github.com/sapph1re/ClaudeCodeMaximus.git
cd ClaudeCodeMaximus
dotnet build code/ClaudeMaximus.sln
```

Start the daemon:
```bash
tessyn start
```

Run the app:
```bash
dotnet run --project code/ClaudeMaximus/ClaudeMaximus.csproj
```

On first launch, enable daemon mode in `appsettings.json` (located in `~/Library/Application Support/ClaudeMaximus/` on macOS):
```json
{
  "UseTessynDaemon": true
}
```

## macOS App Bundle

Build a standalone `.app` bundle (no .NET SDK required to run):

```bash
./build-mac.sh
```

Installs to `~/Applications/Tessyn Desktop.app`. Launchable from Finder or Spotlight.

## Disclaimer

Tessyn Desktop is an independent local tool. It is not affiliated with, sponsored by, or endorsed by Anthropic. It launches the `claude` CLI process already installed on your machine -- equivalent to a custom terminal or IDE plugin.
