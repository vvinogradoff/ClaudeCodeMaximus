using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>What the import did: the groups it copied and the key bindings it could not place.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AvaloniaImportResult(IReadOnlyList<string> Imported, IReadOnlyList<string> SkippedBindings);

/// <summary>
/// The one-time import of the Avalonia client's own settings (decision A23 Q8, Q9), read from the daemon's read-only <c>avalonia-ui</c> section: theme, font sizes,
/// key bindings and screensaver go into the WPF client settings. The colour palette is not imported (dropped, A8). A binding is carried over only
/// when its action has a command here; macOS <c>Cmd</c> combinations become <c>Ctrl</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AvaloniaImporter
{
	/// <summary>Avalonia action name to the command id that replaces it here; actions without an entry (Send, ImportSessions, CloseDialog) have no command in this client yet.</summary>
	public static readonly IReadOnlyDictionary<string, string> ActionCommands = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["OpenSettings"] = Constants.Settings.OpenCommandId,
		["AddDirectory"] = Constants.TreeCommands.AddDirectory,
	};

	private readonly IClientSettingsStore _store;
	private readonly IShellCommands _commands;

	public AvaloniaImporter(IClientSettingsStore store, IShellCommands commands)
	{
		_store = store;
		_commands = commands;
	}

	/// <summary>True when the import was done before.</summary>
	public bool IsDone => _store.Get<AvaloniaImportSettings>().Done;

	/// <summary>Writes the values into the client settings and records that the import happened.</summary>
	public AvaloniaImportResult Apply(AvaloniaUiSettingsDto dto)
	{
		var imported = new List<string>();
		var skipped = new List<string>();

		_store.Update<AppearanceSettings>(a =>
		{
			a.Theme = dto.Theme.ToLowerInvariant() switch
			{
				"dark" => ClientTheme.Dark,
				"light" => ClientTheme.Light,
				_ => ClientTheme.System,
			};
			a.AssistantFontSize = Size(dto.AssistantFontSize, a.AssistantFontSize);
			a.AssistantMarkdownFontSize = Size(dto.AssistantMarkdownFontSize, a.AssistantMarkdownFontSize);
			a.UserFontSize = Size(dto.UserFontSize, a.UserFontSize);
			a.InputFontSize = Size(dto.InputFontSize, a.InputFontSize);
		});
		imported.Add("theme and font sizes");

		_store.Update<ScreensaverSettings>(s =>
		{
			s.Directory = dto.Screensaver.Directory;
			s.TimeoutSeconds = Math.Clamp(dto.Screensaver.TimeoutSeconds, 0, Constants.Settings.MaxScreensaverSeconds);
			s.SlideshowIntervalSeconds = Math.Clamp(dto.Screensaver.SlideshowIntervalSeconds, Constants.Settings.MinScreensaverSeconds, Constants.Settings.MaxScreensaverSeconds);
		});
		imported.Add("screensaver");

		var carried = 0;
		_store.Update<KeyBindingSettings>(k =>
		{
			foreach (var binding in dto.KeyBindings)
			{
				var command = ActionCommands.TryGetValue(binding.Action, out var id) ? _commands.Find(id) : null;
				var gesture = GestureText.FromAvalonia(binding.Binding);
				if (command == null || gesture == null)
				{
					skipped.Add(binding.Action);
					continue;
				}

				if (string.Equals(gesture, command.DefaultGesture, StringComparison.OrdinalIgnoreCase))
					k.Gestures.Remove(command.Id);
				else
					k.Gestures[command.Id] = gesture;

				carried++;
			}
		});
		imported.Add($"{carried} key binding(s)");

		_store.Update<AvaloniaImportSettings>(m =>
		{
			m.Done = true;
			m.ImportedAtUtc = DateTime.UtcNow;
		});
		return new AvaloniaImportResult(imported, skipped);
	}

	private static double Size(double value, double fallback) =>
		value is >= Constants.Settings.MinFontSize and <= Constants.Settings.MaxFontSize ? value : fallback;
}
