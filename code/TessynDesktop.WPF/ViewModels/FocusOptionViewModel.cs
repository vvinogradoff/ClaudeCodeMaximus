using CommunityToolkit.Mvvm.Input;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One answer option of a Focus item; clicking it puts the option text into the card's reply box (FR.23.14).</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FocusOptionViewModel
{
	private readonly Action<string> _pick;

	public FocusOptionViewModel(string text, Action<string> pick)
	{
		Text = text;
		_pick = pick;
	}

	public string Text { get; }

	[RelayCommand]
	private void Pick() => _pick(Text);
}
