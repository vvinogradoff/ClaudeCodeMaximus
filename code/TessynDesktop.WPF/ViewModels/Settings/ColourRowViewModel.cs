using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// One colour of the Colours tab, for the theme being edited: the colour it shows (the stored one, else the theme's default, or the one pending on top of it), whether the user
/// changed it, and the picker the swatch opens. A colour from the picker is only <b>pending</b> (FR.9.19): the row reports it to the section, which stores it when Apply or Save
/// is pressed; nothing is stored or shown elsewhere while the pointer moves. There is no text entry.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ColourRowViewModel : ObservableObject
{
	private readonly Action<ColourRowViewModel, string?> _commit;
	private bool _cancelled;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsOverridden), nameof(IsPending))]
	private string _hex;

	[ObservableProperty]
	private bool _isPickerOpen;

	/// <param name="changedHex">The stored colour of the theme; null when it has none (the default shows).</param>
	/// <param name="commit">Told the row's colour when it becomes pending (the hex), or null when it is back to what is stored.</param>
	/// <param name="pendingHex">A colour already pending for this row (the tab was rebuilt, for instance by switching the theme).</param>
	public ColourRowViewModel(ColourDefinition definition, string defaultHex, string? changedHex, Action<ColourRowViewModel, string?> commit, string? pendingHex = null)
	{
		Definition = definition;
		DefaultHex = defaultHex;
		StoredHex = changedHex ?? defaultHex;
		_commit = commit;
		_hex = pendingHex ?? StoredHex;
		Picker = new ColourPickerViewModel(_hex);
		Picker.PalettePicked += (_, _) => IsPickerOpen = false;
		Picker.Accepted += (_, _) => IsPickerOpen = false;
		Picker.Cancelled += (_, _) => Cancel();
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

	/// <summary>The colour that is stored (and applied) for the theme: the user's, else the default.</summary>
	public string StoredHex { get; }

	/// <summary>True when the colour differs from the default of the theme (it is, or will be once applied, stored).</summary>
	public bool IsOverridden => !string.Equals(Hex, DefaultHex, StringComparison.OrdinalIgnoreCase);

	/// <summary>True when the colour shown is not the stored one yet: the swatch shows a dot until Apply or Save.</summary>
	public bool IsPending => !string.Equals(Hex, StoredHex, StringComparison.OrdinalIgnoreCase);

	public string AutomationName => "Colour " + Label;

	public string ResetAutomationName => "Reset " + Label;

	public RelayCommand ResetCommand { get; }

	/// <summary>The swatch was clicked: the picker opens.</summary>
	[RelayCommand]
	private void OpenPicker() => IsPickerOpen = true;

	partial void OnIsPickerOpenChanged(bool value)
	{
		if (value)
		{
			_cancelled = false;
			Picker.Show(Hex);   // the picker starts on the row's colour, also after an Esc
		}
		else if (_cancelled)
			Picker.Show(Hex);   // Esc: the edit is dropped
		else
			Set(Picker.Hex);    // closed by a click outside, Enter or a palette entry: the new colour is pending
	}

	/// <summary>Esc in the picker: it closes and the row keeps its previous colour.</summary>
	private void Cancel()
	{
		_cancelled = true;
		if (IsPickerOpen)
			IsPickerOpen = false;
		else
			Picker.Show(Hex);
	}

	/// <summary>Takes a colour (from the picker's close, or a script): it is the row's pending colour. Nothing is stored here.</summary>
	public void Set(string hex)
	{
		if (!ColourCatalog.IsValid(hex))
			return;

		var normal = ColourCatalog.Normalize(hex);
		if (string.Equals(normal, Hex, StringComparison.OrdinalIgnoreCase))
			return;

		Hex = normal;
		_commit(this, IsPending ? normal : null);
	}

	/// <summary>Apply (FR.9.18): closes the open picker, which keeps its colour as the row's.</summary>
	public void CommitAndClosePicker() => IsPickerOpen = false;

	/// <summary>Back to the default of the theme (pending until Apply or Save); the picker follows.</summary>
	public void Reset()
	{
		if (!IsOverridden)
			return;

		Set(DefaultHex);
		Picker.Show(DefaultHex);
	}
}
