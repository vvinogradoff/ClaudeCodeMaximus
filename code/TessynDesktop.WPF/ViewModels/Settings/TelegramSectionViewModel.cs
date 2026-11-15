using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Telegram tab (FR.21.7). The enable switch, chat id, allowed users, mention handle and mirror mode are a versioned section saved with the window's Save
/// button. The bot token is write-only and saved at once by its own button (FR.21.6): the window never shows it, only whether one is stored.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TelegramSectionViewModel : DaemonSectionViewModel<TelegramSettingsDto, TelegramSettingsPatch>
{
	private readonly IDialogService _dialogs;
	private readonly ILogger<TelegramSectionViewModel> _logger;

	[ObservableProperty]
	private bool _telegramEnabled;

	[ObservableProperty]
	private string _chatIdText = string.Empty;

	[ObservableProperty]
	private string _allowedUserIdsText = string.Empty;

	[ObservableProperty]
	private string _mentionHandle = string.Empty;

	[ObservableProperty]
	private TelegramMirrorModeDto _mirrorMode;

	/// <summary>What the user typed for the token; cleared as soon as it was sent.</summary>
	[ObservableProperty]
	private string _tokenInput = string.Empty;

	[ObservableProperty]
	private string _tokenText = Constants.Settings.TelegramTokenNotSet;

	[ObservableProperty]
	private string? _bridgeText;

	[ObservableProperty]
	private string? _bridgeError;

	[ObservableProperty]
	private bool _tokenSupported;

	public TelegramSectionViewModel(ITessynClient client, IDialogService dialogs, ILogger<TelegramSectionViewModel> logger) : base(client, logger)
	{
		_dialogs = dialogs;
		_logger = logger;
	}

	public override string Title => "Telegram";

	public override string SectionName => RemoteProtocol.SettingsApi.TelegramSection;

	public IReadOnlyList<SettingOption<TelegramMirrorModeDto>> MirrorModes { get; } =
	[
		new(TelegramMirrorModeDto.Full, "Full (every message)"),
		new(TelegramMirrorModeDto.AnswersOnly, "Answers only"),
		new(TelegramMirrorModeDto.Off, "Off"),
	];

	protected override void ReadFrom(TelegramSettingsDto values)
	{
		TelegramEnabled = values.TelegramEnabled;
		ChatIdText = values.TelegramChatId == 0 ? string.Empty : values.TelegramChatId.ToString(CultureInfo.InvariantCulture);
		AllowedUserIdsText = string.Join(", ", values.TelegramAllowedUserIds);
		MentionHandle = values.TelegramMentionHandle;
		MirrorMode = values.TelegramMirrorMode;
	}

	protected override async Task AfterLoadAsync()
	{
		TokenSupported = Client.ServerInfo?.Capabilities.Contains(RemoteProtocol.TelegramApi.Capability) == true;
		if (TokenSupported)
			await RefreshStatusAsync();
	}

	protected override TelegramSettingsPatch? BuildPatch(TelegramSettingsDto snapshot)
	{
		var chatId = ParseChatId();
		var ids = ParseIds();
		var handle = MentionHandle.Trim();
		var patch = new TelegramSettingsPatch(
			TelegramEnabled: TelegramEnabled != snapshot.TelegramEnabled ? TelegramEnabled : null,
			TelegramChatId: chatId != snapshot.TelegramChatId ? chatId : null,
			TelegramAllowedUserIds: ids.SequenceEqual(snapshot.TelegramAllowedUserIds) ? null : ids,
			TelegramMentionHandle: handle != snapshot.TelegramMentionHandle ? handle : null,
			TelegramMirrorMode: MirrorMode != snapshot.TelegramMirrorMode ? MirrorMode : null);
		return patch == new TelegramSettingsPatch() ? null : patch;
	}

	protected override void ApplyEdits(TelegramSettingsPatch edits)
	{
		if (edits.TelegramEnabled != null)
			TelegramEnabled = edits.TelegramEnabled.Value;
		if (edits.TelegramChatId != null)
			ChatIdText = edits.TelegramChatId.Value == 0 ? string.Empty : edits.TelegramChatId.Value.ToString(CultureInfo.InvariantCulture);
		if (edits.TelegramAllowedUserIds != null)
			AllowedUserIdsText = string.Join(", ", edits.TelegramAllowedUserIds);
		if (edits.TelegramMentionHandle != null)
			MentionHandle = edits.TelegramMentionHandle;
		if (edits.TelegramMirrorMode != null)
			MirrorMode = edits.TelegramMirrorMode.Value;
	}

	/// <summary>Reads <c>telegram.status</c>: whether a token is stored and whether the bridge runs.</summary>
	[RelayCommand]
	public async Task RefreshStatusAsync()
	{
		if (!TokenSupported)
			return;

		try
		{
			ApplyStatus(await Client.TelegramStatusAsync());
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the Telegram status failed");
			BridgeError = ex.Message;
		}
	}

	/// <summary>Stores the typed bot token (write-only) and starts the bridge when it is enabled.</summary>
	[RelayCommand]
	public async Task SaveTokenAsync()
	{
		var token = TokenInput.Trim();
		if (token.Length == 0)
		{
			ErrorText = Constants.Settings.TelegramTokenEmpty;
			return;
		}

		ClearMessages();
		try
		{
			ApplyStatus(await Client.TelegramSetTokenAsync(token));
			TokenInput = string.Empty;
			StatusText = Constants.Settings.Saved;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Storing the Telegram token failed");
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, ex.Message);
		}
	}

	/// <summary>Deletes the stored token after a confirmation; the bridge stops.</summary>
	[RelayCommand]
	public async Task ClearTokenAsync()
	{
		var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
			"Remove Telegram token", "Remove the stored bot token? The Telegram bridge stops until a new token is saved.", "Remove", IsDestructive: true));
		if (!confirmed)
			return;

		ClearMessages();
		try
		{
			ApplyStatus(await Client.TelegramClearTokenAsync());
			StatusText = "Token removed.";
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Removing the Telegram token failed");
			ErrorText = ex.Message;
		}
	}

	private void ApplyStatus(TelegramStatusDto status)
	{
		TokenText = status.HasToken ? Constants.Settings.TelegramTokenSet : Constants.Settings.TelegramTokenNotSet;
		BridgeText = status.Running ? Constants.Settings.TelegramRunning : Constants.Settings.TelegramStopped;
		BridgeError = status.LastError;
	}

	private long ParseChatId()
	{
		var text = ChatIdText.Trim();
		if (text.Length == 0)
			return 0;

		return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)
			? id
			: throw new SettingsInputException(string.Format(Constants.Settings.NotAnInteger, "Supergroup chat id"));
	}

	private List<long> ParseIds()
	{
		var ids = new List<long>();
		foreach (var part in AllowedUserIdsText.Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (!long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
				throw new SettingsInputException(string.Format(Constants.Settings.NotAnId, part));

			if (!ids.Contains(id))
				ids.Add(id);
		}

		return ids;
	}
}
