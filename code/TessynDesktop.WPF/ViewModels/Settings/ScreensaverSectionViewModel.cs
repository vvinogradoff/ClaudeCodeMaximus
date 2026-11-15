using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>The Screensaver tab: the images folder, the idle time (0 turns it off) and the seconds per image, stored in the client settings (section <c>screensaver</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ScreensaverSectionViewModel : SettingsSectionViewModel, ISettingsEditor
{
	private readonly IClientSettingsStore _store;
	private readonly IFolderPicker _picker;

	[ObservableProperty]
	private string _directory = string.Empty;

	[ObservableProperty]
	private string _timeoutText = string.Empty;

	[ObservableProperty]
	private string _intervalText = string.Empty;

	public ScreensaverSectionViewModel(IClientSettingsStore store, IFolderPicker picker)
	{
		_store = store;
		_picker = picker;
		Read();
	}

	public override string Title => "Screensaver";

	public bool HasChanges
	{
		get
		{
			try
			{
				var stored = _store.Get<ScreensaverSettings>();
				var edited = Build();
				return edited.Directory != stored.Directory || edited.TimeoutSeconds != stored.TimeoutSeconds || edited.SlideshowIntervalSeconds != stored.SlideshowIntervalSeconds;
			}
			catch (SettingsInputException)
			{
				return true;
			}
		}
	}

	[RelayCommand]
	public async Task BrowseAsync()
	{
		var folder = await _picker.PickAsync(Constants.Settings.ScreensaverFolderTitle);
		if (!string.IsNullOrWhiteSpace(folder))
			Directory = folder;
	}

	[RelayCommand]
	public void ClearDirectory() => Directory = string.Empty;

	public Task<bool> SaveAsync()
	{
		ClearMessages();
		try
		{
			var edited = Build();
			_store.Update<ScreensaverSettings>(s =>
			{
				s.Directory = edited.Directory;
				s.TimeoutSeconds = edited.TimeoutSeconds;
				s.SlideshowIntervalSeconds = edited.SlideshowIntervalSeconds;
			});
			StatusText = Constants.Settings.Saved;
			return Task.FromResult(true);
		}
		catch (SettingsInputException ex)
		{
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, ex.Message);
			return Task.FromResult(false);
		}
	}

	[RelayCommand]
	public Task ReloadAsync()
	{
		ClearMessages();
		Read();
		return Task.CompletedTask;
	}

	/// <summary>Reads the stored values into the editor (also after an import).</summary>
	public void Read()
	{
		var stored = _store.Get<ScreensaverSettings>();
		Directory = stored.Directory;
		TimeoutText = stored.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
		IntervalText = stored.SlideshowIntervalSeconds.ToString(CultureInfo.InvariantCulture);
	}

	private ScreensaverSettings Build()
	{
		if (!int.TryParse(TimeoutText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var timeout) || timeout > Constants.Settings.MaxScreensaverSeconds
			|| !int.TryParse(IntervalText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var interval)
			|| interval < Constants.Settings.MinScreensaverSeconds || interval > Constants.Settings.MaxScreensaverSeconds)
			throw new SettingsInputException(string.Format(Constants.Settings.ScreensaverInvalid, Constants.Settings.MinScreensaverSeconds));

		return new ScreensaverSettings { Directory = Directory.Trim(), TimeoutSeconds = timeout, SlideshowIntervalSeconds = interval };
	}
}
