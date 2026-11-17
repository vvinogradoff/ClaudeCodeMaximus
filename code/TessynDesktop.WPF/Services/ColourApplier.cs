using System.Windows;
using System.Windows.Media;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Puts the colour palette into the running application as brushes (<c>Colour.&lt;Key&gt;</c> resources, <see cref="ColourCatalog"/>): the defaults of the effective theme with the
/// user's changes on top, now and on every change of the Colours section or the theme. The views use them as dynamic resources, so a change shows at once.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ColourApplier
{
	private readonly IClientSettingsStore _store;
	private readonly IAppearanceService _appearance;
	private readonly IUiDispatcher _ui;

	public ColourApplier(IClientSettingsStore store, IAppearanceService appearance, IUiDispatcher ui)
	{
		_store = store;
		_appearance = appearance;
		_ui = ui;
	}

	/// <summary>Applies the stored colours and keeps them current; call once on the UI thread before the window is shown.</summary>
	public void Start()
	{
		Apply(Application.Current.Resources);
		_store.Changed += (_, e) =>
		{
			if (e.Section == Constants.SettingsSections.Colours)
				_ui.Post(() => Apply(Application.Current.Resources));
		};
		_appearance.Changed += (_, _) => _ui.Post(() => Apply(Application.Current.Resources));
	}

	/// <summary>Sets one frozen brush per colour in <paramref name="resources"/>; a key whose brush already has that colour is left alone.</summary>
	public void Apply(ResourceDictionary resources)
	{
		var overrides = _store.Get<ColourSettings>().Overrides;
		var dark = _appearance.IsDark;
		foreach (var definition in ColourCatalog.All)
		{
			var color = Parse(ColourCatalog.Effective(definition, overrides, dark));
			if (resources[definition.ResourceKey] is SolidColorBrush existing && existing.Color == color)
				continue;

			var brush = new SolidColorBrush(color);
			brush.Freeze();
			resources[definition.ResourceKey] = brush;
		}
	}

	/// <summary>Parses a validated hex colour (<c>#RRGGBB</c> or <c>#AARRGGBB</c>).</summary>
	public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
