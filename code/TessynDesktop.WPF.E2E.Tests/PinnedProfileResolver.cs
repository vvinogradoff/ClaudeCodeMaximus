using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>A test-only profile resolver for the state builder: every session is pinned to one profile, so the tree's session state carries a pinned profile name (the profile text of a row and of the closed Recent button).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class PinnedProfileResolver : IProfileResolver
{
	private readonly ProfileResolution _pin;

	public PinnedProfileResolver(string profileId, string displayName)
	{
		_pin = new ProfileResolution(profileId, displayName, ConfigDir: null, IsPinned: true);
	}

	public ProfileResolution? DescribePin(SessionNodeModel node) => _pin;

	public ProfileResolution? FindById(string profileId, bool checkLogin = false) => profileId == _pin.ProfileId ? _pin : null;

	public ProfileResolution? FindByName(string displayName, bool checkLogin = false) => displayName == _pin.DisplayName ? _pin : null;

	public ProfileResolution Resolve(SessionNodeModel node, string? recordedName, string? perTurnName = null, bool checkLogin = false) => _pin;
}
