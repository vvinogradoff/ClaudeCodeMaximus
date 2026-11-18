using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>One preview group of the Previews tab: the editable name, patterns and auto-close switch next to the stored id, and the error found when saving.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class PreviewGroupRowViewModel : ObservableObject
{
	[ObservableProperty]
	private string _name;

	[ObservableProperty]
	private string _patterns;

	[ObservableProperty]
	private bool _autoClose;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string? _error;

	public PreviewGroupRowViewModel(string id, string name, string patterns, bool autoClose)
	{
		Id = id;
		_name = name;
		_patterns = patterns;
		_autoClose = autoClose;
	}

	public PreviewGroupRowViewModel(PreviewGroupModel group) : this(group.Id, group.Name, group.Patterns, group.AutoClose)
	{
	}

	public string Id { get; }

	public bool HasError => !string.IsNullOrEmpty(Error);

	public string NameAutomationName => "Preview group name " + Name;

	public string PatternsAutomationName => "Preview group patterns " + Name;

	public string AutoCloseAutomationName => "Preview group auto-close " + Name;

	public string UpAutomationName => "Move group up " + Name;

	public string DownAutomationName => "Move group down " + Name;

	public string RemoveAutomationName => "Remove group " + Name;

	partial void OnNameChanged(string value)
	{
		OnPropertyChanged(nameof(PatternsAutomationName));
		OnPropertyChanged(nameof(AutoCloseAutomationName));
		OnPropertyChanged(nameof(UpAutomationName));
		OnPropertyChanged(nameof(DownAutomationName));
		OnPropertyChanged(nameof(RemoveAutomationName));
	}

	/// <summary>The group as it would be stored: trimmed.</summary>
	public PreviewGroupModel ToModel() => new() { Id = Id, Name = Name.Trim(), Patterns = Patterns.Trim(), AutoClose = AutoClose };
}
