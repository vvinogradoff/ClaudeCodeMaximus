using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionSearchService : ISessionSearchService
{
	private static readonly ILogger _log = Log.ForContext<SessionSearchService>();

	private readonly IAppSettingsService _appSettings;
	private readonly ISessionFileService _sessionFiles;

	public SessionSearchService(IAppSettingsService appSettings, ISessionFileService sessionFiles)
	{
		_appSettings = appSettings;
		_sessionFiles = sessionFiles;
	}

	public IReadOnlySet<string> FindMatchingSessionFiles(string query)
	{
		var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (string.IsNullOrWhiteSpace(query))
			return matches;

		var root = _appSettings.Settings.SessionFilesRoot;
		if (!Directory.Exists(root))
			return matches;

		foreach (var filePath in Directory.EnumerateFiles(root, "*" + Constants.SessionFileExtension))
		{
			var fileName = Path.GetFileName(filePath);
			try
			{
				// Entries carry message bodies only — header lines (profile="email" etc.) never match.
				foreach (var entry in _sessionFiles.ReadEntries(fileName))
				{
					if (!entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
						continue;
					matches.Add(fileName);
					break;
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_log.Debug("FindMatchingSessionFiles: skipping unreadable {File}: {Error}", fileName, ex.Message);
			}
		}

		return matches;
	}
}
