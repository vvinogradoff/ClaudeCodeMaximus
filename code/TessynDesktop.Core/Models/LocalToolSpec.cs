using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// Resolved native-tool restriction for a claude CLI spawn (FR.12.21), passed only for local
/// (Ollama) turns whose model supports tool calling. Becomes <c>--tools "&lt;names&gt;"</c> verbatim;
/// an empty list still emits <c>--tools ""</c> (zero native tools). MCP tools are a separate,
/// independently-gated axis — a spike against the real CLI confirmed a native-only <c>--tools</c>
/// restriction does not strip MCP server tools, so those are controlled instead via
/// <c>AgentMcpServer</c>'s filtered <c>--mcp-config</c> plus <c>--strict-mcp-config</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record LocalToolSpec(IReadOnlyList<string> NativeToolNames);
