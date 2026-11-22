using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The selectors of the command bar (W1a): model, effort and profile. The lists come from <c>catalog.get</c>; the selection is restored from the
/// session's <c>lastUsed</c> when it is opened, and what the user picks is remembered per session while the pane lives. Send reads the selection
/// at the moment it is pressed. The model picker is grouped by engine, and the effort and profile lists follow the selected model (FR.33.2, FR.33.3, FR.33.5).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private readonly Dictionary<string, SessionSelection> _picks = new();
	private LastUsedSettingsDto? _lastUsed;
	private string? _lastUsedNodeId;
	private bool _applyingSelection;
	private List<SelectorOptionViewModel> _globalEfforts = [];
	private List<SelectorOptionViewModel> _allProfiles = [];
	private string? _sessionEngine;

	/// <summary>
	/// The next turn runs on the Codex engine (a GPT model is selected, or, before the catalog is known, the session's stored engine is <c>codex</c>):
	/// the Streaming and Tools toggles are hidden (FR.33.1). The selectors stay enabled while idle (FR.33.8).
	/// </summary>
	[ObservableProperty]
	private bool _isCodexSession;

	[ObservableProperty]
	private SelectorOptionViewModel? _selectedModel;

	[ObservableProperty]
	private SelectorOptionViewModel? _selectedEffort;

	[ObservableProperty]
	private SelectorOptionViewModel? _selectedProfile;

	/// <summary>The open session's detail has been applied (its selectors show its own settings); Send waits for it so it can never capture another session's values.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	private bool _isSessionReady;

	public ObservableCollection<SelectorOptionViewModel> Models { get; } = [];

	public ObservableCollection<SelectorOptionViewModel> Efforts { get; } = [];

	public ObservableCollection<SelectorOptionViewModel> Profiles { get; } = [];

	/// <summary>Reads the model, effort and profile lists; a failure leaves them as they were (a send then lets the daemon choose).</summary>
	public async Task LoadCatalogAsync()
	{
		try
		{
			var catalog = await _client.CatalogGetAsync();
			_applyingSelection = true;   // refilling the lists makes the combo boxes report an empty selection: that is no pick
			Fill(Models, ModelPickerList.Models(catalog));
			_globalEfforts = ModelPickerList.GlobalEfforts(catalog);
			_allProfiles = ModelPickerList.Profiles(catalog);
			Fill(Efforts, ModelPickerList.EffortsFor(SelectedModel, _globalEfforts));
			Fill(Profiles, ModelPickerList.ProfilesFor(SelectedModel, _allProfiles));
			_applyingSelection = false;
			ApplyCurrentSelection();
			UpdateEngineLists();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the model, effort and profile lists failed");
		}
		finally
		{
			_applyingSelection = false;
		}
	}

	private static void Fill(ObservableCollection<SelectorOptionViewModel> target, IEnumerable<SelectorOptionViewModel> items)
	{
		target.Clear();
		foreach (var item in items)
			target.Add(item);
	}

	/// <summary>
	/// FR.33.3, FR.33.5: the effort and profile lists follow the selected model (its own efforts; the profiles of its engine). A stored or picked value
	/// the new list lacks shows as Default (or the engine's first profile) and is not sent, but it is not forgotten: it returns with a model that offers it.
	/// </summary>
	private void UpdateEngineLists()
	{
		var model = SelectedModel is { IsSelectable: true } selected ? selected : null;
		var efforts = ModelPickerList.EffortsFor(model, _globalEfforts);
		var profiles = ModelPickerList.ProfilesFor(model, _allProfiles);
		var effortsChanged = !SameIds(Efforts, efforts);
		var profilesChanged = !SameIds(Profiles, profiles);
		if (effortsChanged || profilesChanged)
		{
			var wasApplying = _applyingSelection;
			_applyingSelection = true;
			try
			{
				var picks = NodeId != null && _picks.TryGetValue(NodeId, out var stored) ? stored : new SessionSelection();
				var last = _lastUsedNodeId == NodeId ? _lastUsed : null;
				if (effortsChanged)
				{
					Fill(Efforts, efforts);
					SelectedEffort = Pick(Efforts, picks.EffortId ?? last?.Effort ?? Constants.TurnTexts.DefaultOption);
				}

				if (profilesChanged)
				{
					Fill(Profiles, profiles);
					SelectedProfile = Pick(Profiles, picks.ProfileId ?? last?.ProfileId ?? Constants.TurnTexts.DefaultOption);
				}
			}
			finally
			{
				_applyingSelection = wasApplying;
			}
		}

		IsCodexSession = SelectedModel != null ? ModelPickerList.EngineOf(model) == RemoteProtocol.Engines.Codex : _sessionEngine == RemoteProtocol.Engines.Codex;
	}

	private static bool SameIds(IReadOnlyList<SelectorOptionViewModel> shown, IReadOnlyList<SelectorOptionViewModel> next) =>
		shown.Count == next.Count && shown.Select(o => (o.Id, o.ToolTip)).SequenceEqual(next.Select(o => (o.Id, o.ToolTip)));

	/// <summary>The session's stored engine arrived with its detail or a state push (used while no model is selected yet).</summary>
	private void ApplySessionEngine(string? engine)
	{
		_sessionEngine = engine;
		UpdateEngineLists();
	}

	partial void OnIsCodexSessionChanged(bool value)
	{
		if (Toggles != null)   // set up after the selectors in the constructor
			Toggles.IsStreamingApplicable = !value;
	}

	/// <summary>A session is being opened: nothing of the previous one may show or be sent, and Send waits for this session's detail.</summary>
	private void ResetSelectionForOpen()
	{
		_lastUsed = null;
		_lastUsedNodeId = null;
		_sessionEngine = null;
		IsSessionReady = false;
		_applyingSelection = true;
		try
		{
			SelectedModel = Pick(Models, Constants.TurnTexts.DefaultOption);
			SelectedEffort = Pick(Efforts, Constants.TurnTexts.DefaultOption);
			SelectedProfile = Pick(Profiles, Constants.TurnTexts.DefaultOption);
		}
		finally
		{
			_applyingSelection = false;
		}
	}

	/// <summary>The detail of the open session arrived: its last-used settings are what the selectors show (a pick of the user wins per field).</summary>
	private void ApplySelection(LastUsedSettingsDto lastUsed)
	{
		_lastUsed = lastUsed;
		_lastUsedNodeId = NodeId;
		ApplyCurrentSelection();
	}

	private void ApplyCurrentSelection()
	{
		var id = NodeId;
		if (id == null)
			return;

		var picks = PicksOfOpenSession();
		var last = _lastUsedNodeId == id ? _lastUsed : null;   // never another session's settings
		_applyingSelection = true;
		try
		{
			SelectedModel = Pick(Models, picks.ModelId ?? last?.ModelId ?? Constants.TurnTexts.DefaultOption);
			SelectedEffort = Pick(Efforts, picks.EffortId ?? last?.Effort ?? Constants.TurnTexts.DefaultOption);
			SelectedProfile = Pick(Profiles, picks.ProfileId ?? last?.ProfileId ?? Constants.TurnTexts.DefaultOption);
		}
		finally
		{
			_applyingSelection = false;
		}
	}

	/// <summary>
	/// The option with this id; the "default" option when the id is not in the list (a deleted profile, a model that went away), or the first option
	/// when the list has no "default" (the ChatGPT profiles, FR.33.5). Headers and hints are never picked.
	/// </summary>
	private static SelectorOptionViewModel? Pick(IReadOnlyList<SelectorOptionViewModel> options, string id) =>
		options.FirstOrDefault(o => o.IsSelectable && o.Id == id)
		?? options.FirstOrDefault(o => o.Id == Constants.TurnTexts.DefaultOption)
		?? options.FirstOrDefault(o => o.IsSelectable);

	/// <summary>The fields the user picked for the open session; what a turn sent now carries (null = not picked, the daemon resolves it).</summary>
	private SessionSelection PicksOfOpenSession()
	{
		if (NodeId == null || !_picks.TryGetValue(NodeId, out var picks))
			return new SessionSelection();

		// R2: a pick whose id left the catalog (a deleted profile, a model that went away) is shown as "default" and must not be sent.
		return picks with
		{
			ModelId = StillListed(Models, picks.ModelId),
			EffortId = StillListed(Efforts, picks.EffortId),
			ProfileId = StillListed(Profiles, picks.ProfileId),
		};
	}

	/// <summary>The id when it is in the list (or the list is not loaded, so nothing can be judged); otherwise null.</summary>
	private static string? StillListed(IReadOnlyCollection<SelectorOptionViewModel> options, string? id) =>
		id == null || options.Count == 0 || options.Any(o => o.IsSelectable && o.Id == id) ? id : null;

	private void RememberPick(SelectorOptionViewModel? value, Func<SessionSelection, SessionSelection> change)
	{
		var id = NodeId;
		if (_applyingSelection || id == null || value is not { IsSelectable: true })
			return;

		// The raw picks, not the filtered ones: a pick the current list lacks (an effort the new model does not offer) is kept for when it fits again.
		_picks[id] = change(_picks.TryGetValue(id, out var picks) ? picks : new SessionSelection());
	}

	partial void OnSelectedModelChanged(SelectorOptionViewModel? value)
	{
		RememberPick(value, p => p with { ModelId = value!.Id });
		UpdateEngineLists();
	}

	partial void OnSelectedEffortChanged(SelectorOptionViewModel? value) => RememberPick(value, p => p with { EffortId = value!.Id });

	partial void OnSelectedProfileChanged(SelectorOptionViewModel? value) => RememberPick(value, p => p with { ProfileId = value!.Id });

	/// <summary>Reads the session's header again (after a resync) so its running flag and settings are current.</summary>
	private async Task RefreshDetailAsync(string nodeId)
	{
		try
		{
			var stateEventsAtRequest = _stateEvents;
			var detail = await _client.SessionGetAsync(nodeId);
			_ui.Post(() =>
			{
				if (nodeId == NodeId)
					ApplyDetail(detail, stateEventsAtRequest);
			});
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Re-reading session {NodeId} after a resync failed", nodeId);
		}
	}
}
