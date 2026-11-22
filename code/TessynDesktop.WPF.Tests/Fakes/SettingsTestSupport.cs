using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An in-memory client state (no file), so settings tests read and write sections without a disk.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MemoryClientState : IClientStateStore
{
	public ClientStateModel State { get; } = new();

	public Task LoadAsync() => Task.CompletedTask;

	public void Update(Action<ClientStateModel> change) => change(State);

	public Task FlushAsync() => Task.CompletedTask;
}

/// <summary>An <see cref="IMcpServerEditorPresenter"/> that hands the editor view model to a script instead of showing a window.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScriptedMcpEditorPresenter : IMcpServerEditorPresenter
{
	public List<McpServerEditorViewModel> Shown { get; } = [];

	public Func<McpServerEditorViewModel, Task> Script { get; set; } = _ => Task.CompletedTask;

	public async Task ShowAsync(McpServerEditorViewModel editor)
	{
		Shown.Add(editor);
		await Script(editor);
	}
}

/// <summary>Builders shared by the Settings tests (W6a, W6b).</summary>
/// <remarks>Created by Claude</remarks>
public static class SettingsTestSupport
{
	public static readonly string[] AllCapabilities =
	[
		RemoteProtocol.SettingsApi.Capability, RemoteProtocol.SoundApi.Capability, RemoteProtocol.TelegramApi.Capability, RemoteProtocol.RelayApi.Capability,
		RemoteProtocol.McpApi.Capability, RemoteProtocol.ModelsApi.Capability, RemoteProtocol.ProfilesAdmin.Capability,
	];

	/// <summary>Announces the capabilities of the Settings window (all of them unless some are given).</summary>
	public static FakeTessynClient AnnouncingSettings(this FakeTessynClient client, params string[] capabilities)
	{
		client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow, capabilities.Length == 0 ? AllCapabilities : capabilities, 1, 0);
		return client;
	}

	/// <summary>Scripts the three versioned sections with distinct values, and makes updates apply to them.</summary>
	public static FakeTessynClient WithSettingsValues(this FakeTessynClient client)
	{
		client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection] = new GeneralSettingsDto(
			@"C:\Sessions", "claude", @"C:\Src", string.Empty, "http://localhost:11434", false, false,
			new FileAccessSettingsDto(FileAccessModeDto.ReferencedOnly, 65536, 1000, 2000, 3000, 4000), ["a.example.com"]);
		client.SettingsValues[RemoteProtocol.SettingsApi.NotificationsSection] = new NotificationsSettingsDto(true, @"C:\Windows\Media\ding.wav", 180);
		client.SettingsValues[RemoteProtocol.SettingsApi.TelegramSection] = new TelegramSettingsDto(false, -100123, [11, 22], "@me", TelegramMirrorModeDto.Full);
		client.Sounds = [@"C:\Windows\Media\ding.wav", @"C:\Windows\Media\chimes.wav"];
		client.SettingsPatchApplier = ApplyPatch(client);
		return client;
	}

	private static Func<string, object, object?> ApplyPatch(FakeTessynClient client) => (section, patch) =>
	{
		switch (patch)
		{
			case GeneralSettingsPatch g:
				var general = (GeneralSettingsDto)client.SettingsValues[section];
				return general with
				{
					HttpsProxy = g.HttpsProxy ?? general.HttpsProxy,
					OllamaBaseUrl = g.OllamaBaseUrl ?? general.OllamaBaseUrl,
					AgentToolsEnabled = g.AgentToolsEnabled ?? general.AgentToolsEnabled,
					CodeReferencesEnabled = g.CodeReferencesEnabled ?? general.CodeReferencesEnabled,
					FileAccess = g.FileAccess?.Mode != null ? general.FileAccess with { Mode = g.FileAccess.Mode.Value } : general.FileAccess,
					MediaPreviewHosts = g.MediaPreviewHosts ?? general.MediaPreviewHosts,
				};
			case NotificationsSettingsPatch n:
				var notifications = (NotificationsSettingsDto)client.SettingsValues[section];
				return notifications with
				{
					NotificationsEnabled = n.NotificationsEnabled ?? notifications.NotificationsEnabled,
					SessionCompletionSoundFile = n.SessionCompletionSoundFile ?? notifications.SessionCompletionSoundFile,
					SessionCompletionSoundThrottleSeconds = n.SessionCompletionSoundThrottleSeconds ?? notifications.SessionCompletionSoundThrottleSeconds,
				};
			case TelegramSettingsPatch t:
				var telegram = (TelegramSettingsDto)client.SettingsValues[section];
				return telegram with
				{
					TelegramEnabled = t.TelegramEnabled ?? telegram.TelegramEnabled,
					TelegramChatId = t.TelegramChatId ?? telegram.TelegramChatId,
					TelegramAllowedUserIds = t.TelegramAllowedUserIds ?? telegram.TelegramAllowedUserIds,
					TelegramMentionHandle = t.TelegramMentionHandle ?? telegram.TelegramMentionHandle,
					TelegramMirrorMode = t.TelegramMirrorMode ?? telegram.TelegramMirrorMode,
				};
			default:
				return null;
		}
	};

	public static ClientSettingsStore NewStore(MemoryClientState? state = null) =>
		new(state ?? new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);

	/// <summary>A registry with a few commands of known default gestures.</summary>
	public static ShellCommands NewCommands(IClientSettingsStore store)
	{
		var commands = new ShellCommands(store, NullLogger<ShellCommands>.Instance);
		commands.Register(new ShellCommand { Id = Constants.Settings.OpenCommandId, Title = "Settings…", DefaultGesture = Constants.Settings.OpenCommandGesture, ExecuteAsync = _ => Task.CompletedTask });
		commands.Register(new ShellCommand { Id = Constants.TreeCommands.AddDirectory, Title = "Add directory…", ExecuteAsync = _ => Task.CompletedTask });
		commands.Register(new ShellCommand { Id = Constants.TreeCommands.Rename, Title = "Rename…", DefaultGesture = "F2", ExecuteAsync = _ => Task.CompletedTask });
		return commands;
	}

	/// <summary>The window's view model over the fake client and an in-memory store.</summary>
	public static SettingsViewModel NewSettings(
		FakeTessynClient client, IClientSettingsStore? store = null, IShellCommands? commands = null, FakeDialogPresenter? presenter = null,
		IMcpServerEditorPresenter? editor = null, IFolderPicker? picker = null, IPreviewSettingsService? previewSettings = null)
	{
		store ??= NewStore();
		commands ??= NewCommands(store);
		var ui = new ImmediateUiDispatcher();
		var dialogs = new DialogService(presenter ?? new FakeDialogPresenter(), ui);
		var profiles = new ProfileViewModel(dialogs, ui, NullLogger<ProfileViewModel>.Instance);
		return new SettingsViewModel(
			client, store, commands, dialogs, picker ?? new ScriptedFolderPicker([], NullLogger<ScriptedFolderPicker>.Instance),
			editor ?? new ScriptedMcpEditorPresenter(), profiles, ui, NullLoggerFactory.Instance, previewSettings: previewSettings);
	}
}
