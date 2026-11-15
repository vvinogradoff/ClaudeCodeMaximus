using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The single place that maps a session's profile pin (or a per-turn choice) to a config dir (ISSUE.013). Pins are by
/// <see cref="ClaudeProfileModel.ProfileId"/>; an old display-name pin still resolves by name. Pure: it never changes the node.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IProfileResolver
{
	/// <summary>
	/// Resolves the profile of one turn: <paramref name="perTurnName"/> (an explicit choice), else the node's <see cref="SessionNodeModel.ProfileId"/>
	/// pin, else <paramref name="recordedName"/> (the pinned or last recorded display name).
	/// A pinned or explicitly chosen profile that does not resolve is an error; an unpinned session whose last recorded name
	/// matches nothing keeps today's behaviour (Default).
	/// </summary>
	/// <param name="checkLogin">Also report a stored profile without credentials as <see cref="ProfileResolution.IsLoggedOut"/> (a file existence check).</param>
	ProfileResolution Resolve(SessionNodeModel node, string? recordedName, string? perTurnName = null, bool checkLogin = false);

	/// <summary>The profile with this id (<c>"default"</c> or a stored one); null when there is none.</summary>
	ProfileResolution? FindById(string profileId, bool checkLogin = false);

	/// <summary>The stored profile with this display name (case-insensitive), or the Default profile for a <c>[Sys]</c> label; null when there is none.</summary>
	ProfileResolution? FindByName(string displayName, bool checkLogin = false);

	/// <summary>The profile the node is pinned to, for display and switching: its id pin, else its display-name pin; null for a session that is not pinned. An unresolvable pin is returned with its <see cref="ProfileResolution.Error"/>.</summary>
	ProfileResolution? DescribePin(SessionNodeModel node);
}
