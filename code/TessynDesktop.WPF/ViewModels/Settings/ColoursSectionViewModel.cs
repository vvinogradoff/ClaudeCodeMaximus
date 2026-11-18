using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Colours tab: every colour of <see cref="ColourCatalog"/> (the application, the transcript page, recent and active sessions, session status, Focus kinds, tree selection)
/// for the theme chosen with the Light/Dark switch, which starts on the current theme. Light and Dark keep their colours separately. A colour changed in the picker (or reset) is
/// <b>pending</b> (FR.9.19): <see cref="HasChanges"/> is true, and Apply or the window's Save stores every pending colour in one update, which applies them everywhere. Pending
/// colours are kept per theme while the switch is used; closing the window drops them. Only a colour that differs from its theme's default is stored. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ColoursSectionViewModel : SettingsSectionViewModel, ISettingsEditor
{
	private readonly IClientSettingsStore _store;
	private readonly Dictionary<bool, Dictionary<string, string>> _pending = new() { [false] = new(StringComparer.Ordinal), [true] = new(StringComparer.Ordinal) };

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EditingLight), nameof(ResetAllText))]
	private bool _editingDark;

	public ColoursSectionViewModel(IClientSettingsStore store, IAppearanceService? appearance = null)
	{
		_store = store;
		_editingDark = appearance?.IsDark ?? false;
		Read();
	}

	public override string Title => Constants.Colours.TabTitle;

	/// <summary>The rows in catalog order, for the edited theme.</summary>
	public List<ColourRowViewModel> Rows { get; private set; } = [];

	public string Hint => Constants.Colours.Hint;

	/// <summary>Two-way for the Light radio button of the switch.</summary>
	public bool EditingLight
	{
		get => !EditingDark;
		set
		{
			if (value)
				EditingDark = false;
		}
	}

	public string ResetAllText => string.Format(Constants.Colours.ResetAllFormat, EditingDark ? Constants.Colours.DarkName : Constants.Colours.LightName);

	/// <summary>True while a colour is pending (picked, or reset, and not applied yet) in either theme.</summary>
	public bool HasChanges => _pending.Values.Any(p => p.Count > 0);

	/// <summary>The window's Save: stores the pending colours, like Apply.</summary>
	public Task<bool> SaveAsync()
	{
		ClearMessages();
		Store();
		return Task.FromResult(true);
	}

	/// <summary>Drops the pending colours and reads the stored ones again.</summary>
	[RelayCommand]
	public Task ReloadAsync()
	{
		ClearMessages();
		foreach (var pending in _pending.Values)
			pending.Clear();

		Read();
		OnPropertyChanged(nameof(HasChanges));
		return Task.CompletedTask;
	}

	/// <summary>Every colour of the edited theme back to its default, pending until Apply or Save; the other theme is left alone.</summary>
	[RelayCommand]
	private void ResetAll()
	{
		foreach (var row in Rows)
			row.Reset();
	}

	/// <summary>
	/// Apply (FR.9.18, FR.9.19): closes the open pickers (a picker keeps its colour as the row's), then stores every pending colour in one update, which re-applies the palette
	/// everywhere (the one path: <c>ColourApplier</c> sets the brushes, the appearance service sends the transcript its <c>appearance</c> message). With nothing pending the update
	/// still raises the change, so Apply is also "make sure it is applied now". Applying the theme that is not active leaves the active brushes as they are.
	/// </summary>
	[RelayCommand]
	private void Apply()
	{
		foreach (var row in Rows)
			row.CommitAndClosePicker();

		Store();
	}

	/// <summary>Writes the pending colours of both themes (a colour equal to its default is removed) in one update, then reads the rows again.</summary>
	private void Store()
	{
		var pending = _pending.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value, StringComparer.Ordinal));
		foreach (var colours in _pending.Values)
			colours.Clear();

		_store.Update<ColourSettings>(s =>
		{
			s.Migrate();
			foreach (var (dark, colours) in pending)
			{
				foreach (var (key, hex) in colours)
				{
					var definition = ColourCatalog.Find(key);
					var isDefault = definition != null && string.Equals(hex, dark ? definition.DarkDefault : definition.LightDefault, StringComparison.OrdinalIgnoreCase);
					if (isDefault)
						s.Of(dark).Remove(key);
					else
						s.Of(dark)[key] = hex;
				}
			}
		});
		Read();
		OnPropertyChanged(nameof(HasChanges));
	}

	partial void OnEditingDarkChanged(bool value) => Read();

	/// <summary>Reads the stored colours of the edited theme into the rows.</summary>
	public void Read()
	{
		var dark = EditingDark;
		var changed = _store.Get<ColourSettings>().For(dark);
		var pending = _pending[dark];
		Rows = ColourCatalog.All
			.Select(d => new ColourRowViewModel(d, dark ? d.DarkDefault : d.LightDefault, changed.TryGetValue(d.Key, out var value) && ColourCatalog.IsValid(value) ? ColourCatalog.Normalize(value) : null, Pending,
				pending.GetValueOrDefault(d.Key)))
			.ToList();
		OnPropertyChanged(nameof(Rows));
	}

	/// <summary>A row's colour became pending (the hex), or went back to what is stored (null).</summary>
	private void Pending(ColourRowViewModel row, string? hex)
	{
		if (hex == null)
			_pending[EditingDark].Remove(row.Key);
		else
			_pending[EditingDark][row.Key] = hex;

		OnPropertyChanged(nameof(HasChanges));
	}
}
