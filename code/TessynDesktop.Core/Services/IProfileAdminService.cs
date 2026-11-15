using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The lifecycle of the stored Claude profiles (FR.12.7, FR.12.16-12.19), shared by the Avalonia profile dropdown and the remote
/// <c>profile.*</c> methods (package J3): adding a profile, re-authenticating, renaming and removing. Extracted from the Avalonia
/// <c>ProfileController</c>; every change is saved and announces <see cref="ProfilesChanged"/> (and <c>profiles.changed</c> on the event bus).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IProfileAdminService
{
	/// <summary>Raised after the profile list, a display name or an auth state changed. May fire on any thread.</summary>
	event Action? ProfilesChanged;

	/// <summary>Raised when a login finished: the profile id (<c>"default"</c> or a stored id) and whether the account is now logged in. May fire on any thread.</summary>
	event Action<string, bool>? AuthenticationCompleted;

	/// <summary>A copy of the stored profiles, in selector order (the Default profile is not among them).</summary>
	IReadOnlyList<ClaudeProfileModel> GetProfiles();

	/// <summary>The index of the global profile selection (0 = Default, 1..N = a stored profile).</summary>
	int GetSelectedProfileIndex();

	/// <summary>Whether the profile holds credentials: a file check, or <see cref="ProfileAuthState.Unknown"/> for the Default profile; null for an unknown id.</summary>
	ProfileAuthState? GetAuthState(string profileId);

	/// <summary>
	/// Registers a profile without logging in (the remote <c>profile.add</c>); run <see cref="AuthenticateAsync"/> next. The selection is not changed.
	/// </summary>
	ClaudeProfileModel Add(string displayName);

	/// <summary>
	/// The Avalonia "New..." flow: picks a free id, runs the interactive login for it, and when the account answers adds the profile named after its
	/// e-mail and selects it (globally, and on <paramref name="directoryToSelect"/>). Null when the login was cancelled or failed.
	/// </summary>
	Task<ClaudeProfileModel?> AddAfterLoginAsync(DirectoryNodeModel? directoryToSelect);

	/// <summary>Renames a stored profile. Null when there is no such profile.</summary>
	ClaudeProfileModel? Rename(string profileId, string displayName);

	/// <summary>
	/// Removes a stored profile from the list and renames its config directory with a "_rem" suffix. <paramref name="selectedIndex"/> is the
	/// selection (0 = Default) the caller holds; the adjusted selection is stored globally and on <paramref name="directoryToSelect"/>. Null when there is no such profile.
	/// </summary>
	ProfileRemoval? Remove(string profileId, int selectedIndex, DirectoryNodeModel? directoryToSelect);

	/// <summary>Removes a stored profile; the global selection is the caller's selection (the remote <c>profile.remove</c>).</summary>
	ProfileRemoval? Remove(string profileId);

	/// <summary>
	/// Runs the interactive login for the profile (<c>"default"</c> or a stored id) and waits for it. A stored profile is renamed after the account that
	/// logged in. <see cref="ProfileAuthentication.Ran"/> is false when a login for that profile is already running.
	/// </summary>
	/// <exception cref="ProfileAdminException">There is no such profile.</exception>
	Task<ProfileAuthentication> AuthenticateAsync(string profileId);

	/// <summary>
	/// Starts <see cref="AuthenticateAsync"/> in the background (the remote <c>profile.authenticate</c>); false when a login for that profile is already running.
	/// </summary>
	/// <exception cref="ProfileAdminException">There is no such profile.</exception>
	bool TryStartAuthentication(string profileId);
}
