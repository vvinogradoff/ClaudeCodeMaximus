using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Codex;

/// <summary>
/// Builds the per-thread <c>config</c> of <c>thread/start</c> and <c>thread/resume</c> (FR.33.7, FR.33.9, FR.33.16). Nothing of it is ever
/// written to a file or a command line: it travels only in Tessyn's stdio pipe to its own app-server child.
/// <list type="bullet">
/// <item><c>mcp_servers.cmx</c>: the in-process agent MCP endpoint with the session's own <c>X-CMX-Token</c>, <c>startup_timeout_sec = 20</c>,
/// <c>tool_timeout_sec = 3600</c> and <c>default_tools_approval_mode = "approve"</c>; only when agent tools are enabled and the server listens.</item>
/// <item>The enabled External MCP Server Registry entries (FR.20) in Codex form, with the bearer tokens the merger refreshed (FR.20.6):
/// Streamable HTTP as <c>url</c> plus <c>http_headers</c>, stdio as <c>command</c>, <c>args</c> and <c>env</c>. None is <c>required</c>.</item>
/// <item><c>project_doc_fallback_filenames = ["CLAUDE.md"]</c> and <c>features.apps = false</c>.</item>
/// </list>
/// <see cref="Redact"/> gives a copy safe for a log line.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadConfigBuilder
{
	private static readonly ILogger _log = Log.ForContext<CodexThreadConfigBuilder>();

	private readonly IAppSettingsService _appSettings;
	private readonly Lazy<IAgentMcpServer> _mcpServer;
	private readonly IMcpConfigMerger _merger;

	public CodexThreadConfigBuilder(IAppSettingsService appSettings, Lazy<IAgentMcpServer> mcpServer, IMcpConfigMerger merger)
	{
		_appSettings = appSettings;
		_mcpServer   = mcpServer;
		_merger      = merger;
	}

	/// <summary>The config of the session's thread (a new object on every call).</summary>
	public async Task<JsonObject> BuildAsync(SessionNodeModel node, CancellationToken cancellationToken = default)
	{
		var servers = new JsonObject();

		if (CmxServer(node) is { } cmx)
			servers[Constants.Agent.McpServerName] = cmx;

		var registry = await _merger.BuildRegistryServersAsync(_appSettings.Settings.ExternalMcpServers, cancellationToken).ConfigureAwait(false);
		foreach (var (id, server) in registry)
		{
			if (string.Equals(id, Constants.Agent.McpServerName, StringComparison.OrdinalIgnoreCase))
			{
				_log.Warning("The External MCP Server Registry entry {ServerId} has the name of Tessyn's own server; it is left out of Codex threads", id);
				continue;
			}

			if (ToCodexServer(id, server) is { } codexServer)
				servers[id] = codexServer;
		}

		var config = new JsonObject();
		if (servers.Count > 0)
			config[Constants.Codex.Config.McpServers] = servers;
		config[Constants.Codex.Config.ProjectDocFallbackFilenames] = new JsonArray(Constants.Codex.Config.ProjectDocFallbackClaude);
		// The dotted key is the form the spike verified (item 4): it turns off the ChatGPT connectors' codex_apps server.
		config[Constants.Codex.Config.FeaturesApps] = false;
		return config;
	}

	/// <summary>A deep copy of <paramref name="config"/> with every header and environment value replaced by <c>&lt;redacted&gt;</c>.</summary>
	public JsonObject Redact(JsonObject config)
	{
		var copy = (JsonObject)config.DeepClone();
		if (copy[Constants.Codex.Config.McpServers] is not JsonObject servers)
			return copy;

		foreach (var (_, server) in servers)
		{
			if (server is not JsonObject entry)
				continue;
			foreach (var secretKey in new[] { Constants.Codex.Config.HttpHeaders, Constants.Codex.Config.Env })
			{
				if (entry[secretKey] is JsonObject secrets)
				{
					foreach (var name in secrets.Select(p => p.Key).ToList())
						secrets[name] = Constants.Codex.Config.Redacted;
				}
			}
		}

		return copy;
	}

	/// <summary>
	/// <paramref name="text"/> with every secret of the thread's config (header and env values, and the agent token) replaced by <c>&lt;redacted&gt;</c>.
	/// For error texts before they are logged or written: Codex may quote a config value in a <c>failed to load configuration</c> error.
	/// </summary>
	public string RedactText(string text, JsonObject? config, string? agentToken)
	{
		if (string.IsNullOrEmpty(text))
			return text;

		var secrets = new List<string>();
		if (!string.IsNullOrEmpty(agentToken))
			secrets.Add(agentToken);
		if (config?[Constants.Codex.Config.McpServers] is JsonObject servers)
		{
			foreach (var (_, server) in servers)
			{
				foreach (var key in new[] { Constants.Codex.Config.HttpHeaders, Constants.Codex.Config.Env })
				{
					if (server?[key] is not JsonObject values)
						continue;
					foreach (var (_, value) in values)
					{
						if (value is JsonValue v && v.TryGetValue<string>(out var secret) && !string.IsNullOrEmpty(secret))
							secrets.Add(secret);
					}
				}
			}
		}

		foreach (var secret in secrets.Distinct().OrderByDescending(s => s.Length))
			text = text.Replace(secret, Constants.Codex.Config.Redacted, StringComparison.Ordinal);
		return text;
	}

	/// <summary>The cmx entry, under the same preconditions as the Claude <c>--mcp-config</c> (a listener and a node identity, FR.14.3).</summary>
	private JsonObject? CmxServer(SessionNodeModel node)
	{
		if (!_appSettings.Settings.AgentToolsEnabled || string.IsNullOrEmpty(node.NodeId) || string.IsNullOrEmpty(node.AgentToken))
			return null;

		var port = _mcpServer.Value.Port;
		if (port == 0)
		{
			_log.Warning("The agent MCP server is not listening; Codex session {NodeId} runs without the cmx tools", node.NodeId);
			return null;
		}

		return new JsonObject
		{
			[Constants.Codex.Config.Url]                      = $"http://127.0.0.1:{port}{Constants.Agent.McpEndpointPath}",
			[Constants.Codex.Config.HttpHeaders]              = new JsonObject { [Constants.Agent.TokenHeader] = node.AgentToken },
			[Constants.Codex.Config.StartupTimeoutSec]        = Constants.Codex.Config.CmxStartupTimeoutSec,
			[Constants.Codex.Config.ToolTimeoutSec]           = Constants.Codex.Config.CmxToolTimeoutSec,
			[Constants.Codex.Config.DefaultToolsApprovalMode] = Constants.Codex.Config.ApprovalModeApprove,
		};
	}

	/// <summary>
	/// A merged registry entry (the Claude <c>mcpServers</c> shape: <c>type</c>, <c>url</c>, <c>headers</c> or <c>command</c>, <c>args</c>,
	/// <c>env</c>) in Codex form. The merger already left out OAuth entries without a valid token.
	/// </summary>
	private JsonObject? ToCodexServer(string id, object server)
	{
		if (JsonSerializer.SerializeToNode(server) is not JsonObject source)
			return null;

		var codex = new JsonObject();
		var type  = source[Constants.Codex.Config.RegistryType]?.GetValue<string>();
		if (string.Equals(type, Constants.Codex.Config.RegistryTypeStdio, StringComparison.OrdinalIgnoreCase))
		{
			var command = source[Constants.Codex.Config.Command]?.GetValue<string>();
			if (string.IsNullOrWhiteSpace(command))
			{
				_log.Warning("The stdio MCP server {ServerId} has no command; it is left out of Codex threads", id);
				return null;
			}

			codex[Constants.Codex.Config.Command] = command;
			codex[Constants.Codex.Config.Args]    = source[Constants.Codex.Config.Args]?.DeepClone() ?? new JsonArray();
			if (source[Constants.Codex.Config.RegistryEnv] is JsonObject { Count: > 0 } env)
				codex[Constants.Codex.Config.Env] = env.DeepClone();
		}
		else
		{
			var url = source[Constants.Codex.Config.Url]?.GetValue<string>();
			if (string.IsNullOrWhiteSpace(url))
			{
				_log.Warning("The MCP server {ServerId} has no url; it is left out of Codex threads", id);
				return null;
			}

			codex[Constants.Codex.Config.Url] = url;
			if (source[Constants.Codex.Config.RegistryHeaders] is JsonObject { Count: > 0 } headers)
				codex[Constants.Codex.Config.HttpHeaders] = headers.DeepClone();
		}

		codex[Constants.Codex.Config.DefaultToolsApprovalMode] = Constants.Codex.Config.ApprovalModeApprove;
		return codex;
	}
}
