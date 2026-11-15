namespace TessynDesktop.Models.Remote;

/// <summary>Whether a profile holds credentials (a file check; the account is not asked).</summary>
/// <remarks>Created by Claude</remarks>
public enum ProfileAuthState
{
	/// <summary>The profile has credentials.</summary>
	LoggedIn = 0,

	/// <summary>A stored profile without a credentials file: it needs <c>profile.authenticate</c>.</summary>
	LoggedOut = 1,

	/// <summary>The Default profile, whose account is not checked by file.</summary>
	Unknown = 2,
}
