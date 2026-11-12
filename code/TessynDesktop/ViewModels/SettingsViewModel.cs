using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
	private readonly IAppSettingsService _appSettings;
	private readonly IMcpTokenStore _mcpTokenStore;
	private readonly ITelegramTokenStore _telegramTokenStore;
	private readonly ISessionSoundService _soundService;
	private readonly IRelayConfigStore _relayConfigStore;
	private readonly IRelayConnectorService _relayConnector;
	private bool _relayEnabled;
	private string _relayUrl;
	private string _relayTokenInput = string.Empty;
	private string _relayTokenStatus;
	private string _relayStatusText;
	private McpServerRowViewModel? _selectedMcpServer;
	private string _sessionFilesRoot;
	private string _claudePath;
	private string _sourceCodesLocation;
	private string _httpsProxy;
	private bool _isDarkTheme;
	private string _screensaverDirectory;
	private string _screensaverTimeout;
	private string _screensaverSlideshowInterval;
	private bool _notificationsEnabled;
	private string _sessionCompletionSoundFile;
	private string _sessionCompletionSoundThrottleSeconds;
	private bool _telegramEnabled;
	private string _telegramChatId;
	private string _telegramAllowedUserIds;
	private string _telegramMentionHandle;
	private TelegramMirrorMode _telegramMirrorMode;
	private string _telegramBotTokenInput = string.Empty;
	private string _telegramTokenStatus;

	// Color fields for the currently selected theme
	private string _inputBoxBackground;
	private string _inputBoxText;
	private string _userBubbleBackground;
	private string _userBubbleText;
	private string _codeBlockBackground;
	private string _codeBlockText;
	private string _inlineCodeBackground;
	private string _inlineCodeText;
	private string _systemBubbleBackground;
	private string _recency15MinBackground;
	private string _recency30MinBackground;
	private string _recency60MinBackground;

	public string SessionFilesRoot
	{
		get => _sessionFilesRoot;
		set => this.RaiseAndSetIfChanged(ref _sessionFilesRoot, value);
	}

	public string ClaudePath
	{
		get => _claudePath;
		set => this.RaiseAndSetIfChanged(ref _claudePath, value);
	}

	public string SourceCodesLocation
	{
		get => _sourceCodesLocation;
		set => this.RaiseAndSetIfChanged(ref _sourceCodesLocation, value);
	}

	public string HttpsProxy
	{
		get => _httpsProxy;
		set => this.RaiseAndSetIfChanged(ref _httpsProxy, value);
	}

	public string ScreensaverDirectory
	{
		get => _screensaverDirectory;
		set => this.RaiseAndSetIfChanged(ref _screensaverDirectory, value);
	}

	public string ScreensaverTimeout
	{
		get => _screensaverTimeout;
		set => this.RaiseAndSetIfChanged(ref _screensaverTimeout, value);
	}

	public string ScreensaverSlideshowInterval
	{
		get => _screensaverSlideshowInterval;
		set => this.RaiseAndSetIfChanged(ref _screensaverSlideshowInterval, value);
	}

	public bool IsDarkTheme
	{
		get => _isDarkTheme;
		set
		{
			this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
			LoadColorsFromTheme();
		}
	}

	/// <summary>Whether a desktop toast shows the same messages that ping Telegram when a session calls notify_user (FR.21.5).</summary>
	public bool NotificationsEnabled
	{
		get => _notificationsEnabled;
		set => this.RaiseAndSetIfChanged(ref _notificationsEnabled, value);
	}

	/// <summary>Every .wav under the Windows sound library, for the Session Completion Sound picker (FR.22.5).</summary>
	public ObservableCollection<string> AvailableCompletionSounds { get; } = [];

	/// <summary>Selected .wav path played by the Session Completion Sound (FR.22.4).</summary>
	public string SessionCompletionSoundFile
	{
		get => _sessionCompletionSoundFile;
		set => this.RaiseAndSetIfChanged(ref _sessionCompletionSoundFile, value);
	}

	/// <summary>Minimum seconds between Session Completion Sound plays for the same session (FR.22.3).</summary>
	public string SessionCompletionSoundThrottleSeconds
	{
		get => _sessionCompletionSoundThrottleSeconds;
		set => this.RaiseAndSetIfChanged(ref _sessionCompletionSoundThrottleSeconds, value);
	}

	public ReactiveCommand<Unit, Unit> TestCompletionSoundCommand { get; }

	/// <summary>Master toggle for the Telegram Notification Bridge (FR.21.7).</summary>
	public bool TelegramEnabled
	{
		get => _telegramEnabled;
		set => this.RaiseAndSetIfChanged(ref _telegramEnabled, value);
	}

	/// <summary>Chat ID of the Topics-enabled Telegram supergroup the bridge posts to (FR.21.1).</summary>
	public string TelegramChatId
	{
		get => _telegramChatId;
		set => this.RaiseAndSetIfChanged(ref _telegramChatId, value);
	}

	/// <summary>Comma-separated Telegram numeric user IDs authorized to message the bot (FR.21.1).</summary>
	public string TelegramAllowedUserIds
	{
		get => _telegramAllowedUserIds;
		set => this.RaiseAndSetIfChanged(ref _telegramAllowedUserIds, value);
	}

	/// <summary>Dropdown labels for <see cref="TelegramMirrorMode"/>, in enum order (FR.21.7).</summary>
	public string[] TelegramMirrorModeOptions { get; } = ["Full", "Answers only", "Off"];

	/// <summary>What the Telegram mirror posts (FR.21.22); committed on the main Save button like the mention handle (FR.21.7).</summary>
	public TelegramMirrorMode TelegramMirrorMode
	{
		get => _telegramMirrorMode;
		set
		{
			this.RaiseAndSetIfChanged(ref _telegramMirrorMode, value);
			this.RaisePropertyChanged(nameof(TelegramMirrorModeIndex));
		}
	}

	/// <summary>ComboBox selection index of <see cref="TelegramMirrorMode"/> (options are in enum order).</summary>
	public int TelegramMirrorModeIndex
	{
		get => (int)_telegramMirrorMode;
		set
		{
			if (Enum.IsDefined(typeof(TelegramMirrorMode), value))
				TelegramMirrorMode = (TelegramMirrorMode)value;
		}
	}

	/// <summary>@handle prefixed onto notify_user pings — never onto mirrored conversation text (FR.21.10).</summary>
	public string TelegramMentionHandle
	{
		get => _telegramMentionHandle;
		set => this.RaiseAndSetIfChanged(ref _telegramMentionHandle, value);
	}

	/// <summary>
	/// Staging field for a newly-entered bot token — never round-tripped from storage (FR.21.6);
	/// only <see cref="SaveTelegramTokenCommand"/> writes it, immediately, like the MCP registry's
	/// immediate-persist actions (FR.20.7).
	/// </summary>
	public string TelegramBotTokenInput
	{
		get => _telegramBotTokenInput;
		set => this.RaiseAndSetIfChanged(ref _telegramBotTokenInput, value);
	}

	/// <summary>"Configured" / "Not set" — never shows the token itself.</summary>
	public string TelegramTokenStatus
	{
		get => _telegramTokenStatus;
		private set => this.RaiseAndSetIfChanged(ref _telegramTokenStatus, value);
	}

	public ReactiveCommand<Unit, Unit> SaveTelegramTokenCommand { get; }

	/// <summary>Whether the desktop dials out to the web server (web access). Committed by <see cref="SaveRelayCommand"/>.</summary>
	public bool RelayEnabled
	{
		get => _relayEnabled;
		set => this.RaiseAndSetIfChanged(ref _relayEnabled, value);
	}

	/// <summary>Base URL of the web server, e.g. https://tessyn.vvinogradoff.com.</summary>
	public string RelayUrl
	{
		get => _relayUrl;
		set => this.RaiseAndSetIfChanged(ref _relayUrl, value);
	}

	/// <summary>Write-only entry box for the link token; the stored token never round-trips into the view-model.</summary>
	public string RelayTokenInput
	{
		get => _relayTokenInput;
		set => this.RaiseAndSetIfChanged(ref _relayTokenInput, value);
	}

	/// <summary>"Not set" or "Configured".</summary>
	public string RelayTokenStatus
	{
		get => _relayTokenStatus;
		private set => this.RaiseAndSetIfChanged(ref _relayTokenStatus, value);
	}

	/// <summary>Live connector state, e.g. "Connected since 14:02".</summary>
	public string RelayStatusText
	{
		get => _relayStatusText;
		private set => this.RaiseAndSetIfChanged(ref _relayStatusText, value);
	}

	/// <summary>Saves the web-access settings immediately (DPAPI file, not appsettings) and restarts the connector.</summary>
	public ReactiveCommand<Unit, Unit> SaveRelayCommand { get; }

	public string InputBoxBackground
	{
		get => _inputBoxBackground;
		set => this.RaiseAndSetIfChanged(ref _inputBoxBackground, value);
	}

	public string InputBoxText
	{
		get => _inputBoxText;
		set => this.RaiseAndSetIfChanged(ref _inputBoxText, value);
	}

	public string UserBubbleBackground
	{
		get => _userBubbleBackground;
		set => this.RaiseAndSetIfChanged(ref _userBubbleBackground, value);
	}

	public string UserBubbleText
	{
		get => _userBubbleText;
		set => this.RaiseAndSetIfChanged(ref _userBubbleText, value);
	}

	public string CodeBlockBackground
	{
		get => _codeBlockBackground;
		set => this.RaiseAndSetIfChanged(ref _codeBlockBackground, value);
	}

	public string CodeBlockText
	{
		get => _codeBlockText;
		set => this.RaiseAndSetIfChanged(ref _codeBlockText, value);
	}

	public string InlineCodeBackground
	{
		get => _inlineCodeBackground;
		set => this.RaiseAndSetIfChanged(ref _inlineCodeBackground, value);
	}

	public string InlineCodeText
	{
		get => _inlineCodeText;
		set => this.RaiseAndSetIfChanged(ref _inlineCodeText, value);
	}

	public string SystemBubbleBackground
	{
		get => _systemBubbleBackground;
		set => this.RaiseAndSetIfChanged(ref _systemBubbleBackground, value);
	}

	public string Recency15MinBackground
	{
		get => _recency15MinBackground;
		set => this.RaiseAndSetIfChanged(ref _recency15MinBackground, value);
	}

	public string Recency30MinBackground
	{
		get => _recency30MinBackground;
		set => this.RaiseAndSetIfChanged(ref _recency30MinBackground, value);
	}

	public string Recency60MinBackground
	{
		get => _recency60MinBackground;
		set => this.RaiseAndSetIfChanged(ref _recency60MinBackground, value);
	}

	public ReactiveCommand<Unit, Unit> SaveCommand { get; }

	/// <summary>
	/// Key bindings displayed in the settings window.
	/// Each entry has an action name and its current binding string.
	/// </summary>
	public ObservableCollection<KeyBindingEntryViewModel> KeyBindingEntries { get; } = [];

	/// <summary>External MCP Server Registry rows (FR.20.7). Unlike the fields above, edits here
	/// persist immediately rather than waiting for the window's Save button — see
	/// <see cref="McpServerRowViewModel"/>.</summary>
	public ObservableCollection<McpServerRowViewModel> McpServers { get; } = [];

	public McpServerRowViewModel? SelectedMcpServer
	{
		get => _selectedMcpServer;
		set => this.RaiseAndSetIfChanged(ref _selectedMcpServer, value);
	}

	public SettingsViewModel(IAppSettingsService appSettings, IMcpTokenStore mcpTokenStore, ITelegramTokenStore telegramTokenStore, ISessionSoundService soundService,
		IRelayConfigStore relayConfigStore, IRelayConnectorService relayConnector)
	{
		_appSettings   = appSettings;
		_mcpTokenStore = mcpTokenStore;
		_telegramTokenStore = telegramTokenStore;
		_soundService  = soundService;
		_relayConfigStore = relayConfigStore;
		_relayConnector = relayConnector;
		var relayConfig = relayConfigStore.Load();
		_relayEnabled = relayConfig?.Enabled ?? false;
		_relayUrl = relayConfig?.Url ?? string.Empty;
		_relayTokenStatus = string.IsNullOrEmpty(relayConfig?.Token) ? "Not set" : "Configured";
		_relayStatusText = DescribeRelayStatus(relayConnector.Status);
		relayConnector.StatusChanged += OnRelayStatusChanged;
		_sessionFilesRoot = appSettings.Settings.SessionFilesRoot;
		_claudePath = appSettings.Settings.ClaudePath;
		_sourceCodesLocation = appSettings.Settings.SourceCodesLocation;
		_httpsProxy = appSettings.Settings.HttpsProxy;
		_screensaverDirectory = appSettings.Settings.ScreensaverDirectory;
		_screensaverTimeout = appSettings.Settings.ScreensaverTimeout.ToString();
		_screensaverSlideshowInterval = appSettings.Settings.ScreensaverSlideshowInterval.ToString();
		_isDarkTheme = appSettings.Settings.Theme == "Dark";
		_notificationsEnabled = appSettings.Settings.NotificationsEnabled;
		_sessionCompletionSoundFile = appSettings.Settings.SessionCompletionSoundFile;
		_sessionCompletionSoundThrottleSeconds = appSettings.Settings.SessionCompletionSoundThrottleSeconds.ToString();
		_telegramEnabled = appSettings.Settings.TelegramEnabled;
		_telegramChatId = appSettings.Settings.TelegramChatId == 0 ? string.Empty : appSettings.Settings.TelegramChatId.ToString();
		_telegramAllowedUserIds = string.Join(", ", appSettings.Settings.TelegramAllowedUserIds);
		_telegramMentionHandle = appSettings.Settings.TelegramMentionHandle;
		_telegramMirrorMode = appSettings.Settings.TelegramMirrorMode;
		_telegramTokenStatus = telegramTokenStore.Load() is null ? "Not set" : "Configured";

		var colors = _isDarkTheme ? appSettings.Settings.DarkColors : appSettings.Settings.LightColors;
		_inputBoxBackground    = colors.InputBoxBackground;
		_inputBoxText          = colors.InputBoxText;
		_userBubbleBackground  = colors.UserBubbleBackground;
		_userBubbleText        = colors.UserBubbleText;
		_codeBlockBackground   = colors.CodeBlockBackground;
		_codeBlockText         = colors.CodeBlockText;
		_inlineCodeBackground  = colors.InlineCodeBackground;
		_inlineCodeText        = colors.InlineCodeText;
		_systemBubbleBackground = colors.SystemBubbleBackground;
		_recency15MinBackground = colors.Recency15MinBackground;
		_recency30MinBackground = colors.Recency30MinBackground;
		_recency60MinBackground = colors.Recency60MinBackground;

		SaveCommand = ReactiveCommand.Create(Save);
		SaveTelegramTokenCommand = ReactiveCommand.Create(SaveTelegramToken);
		SaveRelayCommand = ReactiveCommand.Create(SaveRelay);
		TestCompletionSoundCommand = ReactiveCommand.Create(TestCompletionSound);

		LoadKeyBindings();
		LoadMcpServers();
		LoadAvailableSounds();
	}

	/// <summary>Populates the sound picker (FR.22.5), adding the currently configured file if it fell outside the stock library.</summary>
	private void LoadAvailableSounds()
	{
		foreach (var file in _soundService.GetAvailableSounds())
			AvailableCompletionSounds.Add(file);

		if (!string.IsNullOrWhiteSpace(_sessionCompletionSoundFile) && !AvailableCompletionSounds.Contains(_sessionCompletionSoundFile))
			AvailableCompletionSounds.Insert(0, _sessionCompletionSoundFile);
	}

	/// <summary>Plays the currently selected sound immediately, bypassing the per-session throttle (FR.22.5).</summary>
	private void TestCompletionSound() => _soundService.PlayTestSound(_sessionCompletionSoundFile);

	/// <summary>
	/// Persists the entered bot token immediately via DPAPI (FR.21.6), independent of the window's
	/// main Save button — mirrors the MCP registry's immediate-persist actions (FR.20.7).
	/// </summary>
	private void SaveTelegramToken()
	{
		if (string.IsNullOrWhiteSpace(TelegramBotTokenInput))
			return;

		_telegramTokenStore.Save(TelegramBotTokenInput.Trim());
		TelegramBotTokenInput = string.Empty;
		TelegramTokenStatus = "Configured";
	}

	/// <summary>Persists the web-access config (keeping the stored token when the input box is empty) and restarts the connector off the UI thread.</summary>
	private void SaveRelay()
	{
		var existing = _relayConfigStore.Load();
		var token = string.IsNullOrWhiteSpace(RelayTokenInput) ? existing?.Token ?? string.Empty : RelayTokenInput.Trim();

		_relayConfigStore.Save(new RelayConfigModel { Enabled = RelayEnabled, Url = RelayUrl.Trim(), Token = token });
		RelayTokenInput = string.Empty;
		RelayTokenStatus = token.Length == 0 ? "Not set" : "Configured";

		_ = System.Threading.Tasks.Task.Run(_relayConnector.Restart);
	}

	private void OnRelayStatusChanged(RelayStatusModel status) =>
		Avalonia.Threading.Dispatcher.UIThread.Post(() => RelayStatusText = DescribeRelayStatus(status));

	private static string DescribeRelayStatus(RelayStatusModel status) => status.State switch
	{
		RelayState.Connected  => $"Connected since {status.Since.ToLocalTime():HH:mm}",
		RelayState.Connecting => "Connecting…",
		RelayState.Error      => $"Error: {status.LastError}",
		_                     => "Disabled",
	};

	/// <summary>Detaches from the connector's status event when the window closes.</summary>
	public void Dispose() => _relayConnector.StatusChanged -= OnRelayStatusChanged;

	private void LoadKeyBindings()
	{
		KeyBindingEntries.Clear();
		foreach (var (action, binding) in _appSettings.Settings.KeyBindings.Bindings)
			KeyBindingEntries.Add(new KeyBindingEntryViewModel(action, binding));
	}

	private void LoadMcpServers()
	{
		McpServers.Clear();
		foreach (var entry in _appSettings.Settings.ExternalMcpServers)
			McpServers.Add(new McpServerRowViewModel(entry, _mcpTokenStore, _appSettings.Save));
	}

	/// <summary>Appends a newly-created registry entry (FR.20.1) and persists immediately.</summary>
	public void AddMcpServer(ExternalMcpServerModel model)
	{
		_appSettings.Settings.ExternalMcpServers.Add(model);
		_appSettings.Save();
		McpServers.Add(new McpServerRowViewModel(model, _mcpTokenStore, _appSettings.Save));
	}

	/// <summary>Replaces an edited registry entry in place (by <see cref="ExternalMcpServerModel.Id"/>) and persists immediately.</summary>
	public void ReplaceMcpServer(McpServerRowViewModel row, ExternalMcpServerModel updated)
	{
		var list  = _appSettings.Settings.ExternalMcpServers;
		var index = list.FindIndex(e => e.Id == updated.Id);
		if (index >= 0)
			list[index] = updated;
		_appSettings.Save();

		var rowIndex = McpServers.IndexOf(row);
		var newRow   = new McpServerRowViewModel(updated, _mcpTokenStore, _appSettings.Save);
		if (rowIndex >= 0)
			McpServers[rowIndex] = newRow;
		if (SelectedMcpServer == row)
			SelectedMcpServer = newRow;
	}

	/// <summary>Removes a registry entry and its stored OAuth token (if any), and persists immediately.</summary>
	public void RemoveMcpServer(McpServerRowViewModel row)
	{
		_appSettings.Settings.ExternalMcpServers.RemoveAll(e => e.Id == row.Model.Id);
		_appSettings.Save();
		_mcpTokenStore.Delete(row.Model.Id);

		McpServers.Remove(row);
		if (SelectedMcpServer == row)
			SelectedMcpServer = null;
	}

	private void LoadColorsFromTheme()
	{
		var colors = _isDarkTheme ? _appSettings.Settings.DarkColors : _appSettings.Settings.LightColors;
		InputBoxBackground    = colors.InputBoxBackground;
		InputBoxText          = colors.InputBoxText;
		UserBubbleBackground  = colors.UserBubbleBackground;
		UserBubbleText        = colors.UserBubbleText;
		CodeBlockBackground   = colors.CodeBlockBackground;
		CodeBlockText         = colors.CodeBlockText;
		InlineCodeBackground  = colors.InlineCodeBackground;
		InlineCodeText        = colors.InlineCodeText;
		SystemBubbleBackground = colors.SystemBubbleBackground;
		Recency15MinBackground = colors.Recency15MinBackground;
		Recency30MinBackground = colors.Recency30MinBackground;
		Recency60MinBackground = colors.Recency60MinBackground;
	}

	private void Save()
	{
		_appSettings.Settings.SessionFilesRoot = _sessionFilesRoot;
		_appSettings.Settings.ClaudePath = _claudePath;
		_appSettings.Settings.SourceCodesLocation = _sourceCodesLocation;
		_appSettings.Settings.HttpsProxy = _httpsProxy;
		_appSettings.Settings.ScreensaverDirectory = _screensaverDirectory;
		_appSettings.Settings.ScreensaverTimeout = int.TryParse(_screensaverTimeout, out var st) ? Math.Max(0, st) : 120;
		_appSettings.Settings.ScreensaverSlideshowInterval = int.TryParse(_screensaverSlideshowInterval, out var si) ? Math.Max(1, si) : 10;
		_appSettings.Settings.NotificationsEnabled = _notificationsEnabled;
		_appSettings.Settings.SessionCompletionSoundFile = _sessionCompletionSoundFile;
		_appSettings.Settings.SessionCompletionSoundThrottleSeconds =
			int.TryParse(_sessionCompletionSoundThrottleSeconds, out var soundThrottle) ? Math.Max(0, soundThrottle) : Constants.SessionSound.DefaultThrottleSeconds;
		_appSettings.Settings.TelegramEnabled = _telegramEnabled;
		_appSettings.Settings.TelegramChatId = long.TryParse(_telegramChatId, out var chatId) ? chatId : 0;
		_appSettings.Settings.TelegramAllowedUserIds = _telegramAllowedUserIds
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(s => long.TryParse(s, out var id) ? (long?)id : null)
			.Where(id => id.HasValue)
			.Select(id => id!.Value)
			.ToList();
		_appSettings.Settings.TelegramMentionHandle = _telegramMentionHandle;
		_appSettings.Settings.TelegramMirrorMode = _telegramMirrorMode;
		_appSettings.Settings.Theme = _isDarkTheme ? "Dark" : "Light";

		// Save colors to the appropriate theme
		var colors = _isDarkTheme ? _appSettings.Settings.DarkColors : _appSettings.Settings.LightColors;
		colors.InputBoxBackground    = _inputBoxBackground;
		colors.InputBoxText          = _inputBoxText;
		colors.UserBubbleBackground  = _userBubbleBackground;
		colors.UserBubbleText        = _userBubbleText;
		colors.CodeBlockBackground   = _codeBlockBackground;
		colors.CodeBlockText         = _codeBlockText;
		colors.InlineCodeBackground  = _inlineCodeBackground;
		colors.InlineCodeText        = _inlineCodeText;
		colors.SystemBubbleBackground = _systemBubbleBackground;
		colors.Recency15MinBackground = _recency15MinBackground;
		colors.Recency30MinBackground = _recency30MinBackground;
		colors.Recency60MinBackground = _recency60MinBackground;

		// Save key bindings
		foreach (var entry in KeyBindingEntries)
			_appSettings.Settings.KeyBindings.Bindings[entry.ActionName] = entry.Binding;

		ThemeApplicator.Apply(_appSettings.Settings);
		_appSettings.Save();
	}
}
