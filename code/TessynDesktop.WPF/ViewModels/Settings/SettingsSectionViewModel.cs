using CommunityToolkit.Mvvm.ComponentModel;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>One tab of the Settings window: a title, a status line, an error line and an availability switch (a daemon without the capability shows a note instead of the controls).</summary>
/// <remarks>Created by Claude</remarks>
public abstract partial class SettingsSectionViewModel : ObservableObject
{
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasStatus))]
	private string? _statusText;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string? _errorText;

	[ObservableProperty]
	private bool _isAvailable = true;

	[ObservableProperty]
	private string? _unavailableText;

	public abstract string Title { get; }

	public bool HasStatus => !string.IsNullOrEmpty(StatusText);

	public bool HasError => !string.IsNullOrEmpty(ErrorText);

	/// <summary>Marks the section unusable with the reason shown in place of its controls.</summary>
	protected void MakeUnavailable(string capability)
	{
		IsAvailable = false;
		UnavailableText = string.Format(Constants.Settings.Unavailable, capability);
	}

	protected void ClearMessages()
	{
		StatusText = null;
		ErrorText = null;
	}
}
