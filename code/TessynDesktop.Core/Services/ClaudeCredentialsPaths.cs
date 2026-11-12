using System;
using System.IO;

namespace TessynDesktop.Services;

/// <summary>Where a Claude profile keeps its OAuth credentials (used for the usage lookup).</summary>
/// <remarks>Created by Claude</remarks>
public static class ClaudeCredentialsPaths
{
	/// <summary>
	/// The credentials file of the profile whose config directory is <paramref name="profileConfigDir"/>;
	/// the system-wide account's file (<c>~/.claude/.credentials.json</c>) when it is null or empty.
	/// </summary>
	public static string Resolve(string? profileConfigDir)
	{
		if (!string.IsNullOrEmpty(profileConfigDir))
			return Path.Combine(profileConfigDir, Constants.Usage.CredentialsFileName);

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return Path.Combine(home, Constants.Usage.DefaultClaudeRelativePath, Constants.Usage.CredentialsFileName);
	}
}
