using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>One editable row of the GPT model tier table (FR.33.14): a GPT model id that contains <see cref="Match"/> (any case) has <see cref="Tier"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class CodexTierRowViewModel : ObservableObject
{
	[ObservableProperty]
	private string _match;

	[ObservableProperty]
	private int _tier;

	public CodexTierRowViewModel(string match, int tier)
	{
		_match = match;
		_tier = tier;
	}

	public CodexTierRowViewModel(CodexModelTierDto row) : this(row.Match, row.Tier)
	{
	}

	/// <summary>The row as sent (the match trimmed).</summary>
	public CodexModelTierDto ToDto() => new(Match.Trim(), Tier);
}
