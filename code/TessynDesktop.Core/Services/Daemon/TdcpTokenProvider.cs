using System;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Resolves the local TDCP token at startup: the DPAPI file wins; otherwise the legacy <c>TdcpToken</c> of appsettings.json is
/// moved into the file once (so the token does not change for existing clients) and removed from appsettings.json; otherwise a
/// new one is issued. The token never stays in appsettings.json.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class TdcpTokenProvider
{
	private static readonly ILogger _log = Log.ForContext(typeof(TdcpTokenProvider));

	public static string GetOrCreate(ITdcpTokenStore store, IAppSettingsService appSettings)
	{
		var token = store.Load();
		var legacy = appSettings.Settings.TdcpToken;

		if (string.IsNullOrEmpty(token))
		{
			token = string.IsNullOrEmpty(legacy) ? Guid.NewGuid().ToString("N") : legacy;
			try
			{
				store.Save(token);
				_log.Information("TDCP token stored in the protected token file ({Source})", string.IsNullOrEmpty(legacy) ? "new" : "migrated from appsettings.json");
			}
			catch (Exception ex)
			{
				// Degrade instead of failing the agent MCP server startup: the token lives in memory for this run only, and the
				// legacy appsettings copy (if any) is kept because the file does not hold it (review A4).
				_log.Error(ex, "TDCP token could not be saved; using an in-memory token for this run");
				return token;
			}
		}

		// Only after the file really holds a token is the plain-text copy removed.
		if (!string.IsNullOrEmpty(legacy) && store.Load() == token)
		{
			lock (appSettings.SyncRoot)
			{
				appSettings.Settings.TdcpToken = null;
				appSettings.Save();
			}
		}

		return token;
	}
}
