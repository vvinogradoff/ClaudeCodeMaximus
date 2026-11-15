using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Switch;

/// <summary>Remembers that the one-time Avalonia import was answered (section <c>imports</c> of <c>wpf.json</c>). Delete the section to be asked again.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.Switch.ImportsSection)]
public sealed class ImportState
{
	/// <summary>The user answered the question (imported or declined); it is not asked again.</summary>
	public bool AvaloniaUiHandled { get; set; }

	/// <summary>"imported" or "declined".</summary>
	public string? AvaloniaUiOutcome { get; set; }
}

/// <summary>What an import took over and what it could not.</summary>
/// <param name="ImportedKeys">Avalonia key actions written to WPF commands.</param>
/// <param name="SkippedKeys">Avalonia key actions with no WPF command yet.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AvaloniaImportSummary(bool Theme, bool FontSizes, bool Screensaver, IReadOnlyList<string> ImportedKeys, IReadOnlyList<string> SkippedKeys);

/// <summary>
/// Maps the read-only <c>avalonia-ui</c> section (decision A23 Q8) into the WPF client's own sections: theme and font sizes (<c>appearance</c>), key bindings (<c>keyBindings</c>) and
/// the screensaver. The editable colour palette is not imported: WPF has none (decision A8).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AvaloniaUiImporter
{
	private readonly IClientSettingsStore _settings;

	public AvaloniaUiImporter(IClientSettingsStore settings)
	{
		_settings = settings;
	}

	public AvaloniaImportSummary Apply(AvaloniaUiSettingsDto source)
	{
		_settings.Update<AppearanceSettings>(appearance =>
		{
			appearance.Theme = ParseTheme(source.Theme);
			appearance.AssistantFontSize = source.AssistantFontSize;
			appearance.AssistantMarkdownFontSize = source.AssistantMarkdownFontSize;
			appearance.UserFontSize = source.UserFontSize;
			appearance.InputFontSize = source.InputFontSize;
		});

		_settings.Update<ScreensaverSettings>(screensaver =>
		{
			screensaver.Directory = source.Screensaver.Directory;
			screensaver.TimeoutSeconds = source.Screensaver.TimeoutSeconds;
			screensaver.SlideshowIntervalSeconds = source.Screensaver.SlideshowIntervalSeconds;
		});

		var imported = new List<string>();
		var skipped = new List<string>();
		var gestures = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var binding in source.KeyBindings)
		{
			if (Constants.Switch.KeyActionToCommand.TryGetValue(binding.Action, out var commandId) && ToGesture(binding.Binding) is { } gesture)
			{
				gestures[commandId] = gesture;
				imported.Add(binding.Action);
			}
			else
			{
				skipped.Add(binding.Action);
			}
		}

		if (gestures.Count > 0)
		{
			_settings.Update<KeyBindingSettings>(keys =>
			{
				foreach (var (commandId, gesture) in gestures)
					keys.Gestures[commandId] = gesture;
			});
		}

		return new AvaloniaImportSummary(Theme: true, FontSizes: true, Screensaver: true, imported, skipped);
	}

	/// <summary>Avalonia themes are "Dark" or "Light"; anything else follows Windows.</summary>
	public static ClientTheme ParseTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
	{
		"dark" => ClientTheme.Dark,
		"light" => ClientTheme.Light,
		_ => ClientTheme.System,
	};

	/// <summary>The first combination of an Avalonia binding (<c>Cmd+O, Ctrl+O</c>) in WPF notation: <c>Cmd</c> and <c>Meta</c> become <c>Ctrl</c>; an empty binding gives null.</summary>
	public static string? ToGesture(string binding)
	{
		var first = binding.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
		if (first == null)
			return null;

		var parts = first.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.Select(p => p.Equals("cmd", StringComparison.OrdinalIgnoreCase) || p.Equals("meta", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : p);
		return string.Join('+', parts);
	}
}

/// <summary>
/// On the first start with a daemon that serves <c>avalonia-ui</c>, asks once whether to import the Avalonia client's UI settings (A23 Q8) and does it. The answer is
/// remembered either way. Never ends the client: a failure is logged and the question comes back at the next start.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AvaloniaImportHost : IHostedService, IDisposable
{
	private readonly ConnectionViewModel _connection;
	private readonly IClientSettingsStore _settings;
	private readonly IDialogService _dialogs;
	private readonly ILogger<AvaloniaImportHost> _logger;
	private int _running;

	public AvaloniaImportHost(ConnectionViewModel connection, IClientSettingsStore settings, IDialogService dialogs, ILogger<AvaloniaImportHost> logger)
	{
		_connection = connection;
		_settings = settings;
		_dialogs = dialogs;
		_logger = logger;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_connection.ClientReady += OnClientReady;
		if (_connection.Client is { } client)
			OnClientReady(client);

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose() => _connection.ClientReady -= OnClientReady;

	private void OnClientReady(ITessynClient client) => _ = RunAsync(client);

	/// <summary>Asks and imports when the question is still open; true when something was imported.</summary>
	public async Task<bool> RunAsync(ITessynClient client)
	{
		// A reconnect while the dialog is open must not open a second one.
		if (Interlocked.Exchange(ref _running, 1) == 1)
			return false;

		try
		{
			if (_settings.Get<ImportState>().AvaloniaUiHandled)
				return false;

			var sections = await client.SettingsSectionsAsync();
			if (!sections.Sections.Any(s => s.Name == RemoteProtocol.SettingsApi.AvaloniaUiSection))
				return false; // an older daemon: ask again once it serves the section

			var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.Switch.ImportTitle, Constants.Switch.ImportMessage, Constants.Switch.ImportConfirm, Constants.Switch.ImportSkip));
			if (!confirmed)
			{
				_settings.Update<ImportState>(state => { state.AvaloniaUiHandled = true; state.AvaloniaUiOutcome = "declined"; });
				return false;
			}

			var result = await client.SettingsGetAsync<AvaloniaUiSettingsDto>(RemoteProtocol.SettingsApi.AvaloniaUiSection);
			var summary = new AvaloniaUiImporter(_settings).Apply(result.Values);
			_settings.Update<ImportState>(state => { state.AvaloniaUiHandled = true; state.AvaloniaUiOutcome = "imported"; });
			_logger.LogInformation("Imported the Avalonia settings: keys {Keys}, skipped keys {Skipped}", string.Join(',', summary.ImportedKeys), string.Join(',', summary.SkippedKeys));
			return true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "The Avalonia settings import failed; it will be offered again at the next start");
			return false;
		}
		finally
		{
			Interlocked.Exchange(ref _running, 0);
		}
	}
}
