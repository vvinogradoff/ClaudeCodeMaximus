using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>One colour of the Colours tab: the text being edited (empty = the default) next to the default of the current theme.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ColourRowViewModel : ObservableObject
{
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsOverridden), nameof(IsValid), nameof(SwatchHex))]
	private string _text;

	public ColourRowViewModel(ColourDefinition definition, string defaultHex, string text)
	{
		Definition = definition;
		DefaultHex = defaultHex;
		_text = text;
		ResetCommand = new RelayCommand(() => Text = string.Empty);
	}

	public ColourDefinition Definition { get; }

	public string Key => Definition.Key;

	public string Label => Definition.Label;

	/// <summary>The group of the row in the tab.</summary>
	public string Group => Definition.Group;

	/// <summary>The default of the current theme, as <c>#RRGGBB</c> / <c>#AARRGGBB</c>.</summary>
	public string DefaultHex { get; }

	public bool IsOverridden => Text.Trim().Length > 0;

	/// <summary>Empty counts as valid (the default applies).</summary>
	public bool IsValid => !IsOverridden || ColourCatalog.IsValid(Text);

	/// <summary>The colour the swatch shows: the entry while it is a colour, else the default.</summary>
	public string SwatchHex => IsOverridden && ColourCatalog.IsValid(Text) ? ColourCatalog.Normalize(Text) : DefaultHex;

	public string AutomationName => "Colour " + Label;

	public string ResetAutomationName => "Reset " + Label;

	public RelayCommand ResetCommand { get; }
}
