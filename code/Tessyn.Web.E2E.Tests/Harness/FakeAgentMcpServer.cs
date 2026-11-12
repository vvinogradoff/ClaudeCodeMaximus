using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>No in-process MCP server: turns get no MCP config.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeAgentMcpServer : IAgentMcpServer
{
	public int Port => 0;

	public void Start()
	{
	}

	public void Stop()
	{
	}

	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken) => Task.FromResult<string?>(null);

	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken, bool includeCmx, IReadOnlyList<string> externalServerIds) => Task.FromResult<string?>(null);
}
