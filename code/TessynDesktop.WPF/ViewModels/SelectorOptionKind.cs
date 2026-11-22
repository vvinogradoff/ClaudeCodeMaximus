namespace TessynDesktop.WPF.ViewModels;

/// <summary>What a line of a command-bar selector is: an option the user can pick, or a line that only explains the list (FR.33.2).</summary>
/// <remarks>Created by Claude</remarks>
public enum SelectorOptionKind
{
	/// <summary>A model, effort or profile that can be picked and sent.</summary>
	Option,

	/// <summary>A group header of the model picker (Claude, GPT (ChatGPT plan), Local); never selectable.</summary>
	Header,

	/// <summary>A disabled explanation, such as the GPT sign-in hint; never selectable.</summary>
	Hint,
}
