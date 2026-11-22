using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The General tab (FR.4.4, FR.12.13, FR.31): proxy, Ollama address, agent tools, code references (FR.4.8), remote file access and the media preview hosts. The session files
/// root, the Claude path and the source codes location are shown but cannot be edited: the daemon does not accept them from a client.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class GeneralSectionViewModel : DaemonSectionViewModel<GeneralSettingsDto, GeneralSettingsPatch>
{
	[ObservableProperty]
	private string _sessionFilesRoot = string.Empty;

	[ObservableProperty]
	private string _claudePath = string.Empty;

	[ObservableProperty]
	private string _sourceCodesLocation = string.Empty;

	[ObservableProperty]
	private string _httpsProxy = string.Empty;

	[ObservableProperty]
	private string _ollamaBaseUrl = string.Empty;

	[ObservableProperty]
	private bool _agentToolsEnabled;

	[ObservableProperty]
	private bool _codeReferencesEnabled;

	[ObservableProperty]
	private FileAccessModeDto _fileAccessMode;

	/// <summary>One host per line; <c>*.example.com</c> allows subdomains.</summary>
	[ObservableProperty]
	private string _mediaHostsText = string.Empty;

	public GeneralSectionViewModel(ITessynClient client, ILogger<GeneralSectionViewModel> logger) : base(client, logger)
	{
	}

	public override string Title => "General";

	public override string SectionName => RemoteProtocol.SettingsApi.GeneralSection;

	public IReadOnlyList<SettingOption<FileAccessModeDto>> FileAccessModes { get; } =
	[
		new(FileAccessModeDto.ReferencedOnly, Constants.Settings.FileAccessReferencedOnly),
		new(FileAccessModeDto.WorkingDirectory, Constants.Settings.FileAccessWorkingDirectory),
	];

	protected override void ReadFrom(GeneralSettingsDto values)
	{
		SessionFilesRoot = values.SessionFilesRoot;
		ClaudePath = values.ClaudePath;
		SourceCodesLocation = values.SourceCodesLocation;
		HttpsProxy = values.HttpsProxy;
		OllamaBaseUrl = values.OllamaBaseUrl;
		AgentToolsEnabled = values.AgentToolsEnabled;
		CodeReferencesEnabled = values.CodeReferencesEnabled;
		FileAccessMode = values.FileAccess.Mode;
		MediaHostsText = string.Join(Environment.NewLine, values.MediaPreviewHosts);
	}

	protected override GeneralSettingsPatch? BuildPatch(GeneralSettingsDto snapshot)
	{
		var proxy = HttpsProxy.Trim();
		var ollama = OllamaBaseUrl.Trim();
		var hosts = ParseHosts();

		var patch = new GeneralSettingsPatch(
			HttpsProxy: proxy != snapshot.HttpsProxy ? proxy : null,
			OllamaBaseUrl: ollama != snapshot.OllamaBaseUrl ? ollama : null,
			AgentToolsEnabled: AgentToolsEnabled != snapshot.AgentToolsEnabled ? AgentToolsEnabled : null,
			CodeReferencesEnabled: CodeReferencesEnabled != snapshot.CodeReferencesEnabled ? CodeReferencesEnabled : null,
			FileAccess: FileAccessMode != snapshot.FileAccess.Mode ? new FileAccessSettingsPatch(Mode: FileAccessMode) : null,
			MediaPreviewHosts: hosts.SequenceEqual(snapshot.MediaPreviewHosts, StringComparer.OrdinalIgnoreCase) ? null : hosts);

		return patch == new GeneralSettingsPatch() ? null : patch;
	}

	protected override void ApplyEdits(GeneralSettingsPatch edits)
	{
		if (edits.HttpsProxy != null)
			HttpsProxy = edits.HttpsProxy;
		if (edits.OllamaBaseUrl != null)
			OllamaBaseUrl = edits.OllamaBaseUrl;
		if (edits.AgentToolsEnabled != null)
			AgentToolsEnabled = edits.AgentToolsEnabled.Value;
		if (edits.CodeReferencesEnabled != null)
			CodeReferencesEnabled = edits.CodeReferencesEnabled.Value;
		if (edits.FileAccess?.Mode != null)
			FileAccessMode = edits.FileAccess.Mode.Value;
		if (edits.MediaPreviewHosts != null)
			MediaHostsText = string.Join(Environment.NewLine, edits.MediaPreviewHosts);
	}

	private List<string> ParseHosts()
	{
		var hosts = MediaHostsText
			.Split(['\r', '\n', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (hosts.Count > Constants.Settings.MaxMediaHosts)
			throw new SettingsInputException(string.Format(Constants.Settings.TooManyHosts, Constants.Settings.MaxMediaHosts));

		return hosts;
	}
}
