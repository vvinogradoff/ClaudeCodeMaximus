using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// One colour of the Colours tab, for the theme being edited: the colour it has now (the user's, else the theme's default), whether the user changed it, and the picker the swatch
/// opens. A change made in the picker goes to the section at once (the application shows it live); there is no text entry.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ColourRowViewModel : ObservableObject
{
	private readonly Action<ColourRowViewModel, string?> _commit;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsOverridden))]
	private string _hex;

	[ObservableProperty]
	private bool _isPickerOpen;

	public ColourRowViewModel(ColourDefinition definition, string defaultHex, string? changedHex, Action<ColourRowViewModel, string?> commit)
	{
		Definition = definition;
		DefaultHex = defaultHex;
		_commit = commit;
		_hex = changedHex ?? defaultHex;
		Picker = new ColourPickerViewModel(_hex);
		Picker.Picked += (_, hex) => Set(hex);
		Picker.PalettePicked += (_, _) => IsPickerOpen = false;
		ResetCommand = new RelayCommand(Reset);
	}

	public ColourDefinition Definition { get; }

	public string Key => Definition.Key;

	public string Label => Definition.Label;

	/// <summary>The group of the row in the tab.</summary>
	public string Group => Definition.Group;

	/// <summary>The default of the theme being edited, as <c>#RRGGBB</c> / <c>#AARRGGBB</c>.</summary>
	public string DefaultHex { get; }

	public ColourPickerViewModel Picker { get; }

	/// <summary>True when the colour differs from the default of the theme (and so is stored).</summary>
	public bool IsOverridden => !string.Equals(Hex, DefaultHex, StringComparison.OrdinalIgnoreCase);

	public string AutomationName => "Colour " + Label;

	public string ResetAutomationName => "Reset " + Label;

	public RelayCommand ResetCommand { get; }

	/// <summary>The swatch was clicked: the picker opens.</summary>
	[RelayCommand]
	private void OpenPicker() => IsPickerOpen = true;

	/// <summary>Takes a colour from the picker (or a script): it is the row's colour and goes to the section.</summary>
	public void Set(string hex)
	{
		if (!ColourCatalog.IsValid(hex))
			return;

		var normal = ColourCatalog.Normalize(hex);
		if (string.Equals(normal, Hex, StringComparison.OrdinalIgnoreCase))
			return;

		Hex = normal;
		_commit(this, IsOverridden ? normal : null);
	}

	/// <summary>Apply (FR.9.18): takes the colour the open picker shows as the row's colour, then closes the picker.</summary>
	public void CommitAndClosePicker()
	{
		if (IsPickerOpen)
			Set(Picker.Hex);

		IsPickerOpen = false;
	}

	/// <summary>Back to the default of the theme.</summary>
	public void Reset()
	{
		if (!IsOverridden)
			return;

		Hex = DefaultHex;
		Picker.Show(DefaultHex);
		_commit(this, null);
	}
}
