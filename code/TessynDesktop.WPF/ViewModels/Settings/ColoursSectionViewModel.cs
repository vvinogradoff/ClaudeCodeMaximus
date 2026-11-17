using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Colours tab: every colour of <see cref="ColourCatalog"/> (recent and active sessions, session status, Focus kinds, tree selection) with its entry. An empty entry
/// means the default (the Avalonia client's); a colour is stored only when it differs from the default of the current theme. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ColoursSectionViewModel : SettingsSectionViewModel, ISettingsEditor
{
	private readonly IClientSettingsStore _store;
	private readonly Func<bool> _isDark;

	public ColoursSectionViewModel(IClientSettingsStore store, IAppearanceService? appearance = null)
	{
		_store = store;
		_isDark = () => appearance?.IsDark ?? false;
		Read();
	}

	public override string Title => Constants.Colours.TabTitle;

	/// <summary>The rows in catalog order.</summary>
	public List<ColourRowViewModel> Rows { get; private set; } = [];

	public string Hint => Constants.Colours.Hint;

	public bool HasChanges
	{
		get
		{
			var stored = _store.Get<ColourSettings>().Overrides;
			foreach (var row in Rows)
			{
				if (!row.IsValid)
					return true;

				var edited = row.IsOverridden ? ColourCatalog.Normalize(row.Text) : null;
				var current = stored.TryGetValue(row.Key, out var value) && ColourCatalog.IsValid(value) ? ColourCatalog.Normalize(value) : null;
				if (edited != current)
					return true;
			}

			return false;
		}
	}

	public Task<bool> SaveAsync()
	{
		ClearMessages();
		var invalid = Rows.FirstOrDefault(r => !r.IsValid);
		if (invalid != null)
		{
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, string.Format(Constants.Colours.InvalidFormat, invalid.Label, invalid.Text));
			return Task.FromResult(false);
		}

		var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var row in Rows.Where(r => r.IsOverridden))
		{
			var hex = ColourCatalog.Normalize(row.Text);
			if (hex != row.DefaultHex)
				overrides[row.Key] = hex;
		}

		_store.Update<ColourSettings>(s => s.Overrides = overrides);
		StatusText = Constants.Settings.Saved;
		return Task.FromResult(true);
	}

	[RelayCommand]
	public Task ReloadAsync()
	{
		ClearMessages();
		Read();
		return Task.CompletedTask;
	}

	/// <summary>Every colour back to its default (applied with Save).</summary>
	[RelayCommand]
	private void ResetAll()
	{
		foreach (var row in Rows)
			row.Text = string.Empty;
	}

	/// <summary>Reads the stored colours into the rows.</summary>
	public void Read()
	{
		var stored = _store.Get<ColourSettings>().Overrides;
		var dark = _isDark();
		Rows = ColourCatalog.All
			.Select(d => new ColourRowViewModel(d, dark ? d.DarkDefault : d.LightDefault, stored.TryGetValue(d.Key, out var value) && ColourCatalog.IsValid(value) ? ColourCatalog.Normalize(value) : string.Empty))
			.ToList();
		OnPropertyChanged(nameof(Rows));
	}
}
