using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The colour picker's state: hue, saturation, value and alpha (the square and the two sliders edit them), the palette to pick from, and the resulting hex. There is no hex text
/// box. Dragging only changes this picker's own (new) colour: <see cref="Picked"/> is raised for the preview, nothing is stored or applied (FR.9.19); the row keeps the colour as
/// pending when the picker closes (<see cref="Accepted"/>) or drops it (<see cref="Cancelled"/>). The picker also lists the CSS named colours and the colour's harmonies, which are
/// worked out in HSV by rotating the hue. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ColourPickerViewModel : ObservableObject
{
	/// <summary>Neutrals and the usual hues: a grid of quick choices, 12 per row.</summary>
	public static IReadOnlyList<string> Palette { get; } =
	[
		"#FFFFFF", "#F3F3F3", "#E0E0E0", "#C8C8C8", "#ADADAD", "#8C8C8C", "#616161", "#404040", "#2B2B2B", "#202020", "#1A1A1A", "#000000",
		"#FDECEA", "#F8B4B4", "#E57373", "#D13438", "#B71C1C", "#8B0000", "#FFF4CE", "#FFE082", "#DAA520", "#D8803A", "#C9722F", "#7A4A12",
		"#E8F5E9", "#A5D6A7", "#90EE90", "#4CAF50", "#008000", "#006400", "#E0F2F1", "#80CBC4", "#26A69A", "#00897B", "#00695C", "#004D40",
		"#E3F2FD", "#CDE6F7", "#90CAF9", "#1E90FF", "#0078D4", "#264F78", "#EDE7F6", "#B39DDB", "#7E57C2", "#5E35B1", "#4527A0", "#311B92",
	];

	private bool _updating;

	/// <summary>The colour the picker was given (the row's colour), shown next to the new one so the change can be compared.</summary>
	[ObservableProperty]
	private string _oldHex = "#000000";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(NamedColours))]
	private string _namedFilter = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Hex), nameof(HueHex))]
	private double _hue;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Hex))]
	private double _saturation;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Hex))]
	private double _value;

	/// <summary>Opacity, 0 (transparent) to 255.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Hex))]
	private double _alpha = 255;

	public ColourPickerViewModel(string hex = "#000000")
	{
		Show(hex);
	}

	/// <summary>The colour as <c>#RRGGBB</c> or, when not opaque, <c>#AARRGGBB</c>.</summary>
	public string Hex => ToHsv().ToHex();

	/// <summary>The fully saturated colour of the current hue: the background of the square.</summary>
	public string HueHex => new Hsv(Hue, 1, 1).ToHex();

	/// <summary>The named colours that match the filter box (every one when it is empty).</summary>
	public IReadOnlyList<NamedColour> NamedColours => State.NamedColours.Filter(NamedFilter);

	/// <summary>The name(s) of the new colour when it equals a named colour (the opacity is ignored); empty otherwise.</summary>
	public string ColourName => State.NamedColours.NameOf(Hex) ?? string.Empty;

	/// <summary>True when <see cref="ColourName"/> has a name.</summary>
	public bool HasColourName => ColourName.Length > 0;

	/// <summary>The hue + 180° colour, keeping saturation, brightness and opacity.</summary>
	public IReadOnlyList<string> Complementary => Harmony(180);

	/// <summary>The hue − 30° and + 30° colours.</summary>
	public IReadOnlyList<string> Analogous => Harmony(-30, 30);

	/// <summary>The hue + 120° and + 240° colours.</summary>
	public IReadOnlyList<string> Triadic => Harmony(120, 240);

	/// <summary>Raised with the new hex when a person changes the colour (square, sliders, palette, a name, a harmony): a preview only, nothing is stored.</summary>
	public event EventHandler<string>? Picked;

	/// <summary>Takes a colour without raising <see cref="Picked"/> (the picker is opened on a row, or the row changed from elsewhere).</summary>
	public void Show(string hex)
	{
		var valid = ColourCatalog.IsValid(hex) ? ColourCatalog.Normalize(hex) : "#000000";
		OldHex = valid;
		Take(Hsv.FromHex(valid));
	}

	private void Take(Hsv value)
	{
		_updating = true;
		try
		{
			// A grey has no hue of its own: keep the one the person had, so dragging the square back to colour does not jump to red.
			if (value.S > 1e-6 && value.V > 1e-6)
				Hue = value.H;

			Saturation = value.S;
			Value = value.V;
			Alpha = value.A;
		}
		finally
		{
			_updating = false;
		}
	}

	/// <summary>The square: saturation (left to right) and value (bottom to top), both 0-1.</summary>
	public void PickSquare(double saturation, double value)
	{
		Saturation = Math.Clamp(saturation, 0, 1);
		Value = Math.Clamp(value, 0, 1);
	}

	[RelayCommand]
	private void PickPalette(string? hex)
	{
		if (!ColourCatalog.IsValid(hex))
			return;

		var colour = Hsv.FromHex(hex!);
		Take(colour.A == 255 ? colour with { A = (byte)Math.Clamp((int)Math.Round(Alpha), 0, 255) } : colour);   // an opaque palette entry keeps the opacity chosen so far
		Raise();
		PalettePicked?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>A named colour was clicked: it is the new colour, the opacity stays.</summary>
	[RelayCommand]
	private void PickNamed(string? hex)
	{
		if (!ColourCatalog.IsValid(hex))
			return;

		Take(Hsv.FromHex(hex!) with { A = (byte)Math.Clamp((int)Math.Round(Alpha), 0, 255) });
		Raise();
	}

	/// <summary>A harmony swatch was clicked: it is the new colour (it carries its own opacity).</summary>
	[RelayCommand]
	private void PickHarmony(string? hex)
	{
		if (!ColourCatalog.IsValid(hex))
			return;

		Take(Hsv.FromHex(hex!));
		Raise();
	}

	/// <summary>Enter: the picker closes and the row keeps the new colour as pending.</summary>
	public void Accept() => Accepted?.Invoke(this, EventArgs.Empty);

	/// <summary>Esc: the picker closes and the row keeps its previous colour.</summary>
	public void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

	/// <summary>The person pressed Enter in the picker.</summary>
	public event EventHandler? Accepted;

	/// <summary>The person pressed Esc in the picker.</summary>
	public event EventHandler? Cancelled;

	/// <summary>A palette entry was chosen: a final choice, so the host may close the picker (the square and the sliders are for fine tuning and stay open).</summary>
	public event EventHandler? PalettePicked;

	partial void OnHueChanged(double value) => Raise();

	partial void OnSaturationChanged(double value) => Raise();

	partial void OnValueChanged(double value) => Raise();

	partial void OnAlphaChanged(double value) => Raise();

	/// <summary>The colours at the given hue offsets (degrees), with this colour's saturation, brightness and opacity.</summary>
	private List<string> Harmony(params double[] offsets) => offsets.Select(o => (ToHsv() with { H = ((Hue + o) % 360 + 360) % 360 }).ToHex()).ToList();

	private void NotifyDerived()
	{
		OnPropertyChanged(nameof(ColourName));
		OnPropertyChanged(nameof(HasColourName));
		OnPropertyChanged(nameof(Complementary));
		OnPropertyChanged(nameof(Analogous));
		OnPropertyChanged(nameof(Triadic));
	}

	private Hsv ToHsv() => new(Hue, Saturation, Value, (byte)Math.Clamp((int)Math.Round(Alpha), 0, 255));

	private void Raise()
	{
		NotifyDerived();
		if (!_updating)
			Picked?.Invoke(this, Hex);
	}
}
