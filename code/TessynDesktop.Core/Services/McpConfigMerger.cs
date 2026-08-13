using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class McpConfigMerger : IMcpConfigMerger
{
	private static readonly ILogger _log = Log.ForContext<McpConfigMerger>();

	private readonly IMcpOAuthService _mcpOAuth;

	public McpConfigMerger(IMcpOAuthService mcpOAuth)
	{
		_mcpOAuth = mcpOAuth;
	}

	public async Task<Dictionary<string, object>> BuildRegistryServersAsync(
		IReadOnlyList<ExternalMcpServerModel> entries, CancellationToken cancellationToken = default)
	{
		var servers = new Dictionary<string, object>();

		foreach (var entry in entries)
		{
			if (!entry.Enabled)
				continue;

			if (entry.Transport == McpServerTransport.Stdio)
			{
				servers[entry.Id] = new
				{
					type    = "stdio",
					command = entry.Command,
					args    = entry.Arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
				};
				continue;
			}

			// Http transport: resolve the auth header for this entry, skipping it entirely
			// (rather than sending a stale/invalid one) if that resolution fails (FR.20.6).
			var headers = new Dictionary<string, string>();
			switch (entry.AuthType)
			{
				case McpServerAuthType.StaticHeader:
					if (!string.IsNullOrEmpty(entry.StaticHeaderName))
						headers[entry.StaticHeaderName] = entry.StaticHeaderValue;
					break;

				case McpServerAuthType.OAuth2:
					var token = await _mcpOAuth.GetValidAccessTokenAsync(entry, cancellationToken);
					if (token == null)
					{
						_log.Debug("McpConfigMerger: skipping unauthorized/expired OAuth MCP server {ServerId}", entry.Id);
						continue;
					}
					headers["Authorization"] = $"Bearer {token}";
					break;
			}

			servers[entry.Id] = new { type = "http", url = entry.Url, headers };
		}

		return servers;
	}
}
