using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>Implements <see cref="IAppearanceService"/> on the client settings store and the Windows theme probe.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppearanceService : IAppearanceService
{
	private readonly IClientSettingsStore _store;
	private readonly ISystemThemeProbe _probe;
	private readonly object _gate = new();
	private AppearanceSettings _last;
	private bool _lastDark;

	public AppearanceService(IClientSettingsStore store, ISystemThemeProbe probe)
	{
		_store = store;
		_probe = probe;
		_last = store.Get<AppearanceSettings>();
		_lastDark = ComputeDark(_last.Theme);
		store.Changed += OnStoreChanged;
		probe.Changed += (_, _) => Recompute();
	}

	public event EventHandler? Changed;

	public ClientTheme Chosen => _store.Get<AppearanceSettings>().Theme;

	public bool IsDark => ComputeDark(Chosen);

	public AppearanceSettings Current => _store.Get<AppearanceSettings>();

	public IReadOnlyDictionary<string, string> PageColours => ColourCatalog.PageVariables(_store.Get<ColourSettings>(), IsDark);

	public void ToggleTheme() => _store.Update<AppearanceSettings>(a => a.Theme = ComputeDark(a.Theme) ? ClientTheme.Light : ClientTheme.Dark);

	public void AdjustFont(FontTarget target, int delta) => _store.Update<AppearanceSettings>(a =>
	{
		switch (target)
		{
			case FontTarget.Assistant: a.AssistantFontSize = Zoom(a.AssistantFontSize, delta); break;
			case FontTarget.AssistantMarkdown: a.AssistantMarkdownFontSize = Zoom(a.AssistantMarkdownFontSize, delta); break;
			case FontTarget.User: a.UserFontSize = Zoom(a.UserFontSize, delta); break;
			case FontTarget.Input: a.InputFontSize = Zoom(a.InputFontSize, delta); break;
		}
	});

	private static double Zoom(double size, int delta) => Math.Clamp(size + delta, Constants.Appearance.ZoomMin, Constants.Appearance.ZoomMax);

	private bool ComputeDark(ClientTheme theme) => theme switch
	{
		ClientTheme.Dark => true,
		ClientTheme.Light => false,
		_ => !_probe.IsLight,
	};

	private void OnStoreChanged(object? sender, ClientSettingsChangedEventArgs e)
	{
		if (e.Section == Constants.SettingsSections.Appearance)
			Recompute();
		else if (e.Section == Constants.SettingsSections.Colours)
			Changed?.Invoke(this, EventArgs.Empty);   // the transcript page takes its colours with the appearance message
	}

	private void Recompute()
	{
		var now = _store.Get<AppearanceSettings>();
		bool changed;
		lock (_gate)
		{
			var dark = ComputeDark(now.Theme);
			changed = dark != _lastDark || now.Theme != _last.Theme || now.AssistantFontSize != _last.AssistantFontSize || now.AssistantMarkdownFontSize != _last.AssistantMarkdownFontSize
				|| now.UserFontSize != _last.UserFontSize || now.InputFontSize != _last.InputFontSize || now.TagFontSize != _last.TagFontSize || now.LineSpacing != _last.LineSpacing;
			_last = now;
			_lastDark = dark;
		}

		if (changed)
			Changed?.Invoke(this, EventArgs.Empty);
	}
}
