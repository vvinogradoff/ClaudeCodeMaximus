using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Profile service without any claude process: a fixed Default account email and config dirs under the temp root.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeProfileService(string root) : IClaudeProfileService
{
	public const string DefaultEmail = "e2e@example.test";

	public string ProfilesRootDirectory { get; } = Path.Combine(root, "profiles");

	public Task<string?> GetAccountEmailAsync(string claudePath, string? configDir) => Task.FromResult<string?>(DefaultEmail);

	public Task LaunchAuthLoginAsync(string claudePath, string? configDir) => Task.CompletedTask;

	public string? GetConfigDirForProfile(int profileIndex, IReadOnlyList<ClaudeProfileModel> profiles)
	{
		if (profileIndex < 1 || profileIndex > profiles.Count)
			return null;

		return Path.Combine(ProfilesRootDirectory, profiles[profileIndex - 1].ProfileId);
	}

	public void MarkProfileDirectoryRemoved(string profileId)
	{
	}
}
