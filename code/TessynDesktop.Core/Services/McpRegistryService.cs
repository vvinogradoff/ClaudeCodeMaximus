using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IMcpRegistryService" />
/// <remarks>
/// <para>
/// Writes work on a copy and apply it only when every value is valid; a failing <c>Save</c> puts the old values back. The list is replaced, not edited, on
/// add and remove, because the turn path enumerates it without a lock. Secrets stay write-only; on top of that a secret is bound to the origin it was given
/// for: changing the server URL (or the OAuth token endpoint) deletes the stored token and the client secret, and a static header value must be sent
/// again, so a loopback caller cannot point a stored credential at another host. A new server never inherits a token file that an earlier server of the
/// same id left behind.
/// </para>
/// <para>A stdio server is a command the Claude process starts, so its command must be a bare name or a fully qualified local path, and every such change is logged.</para>
/// Created by Claude.
/// </remarks>
public sealed partial class McpRegistryService : IMcpRegistryService
{
	private static readonly ILogger _log = Log.ForContext<McpRegistryService>();

	[GeneratedRegex(Constants.IntegrationsApi.McpIdPattern)]
	private static partial Regex IdPattern();

	[GeneratedRegex(Constants.IntegrationsApi.HeaderNamePattern)]
	private static partial Regex HeaderNamePattern();

	private readonly IAppSettingsService _settings;
	private readonly IMcpTokenStore _tokens;
	private readonly IMcpOAuthService _oauth;
	private readonly IRemoteEventBus? _bus;
	private readonly TimeSpan _discoverTimeout;
	private readonly TimeSpan _authorizeTimeout;
	private readonly ConcurrentDictionary<string, byte> _authorizing = new(StringComparer.Ordinal);

	public McpRegistryService(IAppSettingsService settings, IMcpTokenStore tokens, IMcpOAuthService oauth, IRemoteEventBus? bus = null, TimeSpan? discoverTimeout = null, TimeSpan? authorizeTimeout = null)
	{
		_settings        = settings;
		_tokens          = tokens;
		_oauth           = oauth;
		_bus             = bus;
		_discoverTimeout = discoverTimeout ?? Constants.IntegrationsApi.McpDiscoverTimeout;
		_authorizeTimeout = authorizeTimeout ?? Constants.IntegrationsApi.McpAuthorizeTimeout;
	}

	/// <inheritdoc />
	public IReadOnlyList<McpServerDto> List()
	{
		ExternalMcpServerModel[] snapshot;
		lock (_settings.SyncRoot)
			snapshot = [.. _settings.Settings.ExternalMcpServers.Select(Clone)];

		return [.. snapshot.Select(ToDto)];
	}

	/// <inheritdoc />
	public McpServerDto Upsert(McpServerUpsertRequest request)
	{
		var id = request.Id == null ? Guid.NewGuid().ToString("N") : ReadId(request.Id);
		ExternalMcpServerModel saved;

		lock (_settings.SyncRoot)
		{
			var servers  = _settings.Settings.ExternalMcpServers;
			var existing = servers.FirstOrDefault(s => s.Id == id);
			var working  = Build(existing, request, id, out var revokeToken);
			if (existing == null && servers.Count >= Constants.IntegrationsApi.MaxMcpServers)
				throw new TdcpInvalidParamsException($"The registry holds at most {Constants.IntegrationsApi.MaxMcpServers} servers");

			// A new server starts without a token (a file of an earlier server of this id must not carry over); a revoked one loses it. Deleted before the
			// save: if the save fails the user authorizes again, which is safe, whereas a token left behind for a changed origin is not.
			if (existing == null || revokeToken)
				_tokens.Delete(id);

			var before = existing == null ? null : Clone(existing);
			if (existing == null)
				_settings.Settings.ExternalMcpServers = [.. servers, working];
			else
				CopyInto(existing, working);

			try
			{
				_settings.Save();
			}
			catch
			{
				if (existing == null)
					_settings.Settings.ExternalMcpServers = servers;
				else
					CopyInto(existing, before!);
				throw;
			}

			saved = Clone(existing ?? working);
		}

		if (saved.Transport == McpServerTransport.Stdio)
			_log.Warning("MCP stdio server {Id} was saved over the Remote API: command {Command}", saved.Id, saved.Command);

		Changed(id);
		return ToDto(saved);
	}

