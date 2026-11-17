using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// DPAPI-protected (CurrentUser scope) per-server OAuth token cache (FR.20.5). Each server's token
/// record is serialized to JSON, encrypted, and written to its own file — never stored in
/// appsettings.json, so a leaked or shared appsettings.json backup does not leak live tokens.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class McpTokenStore : IMcpTokenStore
{
	private static readonly ILogger _log = Log.ForContext<McpTokenStore>();
	private static readonly JsonSerializerOptions SerializerOptions = new();

	private readonly string _tokensDir;

	public McpTokenStore() : this(GetDefaultTokensDir())
	{
	}

	/// <summary>Constructor for testing — allows injecting a custom base directory.</summary>
	public McpTokenStore(string tokensDir)
	{
		_tokensDir = tokensDir;
		Directory.CreateDirectory(_tokensDir);
	}

	private static string GetDefaultTokensDir()
	{
		return Path.Combine(Constants.DataRoot, Constants.ExternalMcp.TokenStoreFolderName);
	}

	public McpTokenRecord? Load(string serverId)
	{
		if (!IsValidId(serverId))
			return null;

		var path = GetPath(serverId);
		if (!File.Exists(path))
			return null;

		try
		{
			var encrypted = File.ReadAllBytes(path);
			var plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
			return JsonSerializer.Deserialize<McpTokenRecord>(plaintext, SerializerOptions);
		}
		catch (Exception ex)
		{
			// Corrupt file, or DPAPI-decrypted-by-a-different-user-context — treat as "not authorized"
			// rather than throwing, since callers only ever want a token or null.
			_log.Warning(ex, "McpTokenStore: failed to load/decrypt token for server {ServerId}", serverId);
			return null;
		}
	}

	public void Save(string serverId, McpTokenRecord record)
	{
		var plaintext = JsonSerializer.SerializeToUtf8Bytes(record, SerializerOptions);
		var encrypted = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

		var path     = GetPath(serverId);
		var tempPath = path + ".tmp";
		File.WriteAllBytes(tempPath, encrypted);
		File.Move(tempPath, path, overwrite: true);
	}

	public void Delete(string serverId)
	{
		if (!IsValidId(serverId))
			return;

		var path = GetPath(serverId);
		if (File.Exists(path))
			File.Delete(path);
	}

	/// <summary>The id is a file name: only the registry's own charset is accepted (defence in depth; the registry checks it too).</summary>
	private static bool IsValidId(string serverId) =>
		System.Text.RegularExpressions.Regex.IsMatch(serverId, Constants.IntegrationsApi.McpIdPattern);

	private string GetPath(string serverId) =>
		IsValidId(serverId)
			? Path.Combine(_tokensDir, serverId + Constants.ExternalMcp.TokenFileExtension)
			: throw new ArgumentException("An MCP server id is 1 to 64 letters, digits, '-' or '_'.", nameof(serverId));
}
