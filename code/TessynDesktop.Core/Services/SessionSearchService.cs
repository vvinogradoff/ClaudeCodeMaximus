using System;
using System.Collections.Generic;
using System.IO;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionSearchService : ISessionSearchService
{
	private readonly IAppSettingsService _appSettings;

	public SessionSearchService(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
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
			var content = File.ReadAllText(filePath);
			if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
				matches.Add(Path.GetFileName(filePath));
		}

		return matches;
	}
}
