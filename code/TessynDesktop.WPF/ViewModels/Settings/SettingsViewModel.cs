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
	private readonly AvaloniaImporter _importer;
	private readonly ILogger<SettingsViewModel> _logger;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasStatus))]
	private string? _statusText;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string? _errorText;

	[ObservableProperty]
	private bool _importOffered;

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
		IAppearanceService? appearance = null)
	{
		_client = client;
		_dialogs = dialogs;
		_ui = ui;
		_logger = loggers.CreateLogger<SettingsViewModel>();
		_importer = new AvaloniaImporter(store, commands);

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

	public bool HasStatus => !string.IsNullOrEmpty(StatusText);

	public bool HasError => !string.IsNullOrEmpty(ErrorText);

	public string ImportOfferText => Constants.Settings.ImportOffer;

	/// <summary>The sections the window's Save button applies.</summary>
	private IEnumerable<ISettingsEditor> Editors => [General, Notifications, Telegram, Appearance, Colours, Screensaver, Keyboard, Startup];

	public bool HasChanges => Editors.Any(e => e.HasChanges);

	/// <summary>Reads every section; one that fails shows its own error and does not stop the others.</summary>
	public async Task InitializeAsync()
	{
		ImportOffered = !_importer.IsDone && HasCapability(RemoteProtocol.SettingsApi.Capability);
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

	/// <summary>Copies the Avalonia client's theme, fonts, key bindings and screensaver, after a confirmation (once is enough, but it may be repeated).</summary>
	[RelayCommand]
	public async Task ImportFromAvaloniaAsync()
	{
		StatusText = null;
		ErrorText = null;
		if (!HasCapability(RemoteProtocol.SettingsApi.Capability))
			return;

		AvaloniaUiSettingsDto values;
		try
		{
			values = (await _client.SettingsGetAsync<AvaloniaUiSettingsDto>(RemoteProtocol.SettingsApi.AvaloniaUiSection)).Values;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the Avalonia settings failed");
			ErrorText = string.Format(Constants.Settings.ImportFailedFormat, ex.Message);
			return;
		}

		var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.Settings.ImportTitle, Constants.Settings.ImportPrompt, Constants.Settings.ImportConfirm));
		if (!confirmed)
			return;

		try
		{
			var result = _importer.Apply(values);
			Appearance.Read();
			Screensaver.Read();
			Keyboard.Read();
			ImportOffered = false;
			StatusText = string.Format(Constants.Settings.ImportDone, string.Join(", ", result.Imported))
				+ (result.SkippedBindings.Count > 0 ? " " + string.Format(Constants.Settings.ImportSkippedFormat, string.Join(", ", result.SkippedBindings)) : string.Empty);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Importing the Avalonia settings failed");
			ErrorText = string.Format(Constants.Settings.ImportFailedFormat, ex.Message);
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

	public void Dispose() => _client.NotificationReceived -= OnNotification;
}
