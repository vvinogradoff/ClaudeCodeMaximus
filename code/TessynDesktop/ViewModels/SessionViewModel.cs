using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;
using Serilog;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Coordinates a single session's UI surface, composing the focused collaborators that own each
/// domain: <see cref="SessionContentViewModel"/> (message log, file/JSONL watching),
/// <see cref="ModelSelectionController"/> (model/effort selection),
/// <see cref="ProfileController"/> (profile selection and lifecycle),
/// <see cref="TurnController"/> (sending, streaming, mid-run corrections, compaction), and
/// <see cref="SessionPromptComposer"/> (instruction/context/compaction prompt text).
/// Every property below that used to be implemented directly on this class is now a thin
/// passthrough to one of these collaborators, forwarding their PropertyChanged notifications —
/// this keeps every existing AXAML binding and external call site (MainWindowViewModel,
/// SessionView.axaml.cs) working unchanged (Stage 7 decomposition of the former 2551-LOC class).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionViewModel : ViewModelBase, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<SessionViewModel>();

	private readonly SessionNodeViewModel _node;
	private readonly ISessionFileService _fileService;
	private readonly IDraftService _draftService;
	private readonly IAppSettingsService _appSettings;
	private readonly IDaemonClient _daemonClient;
	private readonly DirectoryNodeModel? _directoryModel;

	private readonly SessionContentViewModel _content;
	private readonly ModelSelectionController _models;
	private readonly ProfileController _profiles;
	private readonly TurnController _turn;

	private string _name;
	private string _inputText = string.Empty;
	private DispatcherTimer? _draftDebounceTimer;
	private bool _isCommandBarVisible;
	private string _projectDirectory = string.Empty;
	private string _treePath = string.Empty;
	private bool _hasActiveSchedule;
	private string _scheduleTooltip = string.Empty;

	public string Name
	{
		get => _name;
		private set => this.RaiseAndSetIfChanged(ref _name, value);
	}

	/// <summary>Display label of the project directory this session belongs to.</summary>
	public string ProjectDirectory
	{
		get => _projectDirectory;
		set
		{
			this.RaiseAndSetIfChanged(ref _projectDirectory, value);
			this.RaisePropertyChanged(nameof(LocationDisplay));
		}
	}

	/// <summary>Group path within the tree (e.g. "GroupA / SubGroup"), empty if at directory root.</summary>
	public string TreePath
	{
		get => _treePath;
		set
		{
			this.RaiseAndSetIfChanged(ref _treePath, value);
			this.RaisePropertyChanged(nameof(LocationDisplay));
		}
	}

	/// <summary>Combined location line: directory + tree path for display in session header.</summary>
	public string LocationDisplay
	{
		get
		{
			if (string.IsNullOrEmpty(_treePath))
				return _projectDirectory;
			return $"{_projectDirectory} / {_treePath}";
		}
	}

	public string InputText
	{
		get => _inputText;
		set
		{
			this.RaiseAndSetIfChanged(ref _inputText, value);
			var hasDraft = !string.IsNullOrWhiteSpace(value);
			_node.HasDraftText = hasDraft;
			_node.LastDraftEditTimestamp = hasDraft ? DateTimeOffset.UtcNow : null;
			SaveDraft(value);
		}
	}

	public bool IsBusy => _turn.IsBusy;

	/// <summary>True while the session's node has an orchestrated turn running (set by SessionTurnService).</summary>
	public bool IsNodeRunning => _turn.IsNodeRunning;

	/// <summary>True when the Stop button should be shown: a local send is in flight, or the node has an active orchestrated turn.</summary>
	public bool CanStop => _turn.CanStop;

	/// <summary>True when this session has at least one active schedule targeting its NodeId.</summary>
	public bool HasActiveSchedule
	{
		get => _hasActiveSchedule;
		private set => this.RaiseAndSetIfChanged(ref _hasActiveSchedule, value);
	}

	/// <summary>Tooltip text describing the active schedule(s) for this session.</summary>
	public string ScheduleTooltip
	{
		get => _scheduleTooltip;
		private set => this.RaiseAndSetIfChanged(ref _scheduleTooltip, value);
	}

	/// <summary>True when this session is being actively used in an external terminal.</summary>
	public bool IsExternallyActive => _node.IsExternallyActive;

	public string ThinkingDuration => _turn.ThinkingDuration;

	public bool IsMarkdownMode
	{
		get => _content.IsMarkdownMode;
		set => _content.IsMarkdownMode = value;
	}

	/// <summary>When true, displays messages from Claude CLI's JSONL session instead of the TessynDesktop .txt file.</summary>
	public bool IsClaudeSessionView
	{
		get => _content.IsClaudeSessionView;
		set => _content.IsClaudeSessionView = value;
	}

	/// <summary>True when the session has any linked Claude JSONL file(s) that can be viewed.</summary>
	public bool HasClaudeSession => _content.HasClaudeSession;

	/// <summary>
	/// Per-session sticky tri-state toggle (FR.11.3): <c>true</c> = Commit, <c>false</c> =
	/// Do-not-commit, <c>null</c> = No preference. Persisted in appsettings.json.
	/// </summary>
	public bool? IsAutoCommit
	{
		get => _turn.IsAutoCommit;
		set => _turn.IsAutoCommit = value;
	}

	/// <summary>One-shot toggle (FR.11.4). Auto-resets after prompt sent.</summary>
	public bool IsNewBranch
	{
		get => _turn.IsNewBranch;
		set => _turn.IsNewBranch = value;
	}

	/// <summary>Per-session sticky toggle (FR.11.5). Persisted in appsettings.json.</summary>
	public bool IsAutoDocument
	{
		get => _turn.IsAutoDocument;
		set => _turn.IsAutoDocument = value;
	}

	/// <summary>One-shot toggle (FR.11.6). Auto-resets after compaction completes.</summary>
	public bool IsAutoCompact
	{
		get => _turn.IsAutoCompact;
		set => _turn.IsAutoCompact = value;
	}

	/// <summary>True when the session has a live ClaudeSessionId that can be cleared.</summary>
	public bool CanClear => _turn.CanClear;

	public double AssistantFontSize
	{
		get => _appSettings.Settings.AssistantFontSize;
		set
		{
			_appSettings.Settings.AssistantFontSize = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	public double AssistantMarkdownFontSize
	{
		get => _appSettings.Settings.AssistantMarkdownFontSize;
		set
		{
			_appSettings.Settings.AssistantMarkdownFontSize = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	public double UserFontSize
	{
		get => _appSettings.Settings.UserFontSize;
		set
		{
			_appSettings.Settings.UserFontSize = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	public double InputFontSize
	{
		get => _appSettings.Settings.InputFontSize;
		set
		{
			_appSettings.Settings.InputFontSize = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	/// <summary>Whether the command bar beneath the input area is visible. Persisted per directory (FR.12.11).</summary>
	public bool IsCommandBarVisible
	{
		get => _isCommandBarVisible;
		set
		{
			this.RaiseAndSetIfChanged(ref _isCommandBarVisible, value);
			if (_directoryModel != null)
			{
				_directoryModel.IsCommandBarVisible = value;
				_appSettings.Save();
			}
		}
	}

	/// <summary>Whether this session is marked as a favorite (FR.9.7). Pins it in the recent-sessions dropdown regardless of recency.</summary>
	public bool IsFavorite
	{
		get => _node.IsFavorite;
		set
		{
			_node.IsFavorite = value;
			this.RaisePropertyChanged();
			_appSettings.Save();
		}
	}

	/// <summary>Display names for the model selector (populated dynamically from IClaudeModelService).</summary>
	public ObservableCollection<string> AvailableModels => _models.AvailableModels;

	/// <summary>Selected model index (0=Default, 1..N=model entries). Persisted per directory as model ID string (FR.12.4).</summary>
	public int SelectedModelIndex
	{
		get => _models.SelectedModelIndex;
		set => _models.SelectedModelIndex = value;
	}

	/// <summary>Returns the true model ID for --model, or null if Default is selected.</summary>
	public string? SelectedModelId => _models.SelectedModelId;

	/// <summary>
	/// Returns the Ollama base URL when the selected model is a local Ollama model, or null for Anthropic models.
	/// Used to set ANTHROPIC_BASE_URL env var on claude CLI spawns (FR.12.14).
	/// </summary>
	public string? SelectedLocalBaseUrl => _models.SelectedLocalBaseUrl;

	/// <summary>Display names for the effort selector.</summary>
	public static string[] AvailableEfforts => ModelSelectionController.AvailableEfforts;

	/// <summary>Selected effort index. 0=Default. Persisted per directory.</summary>
	public int SelectedEffortIndex
	{
		get => _models.SelectedEffortIndex;
		set => _models.SelectedEffortIndex = value;
	}

	/// <summary>Returns the effort value for --effort flag, or null if Default is selected.</summary>
	public string? SelectedEffort => _models.SelectedEffort;

	/// <summary>Experimental per-session toggle: append <c>--chrome</c> to the claude spawn (browser control).</summary>
	public bool IsChromeEnabled
	{
		get => _models.IsChromeEnabled;
		set => _models.IsChromeEnabled = value;
	}

	/// <summary>Streaming Session Mode toggle (FR.19.1, FR.19.6). Turning it off while a process is alive raises <see cref="StreamingDisableConfirmationRequested"/> instead of applying immediately.</summary>
	public bool IsStreamingMode
	{
		get => _turn.IsStreamingMode;
		set => _turn.IsStreamingMode = value;
	}

	/// <summary>True when a Streaming Session Mode process is alive for this session (FR.19.3) — drives the locked Model/Profile/Effort selectors (FR.19.8).</summary>
	public bool IsStreamingProcessAlive => _turn.IsStreamingProcessAlive;

	/// <summary>Raised when the user tries to turn Streaming Session Mode off while its process is alive, requesting a confirmation dialog from the View (FR.19.7).</summary>
	public event Action? StreamingDisableConfirmationRequested
	{
		add => _turn.StreamingDisableConfirmationRequested += value;
		remove => _turn.StreamingDisableConfirmationRequested -= value;
	}

	/// <summary>Called by the View after the user confirms turning Streaming Session Mode off (FR.19.7).</summary>
	public void ConfirmStreamingModeOff() => _turn.ConfirmStreamingModeOff();

	/// <summary>Entries for the profile selector (Default, stored profiles, "New..."). Rebuilt when profiles change.</summary>
	public ObservableCollection<ProfileListItemModel> AvailableProfiles => _profiles.AvailableProfiles;

	/// <summary>Selected profile index. 0=Default, 1..N=stored profiles, last="New...". Persisted per directory (FR.12.8).</summary>
	public int SelectedProfileIndex
	{
		get => _profiles.SelectedProfileIndex;
		set => _profiles.SelectedProfileIndex = value;
	}

	/// <summary>Returns the CLAUDE_CONFIG_DIR path for the selected profile, or null if Default is selected.</summary>
	public string? SelectedProfileConfigDir => _profiles.SelectedProfileConfigDir;

	/// <summary>True when the last request failed with an "Invalid authentication credentials" error (FR.12.16). Drives Re-authenticate/Remove button visibility.</summary>
	public bool HasAuthError => _profiles.HasAuthError;

	/// <summary>True when the Remove button should be shown: an auth error is active and a non-Default profile is selected (FR.12.18).</summary>
	public bool CanRemoveProfile => _profiles.CanRemoveProfile;

	/// <summary>True when a stored (non-Default, non-"New...") profile is currently selected. Drives Rename/Delete button visibility (FR.12.19), independent of auth-error state.</summary>
	public bool CanManageSelectedProfile => _profiles.CanManageSelectedProfile;

	/// <summary>Persisted vertical scroll offset for the message area.</summary>
	public double ScrollOffset
	{
		get => _node.Model.ScrollOffset;
		set => _node.Model.ScrollOffset = value;
	}

	public ObservableCollection<MessageEntryViewModel> Messages => _content.Messages;

	public ReactiveCommand<Unit, Unit> SendCommand { get; }
	public ReactiveCommand<Unit, Unit> ToggleMarkdownCommand { get; }
	public ReactiveCommand<Unit, Unit> StopCommand { get; }
	public ReactiveCommand<Unit, Unit> CancelScheduleCommand { get; }
	public ReactiveCommand<Unit, Unit> ReauthenticateCommand { get; }
	public ReactiveCommand<Unit, Unit> RemoveProfileCommand { get; }
	public AutocompleteViewModel AutocompleteVm { get; }
	public OutputSearchViewModel OutputSearchVm { get; }
	public string WorkingDirectory => _node.Model.WorkingDirectory;

	/// <summary>Raised when the user clicks the schedule button, requesting a cancel confirmation dialog from the View.</summary>
	public event Action? CancelScheduleRequested;

	public SessionViewModel(
		SessionNodeViewModel node,
		ISessionFileService fileService,
		IClaudeProcessManager processManager,
		IClaudeStreamingSessionManager streamingManager,
		IAppSettingsService appSettings,
		IDraftService draftService,
		ICodeIndexService codeIndexService,
		IClaudeProfileService profileService,
		IClaudeSessionImportService importService,
		IClaudeModelService modelService,
		IAgentMcpServer mcpServer,
		IDaemonClient daemonClient,
		IClaudeSessionStatusService sessionStatus)
	{
		_node         = node;
		_fileService  = fileService;
		_draftService = draftService;
		_appSettings  = appSettings;
		_daemonClient = daemonClient;
		_name         = node.Name;

		// Find the parent DirectoryNodeModel for per-directory settings (FR.12.4, FR.12.8, FR.12.11)
		_directoryModel = appSettings.Settings.Tree
			.FirstOrDefault(d => string.Equals(d.Path, node.Model.WorkingDirectory, StringComparison.OrdinalIgnoreCase));

		// TurnController isn't constructed until after SessionContentViewModel (it needs the
		// content log to append to), but the content log needs to ask "is a turn in flight" to
		// gate its file watchers. The lambda closes over this local, assigned once _turn exists.
		TurnController? turn = null;
		_content = new SessionContentViewModel(fileService, importService, node, () => turn?.IsBusy ?? false);
		_models = new ModelSelectionController(modelService, appSettings, _directoryModel);
		_profiles = new ProfileController(profileService, appSettings, _directoryModel, _content);
		var composer = new SessionPromptComposer(fileService, appSettings, node);
		_turn = new TurnController(node, fileService, draftService, processManager, streamingManager, appSettings, mcpServer, daemonClient,
			sessionStatus, _content, _models, _profiles, composer);
		turn = _turn;

		_content.PropertyChanged += (_, e) => this.RaisePropertyChanged(e.PropertyName);
		_models.PropertyChanged += (_, e) => this.RaisePropertyChanged(e.PropertyName);
		_profiles.PropertyChanged += (_, e) => this.RaisePropertyChanged(e.PropertyName);
		_turn.PropertyChanged += (_, e) => this.RaisePropertyChanged(e.PropertyName);
		_turn.InputCleared += () => InputText = string.Empty;

		_isCommandBarVisible = _directoryModel?.IsCommandBarVisible ?? false;
		AutocompleteVm = new AutocompleteViewModel(codeIndexService);
		OutputSearchVm = new OutputSearchViewModel(_content.Messages);

		node.WhenAnyValue(x => x.Name).Subscribe(n => Name = n);
		node.WhenAnyValue(x => x.IsExternallyActive).Subscribe(_ => this.RaisePropertyChanged(nameof(IsExternallyActive)));

		SendCommand           = ReactiveCommand.Create(() => { _ = _turn.SendAsync(InputText); });
		ToggleMarkdownCommand = ReactiveCommand.Create(() => { IsMarkdownMode = !IsMarkdownMode; });
		StopCommand           = ReactiveCommand.Create(_turn.Stop);
		CancelScheduleCommand = ReactiveCommand.Create(() => CancelScheduleRequested?.Invoke());
		ReauthenticateCommand = ReactiveCommand.Create(() => { _ = _profiles.HandleReauthenticateAsync(); });
		RemoveProfileCommand  = ReactiveCommand.Create(_profiles.HandleRemoveProfile);

		_daemonClient.ScheduleChanged += OnScheduleChanged;
		RefreshScheduleState();

		// Start background indexing for this session's working directory
		if (!string.IsNullOrWhiteSpace(WorkingDirectory))
			_ = codeIndexService.GetOrCreateIndexAsync(WorkingDirectory);

		_log.Debug("SessionViewModel created: ExternalId={ExternalId}", _node.ExternalId);
	}

	private void OnScheduleChanged(object? sender, EventArgs e) =>
		Dispatcher.UIThread.Post(RefreshScheduleState);

	/// <summary>Refreshes HasActiveSchedule and ScheduleTooltip from the scheduler's current state for this node.</summary>
	private void RefreshScheduleState()
	{
		var schedules = _daemonClient.GetSchedules(_node.Model.NodeId);
		HasActiveSchedule = schedules.Count > 0;
		ScheduleTooltip = HasActiveSchedule
			? string.Join("\n\n", schedules.Select(FormatScheduleTooltip))
			: string.Empty;
	}

	private static string FormatScheduleTooltip(ScheduleModel s)
	{
		var timing = s.Kind switch
		{
			ScheduleKind.Cron => $"Repeating (cron: {s.CronExpression})",
			_ => s.FireAtUtc.HasValue
				? $"One-time at {s.FireAtUtc.Value.LocalDateTime:yyyy-MM-dd HH:mm}"
				: "One-time",
		};

		var lines = new List<string> { timing };
		if (!string.IsNullOrEmpty(s.Note))
			lines.Add($"Note: \"{s.Note}\"");
		if (s.FireAtUtc.HasValue)
		{
			var remaining = s.FireAtUtc.Value - DateTimeOffset.UtcNow;
			lines.Add(remaining > TimeSpan.Zero
				? $"Next fire: in {FormatTimeSpan(remaining)}"
				: "Next fire: due now");
		}
		return string.Join("\n", lines);
	}

	private static string FormatTimeSpan(TimeSpan ts)
	{
		if (ts.TotalDays >= 1) return $"{ts.TotalDays:F1} days";
		if (ts.TotalHours >= 1) return $"{ts.TotalHours:F1} hours";
		return $"{Math.Max(1, (int)ts.TotalMinutes)} minutes";
	}

	/// <summary>Cancels all schedules currently targeting this session's node.</summary>
	public void CancelAllSchedules()
	{
		foreach (var schedule in _daemonClient.GetSchedules(_node.Model.NodeId))
			_daemonClient.RemoveSchedule(schedule.ScheduleId);
	}

	public void LoadFromFile()
	{
		_content.LoadMessagesFromFile();

		var draft = _draftService.LoadDraft(_node.FileName);
		if (draft is not null)
		{
			_inputText = draft;
			_node.HasDraftText = !string.IsNullOrWhiteSpace(draft);
			if (_node.HasDraftText)
				_node.LastDraftEditTimestamp = _draftService.GetDraftTimestamp(_node.FileName);
		}
	}

	public void Dispose()
	{
		_daemonClient.ScheduleChanged -= OnScheduleChanged;
		_models.Dispose();
		_content.Dispose();
		_turn.Dispose();
	}

	private void SaveDraft(string text)
	{
		_draftDebounceTimer?.Stop();
		_draftDebounceTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(Constants.DraftDebounceMilliseconds)
		};
		_draftDebounceTimer.Tick += (_, _) =>
		{
			_draftDebounceTimer?.Stop();
			_draftDebounceTimer = null;

			if (string.IsNullOrWhiteSpace(text))
				_draftService.DeleteDraft(_node.FileName);
			else
				_draftService.SaveDraft(_node.FileName, text);
		};
		_draftDebounceTimer.Start();
	}

	/// <summary>
	/// Reads the session file and restores the last-used profile, model, and effort to the UI dropdowns.
	/// Sets backing fields directly (no directory-model write or settings save) so that switching
	/// sessions shows each session's own history rather than the shared per-directory values.
	/// </summary>
	public void RestoreLastUsedSettings()
	{
		try
		{
			var entries = _fileService.ReadEntries(_node.FileName).ToList();

			// --- Profile: last user entry that recorded a profile name ---
			var lastUserWithProfile = entries.LastOrDefault(
				e => e.Role == Constants.SessionFile.RoleUser && e.ProfileName != null);
			_profiles.RestoreFromEntry(lastUserWithProfile?.ProfileName);

			// --- Model + Effort: last assistant entry that recorded a model ID ---
			var lastAssistantWithModel = entries.LastOrDefault(
				e => e.Role == Constants.SessionFile.RoleAssistant && e.ModelId != null);
			_models.RestoreFromEntry(lastAssistantWithModel?.ModelId, lastAssistantWithModel?.Effort);
		}
		catch (Exception ex)
		{
			_log.Debug("RestoreLastUsedSettings: {Error}", ex.Message);
		}
	}

	/// <summary>Resolves the default profile email on first load (fire-and-forget).</summary>
	internal void ResolveDefaultProfileEmail() => _profiles.ResolveDefaultProfileEmail();

	/// <summary>Renames a stored profile's display name. Context-menu action on the Profile dropdown; no-op for the "Default"/"New..." entries.</summary>
	public void RenameProfile(ProfileListItemModel item, string newName) => _profiles.RenameProfile(item, newName);

	/// <summary>
	/// Removes a stored profile from the app's list and renames its config directory with a "_rem" suffix (FR.12.18).
	/// Context-menu action on the Profile dropdown; no-op for the "Default"/"New..." entries.
	/// </summary>
	public void DeleteProfile(ProfileListItemModel item) => _profiles.DeleteProfile(item);

	/// <summary>
	/// Immediately detaches the live Claude JSONL session (FR.11.7, FR.11.8).
	/// Moves ClaudeSessionId to PriorClaudeSessionIds so the JSONL is still viewable,
	/// but the next prompt will feed the text session as context instead of --resume.
	/// Thread-safe; posts UI updates to the dispatcher.
	/// </summary>
	public void DetachSession(string displayMessage = "[Session detached — next prompt will use text session as context]") =>
		_turn.DetachSession(displayMessage);
}