	/// <inheritdoc />
	public bool Remove(string id)
	{
		lock (_settings.SyncRoot)
		{
			var servers = _settings.Settings.ExternalMcpServers;
			if (servers.All(s => s.Id != id))
				return false;

			_tokens.Delete(id);
			_settings.Settings.ExternalMcpServers = [.. servers.Where(s => s.Id != id)];

			// Directories that included this server in their local tool profile must not include a later server of the same (client-chosen) id.
			var pruned = new System.Collections.Generic.List<(LocalToolProfileModel Profile, System.Collections.Generic.List<string> Before)>();
			foreach (var profile in _settings.Settings.Tree.Select(d => d.LocalToolProfile))
			{
				if (profile == null || !profile.IncludedExternalMcpServerIds.Contains(id))
					continue;

				pruned.Add((profile, profile.IncludedExternalMcpServerIds));
				profile.IncludedExternalMcpServerIds = [.. profile.IncludedExternalMcpServerIds.Where(i => i != id)];
			}

			try
			{
				_settings.Save();
			}
			catch
			{
				_settings.Settings.ExternalMcpServers = servers;
				foreach (var (profile, before) in pruned)
					profile.IncludedExternalMcpServerIds = before;
				throw;
			}
		}

		Changed(id);
		return true;
	}

	/// <inheritdoc />
	public McpServerDto SetEnabled(string id, bool enabled)
	{
		ExternalMcpServerModel saved;
		lock (_settings.SyncRoot)
		{
			var server = _settings.Settings.ExternalMcpServers.FirstOrDefault(s => s.Id == id)
				?? throw new TdcpInvalidParamsException($"There is no MCP server '{id}'.");

			var was = server.Enabled;
			server.Enabled = enabled;
			try
			{
				_settings.Save();
			}
			catch
			{
				server.Enabled = was;
				throw;
			}

			saved = Clone(server);
		}

		Changed(id);
		return ToDto(saved);
	}

	/// <inheritdoc />
	public async Task<McpDiscoverResult> DiscoverAsync(string url, McpDiscoverKnownEndpoints? known, CancellationToken cancellationToken)
	{
		if (!SafeUrl.TryParse(url.Trim(), out _) || url.Trim().Length > Constants.IntegrationsApi.MaxUrlLength)
			throw new TdcpInvalidParamsException("'url' must be an http or https URL without user info");

		var probe = new McpOAuthConfig
		{
			AuthorizationEndpoint = known?.AuthorizationEndpoint == null ? string.Empty : ReadEndpoint("oauth.authorizationEndpoint", known.AuthorizationEndpoint),
			TokenEndpoint         = known?.TokenEndpoint == null ? string.Empty : ReadEndpoint("oauth.tokenEndpoint", known.TokenEndpoint),
			RegistrationEndpoint  = known?.RegistrationEndpoint is { Length: > 0 } registration ? ReadEndpoint("oauth.registrationEndpoint", registration) : null,
		};

		// The service has its own limit, but the daemon's is the one that counts: a stand-in or a hung probe must not hold the request.
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		linked.CancelAfter(_discoverTimeout);
		var probing = _oauth.DiscoverAsync(url.Trim(), probe, linked.Token);
		var timedOut = false;
		try
		{
			await probing.WaitAsync(_discoverTimeout, cancellationToken);
		}
		catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			timedOut = true;
			linked.Cancel();
			_ = probing.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // a late failure of the abandoned probe is not unobserved
		}

