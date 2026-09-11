# RESEARCH.01 — Visual Studio Automation for an MCP Server — Research Summary

*Researched 2026-08-12. Web-search-verified; no implementation performed.*

## Summary

Multiple 2025/2026 open-source projects already wrap Visual Studio's **Roslyn workspace** (either live-in-process via a VSIX, or standalone via `MSBuildWorkspace`) behind an MCP server, so this doesn't need to be built from scratch. Microsoft itself shipped **MCP client** support in VS (GA August 2025) — VS *consumes* MCP servers (Copilot Agent Mode), it does not ship a first-party "VS as MCP server" for code-intel. For rename/find-refs/go-to-def/call-hierarchy, a **standalone Roslyn `MSBuildWorkspace`** process is the most robust core, optionally augmented by a thin VSIX bridge only for things Roslyn-alone can't see (unsaved editor buffers, build/Error List, actually driving the visible IDE).

## Approaches

| Approach | Capabilities for rename/goto-def/find-refs/call-hierarchy/symbol-search/build | Needs VS running? | Effort | Reliability |
|---|---|---|---|---|
| **(a) EnvDTE/DTE2 COM** via `Marshal.GetActiveObject`/ROT | Can drive VS's own Find All References, Rename, symbol nav, Error List, Build through `DTE2`/`CodeModel`; sees unsaved buffers | Yes, must be running | Medium | Fragile — COM apartment/threading issues, version-specific ROT monikers, breaks across VS updates, single-VS-instance ambiguity when multiple ROT entries exist |
| **(b) VSIX hosting/bridging an MCP server in-process** | Full access to VS's live Roslyn `Workspace`, debugger, Error List, build events, unsaved buffers; can expose via named-pipe/HTTP bridge to an out-of-process MCP server | Yes | Medium-High (VSIX packaging/signing, VS SDK surface, must track VS SDK API churn) | Good while VS is open; several shipping examples (below) prove viability |
| **(c) Roslyn `MSBuildWorkspace` standalone** | Rename (`Renamer.RenameSymbolAsync`), find-refs (`SymbolFinder.FindReferencesAsync`), go-to-def, symbol search — all via `Microsoft.CodeAnalysis.Workspaces.MSBuild` + `Microsoft.Build.Locator`; call hierarchy buildable on top of `FindReferencesAsync`; build via `dotnet build`/MSBuild API, not VS's Error List | No | Low-Medium | High — deterministic, scriptable, CI-friendly; known gaps: generated/implicit-usage references can be missed ([dotnet/roslyn #49387](https://github.com/dotnet/roslyn/discussions/49387)), some rename edge cases under `AdhocWorkspace` ([dotnet/roslyn #28726](https://github.com/dotnet/roslyn/issues/28726)), doesn't see unsaved editor buffers, non-MSBuild project types (e.g. some C++/native, web configs) less supported, solution load time on large solutions |
| **(d) Roslyn LSP (`Microsoft.CodeAnalysis.LanguageServer`, the engine behind C# Dev Kit)** | Same semantic quality as (c) but via standard LSP verbs (`textDocument/rename`, `references`, `definition`, `callHierarchy/*`, `workspace/symbol`); now published as a standalone .NET tool/NuGet (`roslyn-language-server`, e.g. [5.9.0 on NuGet](https://www.nuget.org/packages/roslyn-language-server)) | No | Low-Medium (spawn process, speak LSP over stdio) | High — actively maintained by Microsoft, same engine C# Dev Kit ships; adds live-buffer support if you feed it `textDocument/didOpen` with in-memory content, closing (c)'s unsaved-buffer gap |

## Existing projects

- **[sailro/RoslynMcpExtension](https://github.com/sailro/RoslynMcpExtension)** ("RoslynMCP", VS Marketplace as `YaroslavHorokhov.RoslynMcp` / `SebastienLebretonPersonal.rosmcpext`) — VSIX, uses VS's **live** Roslyn workspace + IDE automation (build/debug/breakpoints/locals). Actively documented by the author on DEV Community; requires VS running.
- **[LadislavSopko/mcp-ai-server-visual-studio](https://github.com/LadislavSopko/mcp-ai-server-visual-studio)** ("MCP AI Server for VS") — VSIX + Roslyn, 41 tools (22 stable + 19 preview debugging), HTTP bridge on localhost, supports VS 2022 17.13+ and VS 2026, amd64/arm64. Small commit history (~8 commits) — early-stage.
- **[CodingWithCalvin/VS-MCPServer](https://github.com/CodingWithCalvin/VS-MCPServer)** — VSIX using the **EnvDTE/DTE** API, out-of-process HTTP/SSE server that talks to the VS extension via named-pipe JSON-RPC. Supports VS 2022/2026. Same author also ships a parallel **VS Code** MCP server ([VSC-MCPServer](https://github.com/CodingWithCalvin/VSC-MCPServer)) using VS Code's language APIs.
- **[carquiza/RoslynMCP](https://github.com/carquiza/RoslynMCP)** — **standalone**, no VS process required; wraps `MSBuildWorkspace` directly. Exposes `SearchSymbols`, `FindReferences`, `GetSymbolInfo`, `AnalyzeDependencies`, `AnalyzeCodeComplexity`. 56 stars/10 forks, moderate activity, no rename tool yet — closest existing analog to the "no-VS-required" half of the requested design.
- **Microsoft first-party**: no "VS as MCP server" for code-intel. Instead, VS 2022 17.14+/VS 2026 shipped a full **MCP client** (Agent Mode, GA Aug 2025) — [devblogs announcement](https://devblogs.microsoft.com/visualstudio/mcp-is-now-generally-available-in-visual-studio/) — plus built-in first-party MCP servers for **Azure** ([Azure MCP in VS 2026](https://devblogs.microsoft.com/visualstudio/azure-mcp-server-now-built-in-with-visual-studio-2026-a-new-era-for-agentic-workflows/)) and **NuGet** ([NuGet MCP server docs](https://learn.microsoft.com/en-us/nuget/concepts/nuget-mcp-server)). The reusable code-intel engine Microsoft does publish standalone is the **Roslyn Language Server** package itself (`Microsoft.CodeAnalysis.LanguageServer.*` / `roslyn-language-server` on NuGet), which C# Dev Kit and VS Code's C# extension both consume — that's the component to embed, not a VS-hosted MCP tool.

## Recommendation

Build the core on **standalone Roslyn** — either raw `MSBuildWorkspace` (approach c) or, preferably, spawn the published **Roslyn Language Server** (approach d) and speak LSP `rename`/`references`/`definition`/`callHierarchy`/`workspace/symbol` to it. This gives full power for rename/find-refs/go-to-def/call-hierarchy/symbol-search without any dependency on VS being open, matches the "degrade gracefully" requirement by construction (it's the *only* tier, not a fallback), and reuses an actively-maintained Microsoft component instead of hand-rolling Roslyn API calls.

Layer a **thin, optional VSIX bridge** (pattern used by CodingWithCalvin/VS-MCPServer and sailro/RoslynMcpExtension: named-pipe/HTTP bridge from an in-process extension) purely for the things standalone Roslyn cannot see: unsaved editor buffer contents, the Build/Error List output, and optionally driving visible-UI actions. Detect VS via `Marshal.GetActiveObject`/ROT probing at request time; if absent, silently route to the standalone Roslyn tier. This avoids EnvDTE as the *primary* code-intel path (approach a alone is too fragile/version-brittle for rename/find-refs) while still using it as a thin presence-check and buffer/build bridge when VS happens to be running.

## Sources

- [MCP is Now Generally Available in Visual Studio — Visual Studio Blog](https://devblogs.microsoft.com/visualstudio/mcp-is-now-generally-available-in-visual-studio/)
- [Azure MCP Server Now Built-In with Visual Studio 2026 — Visual Studio Blog](https://devblogs.microsoft.com/visualstudio/azure-mcp-server-now-built-in-with-visual-studio-2026-a-new-era-for-agentic-workflows/)
- [Using the NuGet MCP Server — Microsoft Learn](https://learn.microsoft.com/en-us/nuget/concepts/nuget-mcp-server)
- [mcp servers — Visual Studio docs, Microsoft Learn](https://learn.microsoft.com/visualstudio/ide/mcp-servers)
- [sailro/RoslynMcpExtension (GitHub)](https://github.com/sailro/RoslynMcpExtension)
- [RoslynMCP — Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=YaroslavHorokhov.RoslynMcp)
- [I built a Visual Studio extension where AI programs using real OOP tools — DEV Community](https://dev.to/yaroslav_gorohov_a4e9e516/i-built-a-visual-studio-extension-where-ai-programs-using-real-oop-tools-roslyn-tree-debugger-ui-3ilf)
- [LadislavSopko/mcp-ai-server-visual-studio (GitHub)](https://github.com/LadislavSopko/mcp-ai-server-visual-studio)
- [CodingWithCalvin/VS-MCPServer (GitHub)](https://github.com/CodingWithCalvin/VS-MCPServer)
- [Introducing the Visual Studio MCP Server! — Coding With Calvin](https://www.codingwithcalvin.net/introducing-the-visual-studio-mcp-server/)
- [carquiza/RoslynMCP (GitHub)](https://github.com/carquiza/RoslynMCP)
- [SymbolFinder.FindReferencesAsync missing implicit-declaration reference — dotnet/roslyn #49387](https://github.com/dotnet/roslyn/discussions/49387)
- [Renamer.RenameSymbolAsync not finding/renaming all symbols — dotnet/roslyn #28726](https://github.com/dotnet/roslyn/issues/28726)
- [Using Roslyn to find all references — .net dev life](https://dotnetdevlife.wordpress.com/2018/10/17/using-roslyn-to-find-all-references/)
- [roslyn-language-server.win-x64 5.8.0 — NuGet Gallery](https://www.nuget.org/packages/roslyn-language-server.win-x64/5.8.0-1.26252.1)
- [roslyn-language-server 5.9.0 — NuGet Gallery](https://www.nuget.org/packages/roslyn-language-server)
- [Published package for the roslyn Microsoft.CodeAnalysis.LanguageServer feature — dotnet/roslyn #71474](https://github.com/dotnet/roslyn/issues/71474)
