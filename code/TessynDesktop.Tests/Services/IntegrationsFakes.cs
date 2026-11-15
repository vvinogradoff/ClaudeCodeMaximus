using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Token store in memory that records what was deleted (the DPAPI one needs a user profile and a directory).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class InMemoryMcpTokenStore : IMcpTokenStore
{
	private readonly object _gate = new();

	public Dictionary<string, McpTokenRecord> Records { get; } = [];
	public List<string> Deleted { get; } = [];

	public McpTokenRecord? Load(string serverId)
	{
		lock (_gate) return Records.GetValueOrDefault(serverId);
	}

	public void Save(string serverId, McpTokenRecord record)
	{
		lock (_gate) Records[serverId] = record;
	}

	public void Delete(string serverId)
	{
		lock (_gate)
		{
			Records.Remove(serverId);
			Deleted.Add(serverId);
		}
	}
}

/// <summary>
/// OAuth service stand-in: discovery fills the endpoints that are empty (never one that is set, like the real one) and always sets the completed flag;
/// authorization stores a token for the server and succeeds, unless a script says otherwise.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ScriptedMcpOAuth : IMcpOAuthService
{
	private readonly InMemoryMcpTokenStore _tokens;

	public ScriptedMcpOAuth(InMemoryMcpTokenStore tokens)
	{
		_tokens = tokens;
	}

	public const string AccessToken = "oauth-access-token-aaaa1111bbbb2222cccc3333dddd4444";
	public const string RefreshToken = "oauth-refresh-token-eeee5555ffff6666gggg7777hhhh8888";

	public Func<string, McpOAuthConfig, CancellationToken, Task>? OnDiscover { get; set; }
	public Func<ExternalMcpServerModel, CancellationToken, Task<McpAuthorizeResult>>? OnAuthorize { get; set; }
	public List<string> Discovered { get; } = [];
	public List<string> Authorized { get; } = [];

	public Task<string?> GetValidAccessTokenAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default) =>
		Task.FromResult<string?>(_tokens.Load(server.Id)?.AccessToken);

	public async Task<McpAuthorizeResult> AuthorizeInteractiveAsync(ExternalMcpServerModel server, CancellationToken cancellationToken = default)
	{
		lock (Authorized) Authorized.Add(server.Id);
		if (OnAuthorize != null)
			return await OnAuthorize(server, cancellationToken);

		_tokens.Save(server.Id, new McpTokenRecord { AccessToken = AccessToken, RefreshToken = RefreshToken });
		server.OAuth ??= new McpOAuthConfig();
		server.OAuth.NeedsReauthorization = false;
		server.OAuth.ConnectedIdentity = "Alex Example";
		return new McpAuthorizeResult(true, "Authorized.");
	}

	public async Task DiscoverAsync(string baseUrl, McpOAuthConfig oauth, CancellationToken cancellationToken = default)
	{
		lock (Discovered) Discovered.Add(baseUrl);
		if (OnDiscover != null)
		{
			await OnDiscover(baseUrl, oauth, cancellationToken);
			return;
		}

		if (string.IsNullOrEmpty(oauth.AuthorizationEndpoint))
			oauth.AuthorizationEndpoint = "https://auth.example.com/authorize";
		if (string.IsNullOrEmpty(oauth.TokenEndpoint))
			oauth.TokenEndpoint = "https://auth.example.com/token";
		oauth.RegistrationEndpoint = "https://auth.example.com/register";
		oauth.DiscoveryCompleted = true;
	}
}

/// <summary>Telegram bridge stand-in: it starts only when the setting is on, like the real one, and counts its starts and stops.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class IntegrationTelegramBridge : ITelegramBridgeService
{
	private readonly IAppSettingsService _settings;

	public IntegrationTelegramBridge(IAppSettingsService settings)
	{
		_settings = settings;
	}

	public int Starts { get; private set; }
	public int Stops { get; private set; }
	public bool IsRunning { get; private set; }
	public string? LastError { get; set; }

	public void Start()
	{
		if (!_settings.Settings.TelegramEnabled || IsRunning)
			return;

		Starts++;
		IsRunning = true;
	}

	public void Stop()
	{
		Stops++;
		IsRunning = false;
	}

	public Task EnsureTopicForSessionAsync(SessionNodeModel node) => Task.CompletedTask;

	public Task<bool> NotifyAsync(string nodeId, string message) => Task.FromResult(false);
}

/// <summary>Relay connector stand-in: a settable status, and a signal for every restart (the module restarts it off the request thread).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingRelayConnector : IRelayConnectorService
{
	private int _restarts;

	public RelayStatusModel Status { get; set; } = new(RelayState.Disabled, new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.Zero));
	public int Restarts => Volatile.Read(ref _restarts);
	public SemaphoreSlim RestartSignal { get; } = new(0);

#pragma warning disable CS0067 // part of the interface; the tests set Status directly
	public event Action<RelayStatusModel>? StatusChanged;
#pragma warning restore CS0067

	public void Start() { }
	public void Stop() { }

	public void Restart()
	{
		Interlocked.Increment(ref _restarts);
		RestartSignal.Release();
	}
}

/// <summary>Model service stand-in with a scriptable refresh.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ScriptedModelService : IClaudeModelService
{
	public List<ClaudeModelInfo> Models { get; set; } = [new("claude-opus-5-5", "opus", "Opus 5.5", InputPricePerMillion: 4m, OutputPricePerMillion: 20m), new("claude-sonnet-5-5", "sonnet", "Sonnet 5.5", InputPricePerMillion: 2m, OutputPricePerMillion: 10m)];
	public Func<CancellationToken, Task>? OnRefresh { get; set; }
	public List<ClaudeModelInfo> AfterRefresh { get; set; } = [];
	public int Refreshes { get; private set; }

	public IReadOnlyList<ClaudeModelInfo> GetCachedModels() => Models;

	public Task EnsureModelsLoadedAsync() => Task.CompletedTask;

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		Refreshes++;
		if (OnRefresh != null)
			await OnRefresh(cancellationToken);

		Models = [.. Models, .. AfterRefresh];
		ModelsUpdated?.Invoke(this, EventArgs.Empty);
	}

	public event EventHandler? ModelsUpdated;
}