		return new McpDiscoverResult(
			new McpDiscoveredConfigDto(
				SafeUrl.ForEndpointDisplay(probe.AuthorizationEndpoint),
				SafeUrl.ForEndpointDisplay(probe.TokenEndpoint),
				probe.RegistrationEndpoint is { Length: > 0 } reg ? SafeUrl.ForEndpointDisplay(reg) : null,
				probe.DiscoveryCompleted),
			timedOut);
	}

	/// <inheritdoc />
	public async Task<McpAuthorizeResultDto> AuthorizeAsync(string id, CancellationToken cancellationToken)
	{
		ExternalMcpServerModel server;
		lock (_settings.SyncRoot)
		{
			server = _settings.Settings.ExternalMcpServers.FirstOrDefault(s => s.Id == id)
				?? throw new TdcpInvalidParamsException($"There is no MCP server '{id}'.");
		}

		if (!_authorizing.TryAdd(id, 0))
			return new McpAuthorizeResultDto(false, "An authorization of this server is already running.");

		try
		{
			// The live model: the flow records what it learns (a registered client id, the discovery flag) on it and saves.
			// The whole flow is capped, not only the browser wait: discovery, client registration and the token exchange run under the shared HTTP client's own
			// (long) timeout. Like DiscoverAsync, this returns even when the flow ignores its token, and the guard is released in the finally below.
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			linked.CancelAfter(_authorizeTimeout);
			var flow = _oauth.AuthorizeInteractiveAsync(server, linked.Token);
			McpAuthorizeResult result;
			try
			{
				result = await flow.WaitAsync(_authorizeTimeout, cancellationToken);
			}
			catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
			{
				linked.Cancel();
				_ = flow.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
				return new McpAuthorizeResultDto(false, "The authorization took too long and was stopped.");
			}

			bool stillRegistered, idTaken;
			lock (_settings.SyncRoot)
			{
				stillRegistered = _settings.Settings.ExternalMcpServers.Any(s => ReferenceEquals(s, server));
				idTaken         = _settings.Settings.ExternalMcpServers.Any(s => s.Id == id);
			}

			if (!stillRegistered)
			{
				// The server was removed while the browser was open: its token goes too, unless a server of the same id was created since (that one's token is not ours to delete).
				if (!idTaken)
					_tokens.Delete(id);
				return new McpAuthorizeResultDto(false, "The server was removed during the authorization.");
			}

			Changed(id);
			return result.Success
				? new McpAuthorizeResultDto(true)
				: new McpAuthorizeResultDto(false, Limit(result.Message));
		}
		finally
		{
			_authorizing.TryRemove(id, out _);
		}
	}

	// ── building a server from a request ─────────────────────────────────────────────────────

	/// <summary>The server as it will be, from the stored one (or a fresh one) and the request; throws before anything is applied when a value is invalid.</summary>
	private static ExternalMcpServerModel Build(ExternalMcpServerModel? existing, McpServerUpsertRequest r, string id, out bool revokeToken)
	{
		var isNew = existing == null;
		var m = isNew ? new ExternalMcpServerModel { Id = id } : Clone(existing!);
		revokeToken = false;

		var oldUrlOrigin   = SafeUrl.Origin(m.Url);
		var oldTokenOrigin = SafeUrl.Origin(m.OAuth?.TokenEndpoint);
		var oldTransport   = m.Transport;
		var oldAuth        = m.AuthType;
		var hadHeader      = !string.IsNullOrEmpty(m.StaticHeaderValue);

		if (r.DisplayName != null)
			m.DisplayName = ReadText("displayName", r.DisplayName, Constants.ProfileAdmin.MaxDisplayNameLength, required: true);
		else if (isNew)
			throw new TdcpInvalidParamsException("'displayName' is required");

		if (r.Enabled is { } enabled)
			m.Enabled = enabled;

		if (r.Transport is { } transport)
			m.Transport = ReadEnum<McpTransportDto, McpServerTransport>("transport", transport);

		if (m.Transport == McpServerTransport.Http)
		{
			if (r.Command != null || r.Arguments != null || r.WorkingDirectory != null)
				throw new TdcpInvalidParamsException("'command', 'arguments' and 'workingDirectory' apply to a stdio server");

			if (!isNew && oldTransport == McpServerTransport.Stdio)
				(m.Command, m.Arguments, m.WorkingDirectory) = (string.Empty, string.Empty, string.Empty);

			if (r.Url != null)
				m.Url = ReadHttpUrl("url", r.Url, Constants.IntegrationsApi.MaxUrlLength);
			if (m.Url.Length == 0)
				throw new TdcpInvalidParamsException("'url' is required for an http server");

			ApplyHttpAuth(m, r, oldAuth, ref revokeToken);
		}
		else
		{
			if (r.Url != null || r.StaticHeaderName != null || r.StaticHeaderValue != null || r.Oauth != null || r.AuthType is not (null or McpAuthTypeDto.None))
				throw new TdcpInvalidParamsException("'url' and the authentication members apply to an http server");

			if (oldTransport == McpServerTransport.Http && !isNew)
			{
				revokeToken = true;
				m.Url = string.Empty;
				m.AuthType = McpServerAuthType.None;
				m.StaticHeaderName = m.StaticHeaderValue = string.Empty;
				m.OAuth = null;
			}

			if (r.Command != null)
				m.Command = ReadCommand(r.Command);
			if (r.Arguments != null)
				m.Arguments = ReadSecretText("arguments", r.Arguments);
			if (r.WorkingDirectory != null)
				m.WorkingDirectory = SettingsPatchReader.AbsolutePath("workingDirectory", r.WorkingDirectory, allowEmpty: true);
			if (m.Command.Length == 0)
				throw new TdcpInvalidParamsException("'command' is required for a stdio server");
		}

		if (!isNew && m.Transport == McpServerTransport.Http)
			BindSecretsToOrigin(m, r, oldUrlOrigin, oldTokenOrigin, oldAuth, hadHeader, ref revokeToken);

		return m;
	}

	private static void ApplyHttpAuth(ExternalMcpServerModel m, McpServerUpsertRequest r, McpServerAuthType oldAuth, ref bool revokeToken)
	{
		if (r.AuthType is { } auth)
			m.AuthType = ReadEnum<McpAuthTypeDto, McpServerAuthType>("authType", auth);

		if (m.AuthType == McpServerAuthType.StaticHeader)
		{
			if (r.StaticHeaderName != null)
				m.StaticHeaderName = ReadHeaderName(r.StaticHeaderName);
			if (r.StaticHeaderValue != null)
				m.StaticHeaderValue = ReadHeaderValue(r.StaticHeaderValue);
			if (m.StaticHeaderName.Length == 0)
				throw new TdcpInvalidParamsException("'staticHeaderName' is required for a static-header server");
		}
		else
		{
			if (r.StaticHeaderName != null || r.StaticHeaderValue != null)
				throw new TdcpInvalidParamsException("'staticHeaderName' and 'staticHeaderValue' apply to a static-header server");

			m.StaticHeaderName = m.StaticHeaderValue = string.Empty; // a header value of an auth type that is no longer used does not stay behind
		}

		if (m.AuthType == McpServerAuthType.OAuth2)
		{
			m.OAuth ??= new McpOAuthConfig();
			if (r.Oauth != null)
				ApplyOAuth(m.OAuth, r.Oauth);
		}
		else
		{
			if (r.Oauth != null)
				throw new TdcpInvalidParamsException("'oauth' applies to an OAuth server");

			if (oldAuth == McpServerAuthType.OAuth2)
				revokeToken = true;
			m.OAuth = null;
		}
	}

	private static void ApplyOAuth(McpOAuthConfig config, McpOAuthPatch p)
	{
		if (p.AuthorizationEndpoint != null)
			config.AuthorizationEndpoint = ReadEndpoint("oauth.authorizationEndpoint", p.AuthorizationEndpoint);
		if (p.TokenEndpoint != null)
			config.TokenEndpoint = ReadEndpoint("oauth.tokenEndpoint", p.TokenEndpoint);
		if (p.RegistrationEndpoint != null)
			config.RegistrationEndpoint = p.RegistrationEndpoint.Length == 0 ? null : ReadEndpoint("oauth.registrationEndpoint", p.RegistrationEndpoint);
		if (p.ClientId != null)
			config.ClientId = ReadText("oauth.clientId", p.ClientId, 512, required: false);
		if (p.ClientSecret != null)
			config.ClientSecret = p.ClientSecret.Length == 0 ? null : ReadSecretText("oauth.clientSecret", p.ClientSecret);
		if (p.Scopes != null)
			config.Scopes = ReadText("oauth.scopes", p.Scopes, Constants.SettingsApi.MaxTextLength, required: false);
	}

	/// <summary>A stored secret is never sent to an origin it was not given for: moving the server drops it, or demands it again.</summary>
	private static void BindSecretsToOrigin(ExternalMcpServerModel m, McpServerUpsertRequest r, string? oldUrlOrigin, string? oldTokenOrigin, McpServerAuthType oldAuth, bool hadHeader, ref bool revokeToken)
	{
		var urlMoved   = oldUrlOrigin != null && SafeUrl.Origin(m.Url) != oldUrlOrigin;
		// null counts as a value: a token endpoint that appears where there was none is a move too (a secret stored before discovery must not follow it)
		var tokenMoved = SafeUrl.Origin(m.OAuth?.TokenEndpoint) != oldTokenOrigin;

		if (urlMoved && oldAuth == McpServerAuthType.StaticHeader && m.AuthType == McpServerAuthType.StaticHeader && hadHeader && r.StaticHeaderValue == null)
			throw new TdcpInvalidParamsException("The URL moves to another host: send 'staticHeaderValue' again, a stored header value is not sent to a new origin");

		if ((urlMoved || tokenMoved) && oldAuth == McpServerAuthType.OAuth2 && m.OAuth != null)
		{
			revokeToken = true;
			if (r.Oauth?.ClientSecret == null)
				m.OAuth.ClientSecret = null;
			m.OAuth.NeedsReauthorization = false;
			m.OAuth.ConnectedIdentity = null;
			if (urlMoved)
				m.OAuth.DiscoveryCompleted = false;
		}
	}

	// ── value rules ──────────────────────────────────────────────────────────────────────────

	private static string ReadId(string id) =>
		IdPattern().IsMatch(id) ? id : throw new TdcpInvalidParamsException($"'id' must be 1 to {Constants.IntegrationsApi.MaxMcpIdLength} letters, digits, '-' or '_'");

	private static string ReadText(string name, string value, int max, bool required)
	{
		var text = value.Trim();
		if (required && text.Length == 0)
			throw new TdcpInvalidParamsException($"'{name}' must not be empty");
		if (text.Length > max || text.Any(char.IsControl))
			throw new TdcpInvalidParamsException($"'{name}' must be at most {max} characters without control characters");

		return text;
	}

	/// <summary>A text that may be a secret: not trimmed (a value is kept as typed), bounded, no control characters (a line break in a header value is header injection).</summary>
	private static string ReadSecretText(string name, string value) =>
		value.Length > Constants.IntegrationsApi.MaxSecretLength || value.Any(char.IsControl)
			? throw new TdcpInvalidParamsException($"'{name}' must be at most {Constants.IntegrationsApi.MaxSecretLength} characters without control characters")
			: value;

	private static string ReadHeaderValue(string value) => ReadSecretText("staticHeaderValue", value);

	private static string ReadHeaderName(string value)
	{
		var name = value.Trim();
		return HeaderNamePattern().IsMatch(name) ? name : throw new TdcpInvalidParamsException("'staticHeaderName' is not a valid header name");
	}

	private static string ReadHttpUrl(string name, string value, int max)
	{
		var text = value.Trim();
		if (text.Length == 0)
			return text;

		return text.Length <= max && SafeUrl.TryParse(text, out _)
			? text
			: throw new TdcpInvalidParamsException($"'{name}' must be an http or https URL without user info");
	}

	private static string ReadEndpoint(string name, string value) => ReadHttpUrl(name, value, Constants.IntegrationsApi.MaxUrlLength);

	/// <summary>A bare command name (found on the PATH) or a fully qualified local path: no relative path, no UNC or device path.</summary>
	private static string ReadCommand(string value)
	{
		var command = ReadText("command", value, Constants.SettingsApi.MaxTextLength, required: true);
		var hasPath = command.IndexOfAny(['\\', '/', ':']) >= 0;
		if (hasPath)
			return SettingsPatchReader.AbsolutePath("command", command, allowEmpty: false);

		return command;
	}

	private static TModel ReadEnum<TWire, TModel>(string name, TWire value)
		where TWire : struct, Enum
		where TModel : struct, Enum =>
		SettingsPatchReader.EnumValue<TWire, TModel>(name, value);

	private static string Limit(string text) =>
		text.Length > Constants.IntegrationsApi.MaxErrorLength ? text[..Constants.IntegrationsApi.MaxErrorLength] : text;

	// ── model and DTO mapping ────────────────────────────────────────────────────────────────

	private McpServerDto ToDto(ExternalMcpServerModel m)
	{
		var http  = m.Transport == McpServerTransport.Http;
		var stdio = !http;
		var hadQuery = false;
		var url   = http ? SafeUrl.ForDisplay(m.Url, out hadQuery) : string.Empty;

		var oauth = http && m.AuthType == McpServerAuthType.OAuth2;
		var needs = oauth && m.OAuth?.NeedsReauthorization == true;
		var token = oauth && !needs && _tokens.Load(m.Id) != null;
		var status = !oauth ? McpServerStatusDto.NotApplicable
			: needs ? McpServerStatusDto.NeedsReauthorization
			: token ? McpServerStatusDto.Connected
			: McpServerStatusDto.NotConnected;

		return new McpServerDto(
			m.Id,
			m.DisplayName,
			m.Enabled,
			(McpTransportDto)(int)m.Transport,
			http ? url : null,
			hadQuery,
			stdio ? m.Command : null,
			stdio && !string.IsNullOrWhiteSpace(m.Arguments),
			stdio && m.WorkingDirectory.Length > 0 ? m.WorkingDirectory : null,
			http ? (McpAuthTypeDto)(int)m.AuthType : McpAuthTypeDto.None,
			http && m.AuthType == McpServerAuthType.StaticHeader && m.StaticHeaderName.Length > 0 ? m.StaticHeaderName : null,
			http && m.AuthType == McpServerAuthType.StaticHeader && m.StaticHeaderValue.Length > 0,
			oauth && m.OAuth != null
				? new McpOAuthDto(
					SafeUrl.ForEndpointDisplay(m.OAuth.AuthorizationEndpoint),
					SafeUrl.ForEndpointDisplay(m.OAuth.TokenEndpoint),
					string.IsNullOrEmpty(m.OAuth.RegistrationEndpoint) ? null : SafeUrl.ForEndpointDisplay(m.OAuth.RegistrationEndpoint),
					m.OAuth.ClientId,
					!string.IsNullOrEmpty(m.OAuth.ClientSecret),
					m.OAuth.Scopes,
					m.OAuth.DiscoveryCompleted)
				: null,
			status == McpServerStatusDto.Connected,
			status,
			oauth && !string.IsNullOrEmpty(m.OAuth?.ConnectedIdentity) ? m.OAuth!.ConnectedIdentity : null);
	}

	private static ExternalMcpServerModel Clone(ExternalMcpServerModel m) => new()
	{
		Id                = m.Id,
		DisplayName       = m.DisplayName,
		Enabled           = m.Enabled,
		Transport         = m.Transport,
		Command           = m.Command,
		Arguments         = m.Arguments,
		WorkingDirectory  = m.WorkingDirectory,
		Url               = m.Url,
		AuthType          = m.AuthType,
		StaticHeaderName  = m.StaticHeaderName,
		StaticHeaderValue = m.StaticHeaderValue,
		OAuth             = m.OAuth == null ? null : new McpOAuthConfig
		{
			AuthorizationEndpoint = m.OAuth.AuthorizationEndpoint,
			TokenEndpoint         = m.OAuth.TokenEndpoint,
			RegistrationEndpoint  = m.OAuth.RegistrationEndpoint,
			ClientId              = m.OAuth.ClientId,
			ClientSecret          = m.OAuth.ClientSecret,
			Scopes                = m.OAuth.Scopes,
			DiscoveryCompleted    = m.OAuth.DiscoveryCompleted,
			NeedsReauthorization  = m.OAuth.NeedsReauthorization,
			ConnectedIdentity     = m.OAuth.ConnectedIdentity,
		},
	};

	/// <summary>Writes every member of <paramref name="source"/> into the live <paramref name="target"/> (the Avalonia rows hold the live object).</summary>
	private static void CopyInto(ExternalMcpServerModel target, ExternalMcpServerModel source)
	{
		// The turn path reads the live object without a lock, field by field. So the secrets go first (cleared), then everything else, then the new secrets:
		// a reader can see a server without its secret, never a new URL with an old secret.
		target.StaticHeaderValue = string.Empty;
		if (target.OAuth != null)
			target.OAuth.ClientSecret = null;   // not the object itself: a reader may hold it between its null check and its use
		target.DisplayName       = source.DisplayName;
		target.Enabled           = source.Enabled;
		target.Transport         = source.Transport;
		target.Command           = source.Command;
		target.Arguments         = source.Arguments;
		target.WorkingDirectory  = source.WorkingDirectory;
		target.Url               = source.Url;
		target.AuthType          = source.AuthType;
		target.StaticHeaderName  = source.StaticHeaderName;
		target.StaticHeaderValue = source.StaticHeaderValue;
		target.OAuth             = source.OAuth;
	}

	private void Changed(string id) => _bus?.Publish(RemoteProtocol.McpApi.ChangedEvent, new McpChangedPayload(id));
}
