using System.Collections.Generic;
using System.Threading.Tasks;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>An agent MCP server that only reports a port (the Codex thread config needs nothing else) and fails if a config file is asked for.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeCmxMcpServer : IAgentMcpServer
{
	public int Port { get; set; } = 51234;

	public int ConfigFileRequests { get; private set; }

	public void Start() { }

	public void Stop() { }

	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken)
	{
		ConfigFileRequests++;
		return Task.FromResult<string?>(null);
	}

	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken, bool includeCmx, IReadOnlyList<string> externalServerIds)
	{
		ConfigFileRequests++;
		return Task.FromResult<string?>(null);
	}
}
