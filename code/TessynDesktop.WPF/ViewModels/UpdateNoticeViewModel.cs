using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The small notice "A newer build is ready — it is installed when you close Tessyn" with its "Restart now" link. Hidden until the live update check finds a newer build.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class UpdateNoticeViewModel : ObservableObject
{
	[ObservableProperty]
	private bool _isVisible;

	public string Text => Constants.Switch.UpdateReadyText;

	public string RestartNowText => Constants.Switch.RestartNowText;

	/// <summary>What "Restart now" does (set by the live update service); null = nothing.</summary>
	public Action? RestartHandler { get; set; }

	[RelayCommand]
	private void RestartNow() => RestartHandler?.Invoke();
}
