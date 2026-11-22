using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One ChatGPT profile of Settings > Profiles (FR.33.5): its name, plan and sign-in state, and what may be done to it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ChatGptProfileRowViewModel
{
	public ChatGptProfileRowViewModel(CodexProfileDto profile)
	{
		Profile = profile;
	}

	public CodexProfileDto Profile { get; }

	public string Id => Profile.Id;

	/// <summary><c>ChatGPT · &lt;email&gt;</c>.</summary>
	public string DisplayName => Profile.DisplayName;

	public string Email => Profile.Email ?? string.Empty;

	/// <summary>The plan shown next to the name (<c>ChatGPT Plus</c>); empty while unknown.</summary>
	public string PlanText => PlanLabel(Profile.Plan);

	public bool IsCodexDefault => Profile.IsCodexDefault;

	/// <summary>Codex Default is the User's own Codex CLI home: Tessyn never deletes it.</summary>
	public bool CanDelete => !Profile.IsCodexDefault;

	public bool IsSignedIn => Profile.SignedIn && !Profile.AuthError;

	/// <summary>Sign out only makes sense for a profile that is signed in.</summary>
	public bool CanSignOut => Profile.SignedIn;

	/// <summary>The sign-in state, and whether it is the profile the GPT model list comes from.</summary>
	public string StateText
	{
		get
		{
			var state = Profile.AuthError ? Constants.Codex.AuthErrorText : Profile.SignedIn ? Constants.Codex.SignedInText : Constants.Codex.SignedOutText;
			return Profile.IsCatalogProfile ? $"{state} · {Constants.Codex.CatalogProfileText}" : state;
		}
	}

	public string AutomationName => DisplayName;

	/// <summary><c>plus</c> → <c>ChatGPT Plus</c>; empty for an unknown plan.</summary>
	public static string PlanLabel(string? plan) =>
		string.IsNullOrWhiteSpace(plan) ? string.Empty : Constants.Codex.PlanPrefix + char.ToUpperInvariant(plan[0]) + plan[1..];
}
