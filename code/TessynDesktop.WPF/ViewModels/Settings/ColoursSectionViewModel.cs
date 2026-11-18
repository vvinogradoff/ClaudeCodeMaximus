using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Colours tab: every colour of <see cref="ColourCatalog"/> (the application, the transcript page, recent and active sessions, session status, Focus kinds, tree selection)
/// for the theme chosen with the Light/Dark switch, which starts on the current theme. Light and Dark keep their colours separately. A colour changed in the picker is stored
/// at once, so the application shows it live (there is nothing to save); only a colour that differs from its theme's default is stored. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ColoursSectionViewModel : SettingsSectionViewModel, ISettingsEditor
{
	private readonly IClientSettingsStore _store;

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

	/// <summary>Colours are stored as they change, so there is never anything left for the window's Save.</summary>
	public bool HasChanges => false;

	public Task<bool> SaveAsync() => Task.FromResult(true);

	[RelayCommand]
	public Task ReloadAsync()
	{
		ClearMessages();
		Read();
		return Task.CompletedTask;
	}

	/// <summary>Every colour of the edited theme back to its default; the other theme is left alone.</summary>
	[RelayCommand]
	private void ResetAll()
	{
		var dark = EditingDark;
		_store.Update<ColourSettings>(s =>
		{
			s.Migrate();
			s.Of(dark).Clear();
		});
		Read();
	}

	/// <summary>
	/// Apply (FR.9.18): commits the colour of any open picker, closes the pickers, and re-applies the palette everywhere by raising the Colours section's change again
	/// (the one path: <c>ColourApplier</c> sets the brushes, the appearance service sends the transcript its <c>appearance</c> message). The stored values do not change
	/// by it, so applying the theme that is not active leaves the active brushes as they are.
	/// </summary>
	[RelayCommand]
	private void Apply()
	{
		foreach (var row in Rows)
			row.CommitAndClosePicker();

		_store.Update<ColourSettings>(s => s.Migrate());
	}

	partial void OnEditingDarkChanged(bool value) => Read();

	/// <summary>Reads the stored colours of the edited theme into the rows.</summary>
	public void Read()
	{
		var dark = EditingDark;
		var changed = _store.Get<ColourSettings>().For(dark);
		Rows = ColourCatalog.All
			.Select(d => new ColourRowViewModel(d, dark ? d.DarkDefault : d.LightDefault, changed.TryGetValue(d.Key, out var value) && ColourCatalog.IsValid(value) ? ColourCatalog.Normalize(value) : null, Commit))
			.ToList();
		OnPropertyChanged(nameof(Rows));
	}

	/// <summary>Stores (or, for null, drops) one colour of the edited theme.</summary>
	private void Commit(ColourRowViewModel row, string? hex)
	{
		var dark = EditingDark;
		_store.Update<ColourSettings>(s =>
		{
			s.Migrate();
			if (hex == null)
				s.Of(dark).Remove(row.Key);
			else
				s.Of(dark)[row.Key] = hex;
		});
	}
}
