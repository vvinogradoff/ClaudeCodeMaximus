using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The colour picker's state: hue, saturation, value and alpha (the square and the two sliders edit them), the palette to pick from, and the resulting hex. There is no hex text
/// box. <see cref="Picked"/> is raised on every change a person makes (so the application shows it live), not when the picker is given a colour. Contains no WPF types.
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

	/// <summary>Raised with the new hex when a person changes the colour (square, sliders, palette).</summary>
	public event EventHandler<string>? Picked;

	/// <summary>Takes a colour without raising <see cref="Picked"/> (the picker is opened on a row, or the row changed from elsewhere).</summary>
	public void Show(string hex)
	{
		Take(Hsv.FromHex(ColourCatalog.IsValid(hex) ? hex : "#000000"));
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

	/// <summary>A palette entry was chosen: a final choice, so the host may close the picker (the square and the sliders are for fine tuning and stay open).</summary>
	public event EventHandler? PalettePicked;

	partial void OnHueChanged(double value) => Raise();

	partial void OnSaturationChanged(double value) => Raise();

	partial void OnValueChanged(double value) => Raise();

	partial void OnAlphaChanged(double value) => Raise();

	private Hsv ToHsv() => new(Hue, Saturation, Value, (byte)Math.Clamp((int)Math.Round(Alpha), 0, 255));

	private void Raise()
	{
		if (!_updating)
			Picked?.Invoke(this, Hex);
	}
}
