using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>The desktop's own usage poller is not running.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeDesktopUsageService : IClaudeUsageService
{
	public ClaudeUsageData? CachedUsage => null;

	public string? ActiveCredentialsPath => null;

	public event EventHandler? UsageUpdated { add { } remove { } }

	public void SetActiveProfile(string? credentialsFilePath)
	{
	}
}
