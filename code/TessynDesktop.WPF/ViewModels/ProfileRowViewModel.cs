using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One profile of the flyout: its name, account and sign-in state, and what may be done to it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileRowViewModel
{
	public ProfileRowViewModel(ProfileDto profile)
	{
		Profile = profile;
	}

	public ProfileDto Profile { get; }

	public string Id => Profile.Id;

	public string DisplayName => Profile.DisplayName;

	/// <summary>The account e-mail of the Default profile, empty for the others (theirs is the display name).</summary>
	public string Email => Profile.Email ?? string.Empty;

	public bool IsSystem => Profile.IsSystem;

	public bool IsDefault => Profile.IsDefault;

	public bool IsLoggedOut => Profile.AuthState == ProfileAuthState.LoggedOut;

	/// <summary>The Default profile can be neither renamed nor removed.</summary>
	public bool CanEdit => !Profile.IsSystem;

	/// <summary>A logged-out stored profile, and the Default profile (whose account is not checked by file), can be signed in again.</summary>
	public bool CanAuthenticate => true;

	public string AuthText => Profile.AuthState switch
	{
		ProfileAuthState.LoggedIn => Constants.Profiles.AuthStateLoggedIn,
		ProfileAuthState.LoggedOut => Constants.Profiles.AuthStateLoggedOut,
		_ => Constants.Profiles.AuthStateUnknown,
	};

	public string AutomationName => DisplayName;
}
