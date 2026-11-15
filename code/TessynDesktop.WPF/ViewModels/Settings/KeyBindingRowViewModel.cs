using CommunityToolkit.Mvvm.ComponentModel;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>One command of the Keyboard shortcuts tab: its title, its default combination and the combination being edited (empty = no key).</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class KeyBindingRowViewModel : ObservableObject
{
	[ObservableProperty]
	private string _text;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasProblem))]
	private string? _problem;

	public KeyBindingRowViewModel(string commandId, string title, string? defaultGesture, string text)
	{
		CommandId = commandId;
		Title = title;
		DefaultGesture = defaultGesture ?? string.Empty;
		_text = text;
	}

	public string CommandId { get; }

	public string Title { get; }

	public string DefaultGesture { get; }

	public string DefaultText => DefaultGesture.Length == 0 ? "none" : DefaultGesture;

	public bool HasProblem => !string.IsNullOrEmpty(Problem);

	public string AutomationName => $"Shortcut for {Title}";
}
