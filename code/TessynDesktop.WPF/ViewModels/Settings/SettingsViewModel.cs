using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Settings window (W6a, W6b): one tab per section. The daemon's sections (General, Notifications, Telegram) are versioned and saved with the window's Save button together
/// with the client sections (Appearance, Screensaver, Keyboard shortcuts); the Telegram token, the web link, the MCP registry and the model refresh act at once. It follows
/// <c>settings.changed</c> and <c>mcp.changed</c> while open. A section whose capability the daemon does not announce shows a note instead of its controls. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly IDialogService _dialogs;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<SettingsViewModel> _logger;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasStatus))]
	private string? _statusText;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string? _errorText;

	[ObservableProperty]
	private bool _isSaving;

	public SettingsViewModel(
		ITessynClient client,
		IClientSettingsStore store,
		IShellCommands commands,
		IDialogService dialogs,
		IFolderPicker picker,
		IMcpServerEditorPresenter editorPresenter,
		ProfileViewModel profiles,
		IUiDispatcher ui,
		ILoggerFactory loggers,
		IAppearanceService? appearance = null,
		IPreviewSettingsService? previewSettings = null,
		PreviewGroupMatcher? previewMatcher = null,
		IBrowserDataCleaner? browserDataCleaner = null)
	{
		_client = client;
		_dialogs = dialogs;
		_ui = ui;
		_logger = loggers.CreateLogger<SettingsViewModel>();

		General = new GeneralSectionViewModel(client, loggers.CreateLogger<GeneralSectionViewModel>());
		Notifications = new NotificationsSectionViewModel(client, loggers.CreateLogger<NotificationsSectionViewModel>());
		Telegram = new TelegramSectionViewModel(client, dialogs, loggers.CreateLogger<TelegramSectionViewModel>());
		WebAccess = new WebAccessSectionViewModel(client, loggers.CreateLogger<WebAccessSectionViewModel>());
		Mcp = new McpSectionViewModel(client, dialogs, editorPresenter, loggers.CreateLogger<McpSectionViewModel>());
		ModelsProfiles = new ModelsProfilesSectionViewModel(client, profiles, loggers.CreateLogger<ModelsProfilesSectionViewModel>());
		Appearance = new AppearanceSectionViewModel(store);
		Screensaver = new ScreensaverSectionViewModel(store, picker);
		Keyboard = new KeyboardSectionViewModel(store, commands);
		Startup = new StartupSectionViewModel(store);
		Colours = new ColoursSectionViewModel(store, appearance);
		if (previewSettings != null)
			Previews = new PreviewsSectionViewModel(previewSettings, previewMatcher ?? new PreviewGroupMatcher(), browserDataCleaner, dialogs, ui);

		client.NotificationReceived += OnNotification;
	}

	public GeneralSectionViewModel General { get; }

	public NotificationsSectionViewModel Notifications { get; }

	public TelegramSectionViewModel Telegram { get; }

	public WebAccessSectionViewModel WebAccess { get; }

	public McpSectionViewModel Mcp { get; }

	public ModelsProfilesSectionViewModel ModelsProfiles { get; }

	public AppearanceSectionViewModel Appearance { get; }

	public ScreensaverSectionViewModel Screensaver { get; }

	public KeyboardSectionViewModel Keyboard { get; }

	public StartupSectionViewModel Startup { get; }

	public ColoursSectionViewModel Colours { get; }

	/// <summary>The Previews tab; null when no preview settings service was given (a test window without one).</summary>
	public PreviewsSectionViewModel? Previews { get; }

	public bool HasPreviews => Previews != null;

	public bool HasStatus => !string.IsNullOrEmpty(StatusText);

	public bool HasError => !string.IsNullOrEmpty(ErrorText);

	/// <summary>The sections the window's Save button applies.</summary>
	private IEnumerable<ISettingsEditor> Editors => Previews == null ? [General, Notifications, Telegram, Appearance, Colours, Screensaver, Keyboard, Startup] : [General, Notifications, Telegram, Appearance, Colours, Screensaver, Keyboard, Startup, Previews];

	public bool HasChanges => Editors.Any(e => e.HasChanges);

	/// <summary>The titles of the tabs with unsaved changes (pending colours included), in tab order.</summary>
	public IReadOnlyList<string> UnsavedTabs => Editors.Where(e => e.HasChanges).Select(e => e is SettingsSectionViewModel section ? section.Title : e.GetType().Name).ToList();

	/// <summary>
	/// FR.4.7: may the window close? With no unsaved changes: yes, without a question. Otherwise a dialog names the tabs and offers Save, Discard and Cancel. Save saves every changed
	/// tab like the Save button and closes only when all were saved (a failure leaves its error in the window); Discard closes and stores nothing; Cancel (or closing the dialog) keeps the window.
	/// </summary>
	public async Task<bool> ConfirmCloseAsync()
	{
		var tabs = UnsavedTabs;
		if (tabs.Count == 0)
			return true;

		var choice = await _dialogs.ChoiceAsync(new ChoiceDialogRequest(
			Constants.Settings.WindowTitle,
			string.Format(Constants.Settings.UnsavedFormat, string.Join(", ", tabs)),
			[new DialogChoice(Constants.Settings.SaveChoiceId, Constants.Settings.SaveChoiceLabel), new DialogChoice(Constants.Settings.DiscardChoiceId, Constants.Settings.DiscardChoiceLabel)]));
		switch (choice)
		{
			case Constants.Settings.SaveChoiceId:
				await SaveAsync();
				return !HasError && !HasChanges;
			case Constants.Settings.DiscardChoiceId:
				return true;
			default:
				return false;
		}
	}

	/// <summary>Reads every section; one that fails shows its own error and does not stop the others.</summary>
	public async Task InitializeAsync()
	{
		await Task.WhenAll(
			General.InitializeAsync(RemoteProtocol.SettingsApi.Capability),
			Notifications.InitializeAsync(RemoteProtocol.SettingsApi.Capability),
			Telegram.InitializeAsync(RemoteProtocol.SettingsApi.Capability),
			WebAccess.InitializeAsync(),
			Mcp.InitializeAsync());
	}

	/// <summary>Saves every section with edits. Each reports its own result; the window says only whether everything went through.</summary>
	[RelayCommand]
	public async Task SaveAsync()
	{
		if (IsSaving)
			return;

		IsSaving = true;
		StatusText = null;
		ErrorText = null;
		try
		{
			var changed = Editors.Where(e => e.HasChanges).ToList();
			if (changed.Count == 0)
			{
				StatusText = Constants.Settings.NothingToSave;
				return;
			}

			var allSaved = true;
			foreach (var editor in changed)
				allSaved &= await editor.SaveAsync();

			if (allSaved)
				StatusText = Constants.Settings.Saved;
			else
				ErrorText = "Some sections were not saved; see their tabs.";
		}
		finally
		{
			IsSaving = false;
		}
	}

	private bool HasCapability(string capability) => _client.ServerInfo?.Capabilities.Contains(capability) == true;

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		if (notification.TryGetSettingsChanged(out var changed))
		{
			_ui.Post(() => _ = RouteSettingsChangedAsync(changed));
		}
		else if (notification.TryGetMcpChanged(out _))
		{
			_ui.Post(() => _ = Mcp.OnChangedAsync());
		}
	}

	private async Task RouteSettingsChangedAsync(SettingsChangedPayload changed)
	{
		try
		{
			switch (changed.Section)
			{
				case RemoteProtocol.SettingsApi.GeneralSection:
					await General.OnChangedElsewhereAsync(changed.Version);
					break;
				case RemoteProtocol.SettingsApi.NotificationsSection:
					await Notifications.OnChangedElsewhereAsync(changed.Version);
					break;
				case RemoteProtocol.SettingsApi.TelegramSection:
					await Telegram.OnChangedElsewhereAsync(changed.Version);
					break;
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Following settings.changed for {Section} failed", changed.Section);
		}
	}

	public void Dispose()
	{
		_client.NotificationReceived -= OnNotification;
		Previews?.Dispose();
	}
}
