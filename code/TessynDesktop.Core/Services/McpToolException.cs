using System;

namespace TessynDesktop.Services;

/// <summary>A tool call of <see cref="AgentMcpServer"/> that failed with a message for the calling session (answered as an MCP tool error).</summary>
/// <remarks>Created by Claude. Its own file since <c>create_session</c> lives in a partial file of the server (FR.15.15).</remarks>
internal sealed class McpToolException(string message) : Exception(message);
