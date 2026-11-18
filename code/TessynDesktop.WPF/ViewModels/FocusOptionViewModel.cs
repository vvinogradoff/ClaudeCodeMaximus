using CommunityToolkit.Mvvm.Input;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// One answer button of a Focus item: an option, or the recommendation (always the last button, FR.23.26). Clicking it hands <see cref="Reply"/> to the
/// card's reply box (FR.23.14): the option text, or "Accepted your recommendation: ..." for the recommendation.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FocusOptionViewModel
{
	private readonly Action<string> _pick;

	public FocusOptionViewModel(string text, Action<string> pick, bool isRecommended = false)
	{
		Text = text;
		IsRecommended = isRecommended;
		_pick = pick;
	}

	/// <summary>What the button shows: the option, or the recommendation text without any prefix.</summary>
	public string Text { get; }

	/// <summary>True for the recommendation button: outlined in the accent colour, with the tooltip "Recommended".</summary>
	public bool IsRecommended { get; }

	/// <summary>What the reply box gets when the button is clicked.</summary>
	public string Reply => IsRecommended ? Constants.Texts.AcceptedRecommendation + Text : Text;

	public string ToolTip => IsRecommended ? Constants.Texts.RecommendedTooltip : Constants.Texts.OptionTooltip;

	[RelayCommand]
	private void Pick() => _pick(Reply);
}
