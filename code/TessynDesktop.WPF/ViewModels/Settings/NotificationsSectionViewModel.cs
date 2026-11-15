using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>The Notifications tab (FR.21.5, FR.22.5): the desktop toast switch, the completion sound file with a test button, and the throttle.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class NotificationsSectionViewModel : DaemonSectionViewModel<NotificationsSettingsDto, NotificationsSettingsPatch>
{
	private readonly ILogger<NotificationsSectionViewModel> _logger;

	[ObservableProperty]
	private bool _notificationsEnabled;

	[ObservableProperty]
	private string _soundFile = string.Empty;

	[ObservableProperty]
	private string _throttleText = string.Empty;

	[ObservableProperty]
	private string? _soundTestText;

	public NotificationsSectionViewModel(ITessynClient client, ILogger<NotificationsSectionViewModel> logger) : base(client, logger)
	{
		_logger = logger;
	}

	public override string Title => "Notifications";

	public override string SectionName => RemoteProtocol.SettingsApi.NotificationsSection;

	/// <summary>The .wav files of the Windows sound library, for the drop-down; the box also takes any path.</summary>
	public ObservableCollection<string> Sounds { get; } = [];

	protected override void ReadFrom(NotificationsSettingsDto values)
	{
		NotificationsEnabled = values.NotificationsEnabled;
		SoundFile = values.SessionCompletionSoundFile;
		ThrottleText = values.SessionCompletionSoundThrottleSeconds.ToString(CultureInfo.InvariantCulture);
	}

	protected override async Task AfterLoadAsync()
	{
		if (Client.ServerInfo?.Capabilities.Contains(RemoteProtocol.SoundApi.Capability) != true || Sounds.Count > 0)
			return;

		try
		{
			var list = await Client.SoundListAsync();
			foreach (var sound in list.Sounds)
				Sounds.Add(sound.File);
		}
		catch (Exception ex)
		{
			// The drop-down is a convenience: the file can still be typed.
			_logger.LogWarning(ex, "Reading the sound list failed");
		}
	}

	protected override NotificationsSettingsPatch? BuildPatch(NotificationsSettingsDto snapshot)
	{
		var file = SoundFile.Trim();
		var throttle = ParseThrottle();
		var patch = new NotificationsSettingsPatch(
			NotificationsEnabled: NotificationsEnabled != snapshot.NotificationsEnabled ? NotificationsEnabled : null,
			SessionCompletionSoundFile: file != snapshot.SessionCompletionSoundFile ? file : null,
			SessionCompletionSoundThrottleSeconds: throttle != snapshot.SessionCompletionSoundThrottleSeconds ? throttle : null);
		return patch == new NotificationsSettingsPatch() ? null : patch;
	}

	protected override void ApplyEdits(NotificationsSettingsPatch edits)
	{
		if (edits.NotificationsEnabled != null)
			NotificationsEnabled = edits.NotificationsEnabled.Value;
		if (edits.SessionCompletionSoundFile != null)
			SoundFile = edits.SessionCompletionSoundFile;
		if (edits.SessionCompletionSoundThrottleSeconds != null)
			ThrottleText = edits.SessionCompletionSoundThrottleSeconds.Value.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>Plays the chosen file on the daemon's machine, as the Avalonia Test button did.</summary>
	[RelayCommand]
	public async Task TestSoundAsync()
	{
		SoundTestText = null;
		var file = SoundFile.Trim();
		if (file.Length == 0 || Client.ServerInfo?.Capabilities.Contains(RemoteProtocol.SoundApi.Capability) != true)
			return;

		try
		{
			var result = await Client.SoundTestAsync(file);
			SoundTestText = result.Played ? null : "That file was not found.";
		}
		catch (Exception ex)
		{
			SoundTestText = ex.Message;
		}
	}

	private int ParseThrottle()
	{
		var text = ThrottleText.Trim();
		if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
			throw new SettingsInputException(string.Format(Constants.Settings.NotAnInteger, "Seconds between sounds"));

		if (seconds > Constants.Settings.MaxThrottleSeconds)
			throw new SettingsInputException(string.Format(Constants.Settings.OutOfRange, "Seconds between sounds", 0, Constants.Settings.MaxThrottleSeconds));

		return seconds;
	}
}
