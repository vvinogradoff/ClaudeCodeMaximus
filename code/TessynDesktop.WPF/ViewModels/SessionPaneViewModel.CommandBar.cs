using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The selectors of the command bar (W1a): model, effort and profile. The lists come from <c>catalog.get</c>; the selection is restored from the
/// session's <c>lastUsed</c> when it is opened, and what the user picks is remembered per session while the pane lives. Send reads the selection
/// at the moment it is pressed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private readonly Dictionary<string, SessionSelection> _picks = new();
	private LastUsedSettingsDto? _lastUsed;
	private string? _lastUsedNodeId;
	private bool _applyingSelection;

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
			Fill(Models, catalog.Models.Select(m => new SelectorOptionViewModel(m.Id, m.DisplayName, m.Provider)));
			Fill(Efforts, catalog.Efforts.Select(e => new SelectorOptionViewModel(e, Capitalize(e))));
			Fill(Profiles, catalog.Profiles.Select(p => new SelectorOptionViewModel(p.Id, p.DisplayName)));
			_applyingSelection = false;
			ApplyCurrentSelection();
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

	private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

	/// <summary>A session is being opened: nothing of the previous one may show or be sent, and Send waits for this session's detail.</summary>
	private void ResetSelectionForOpen()
	{
		_lastUsed = null;
		_lastUsedNodeId = null;
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

	/// <summary>The option with this id; the "default" option when the id is not in the list (a deleted profile, a model that went away).</summary>
	private static SelectorOptionViewModel? Pick(IReadOnlyList<SelectorOptionViewModel> options, string id) =>
		options.FirstOrDefault(o => o.Id == id) ?? options.FirstOrDefault(o => o.Id == Constants.TurnTexts.DefaultOption);

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
		id == null || options.Count == 0 || options.Any(o => o.Id == id) ? id : null;

	private void RememberPick(SelectorOptionViewModel? value, Func<SessionSelection, SessionSelection> change)
	{
		var id = NodeId;
		if (_applyingSelection || id == null || value == null)
			return;

		_picks[id] = change(PicksOfOpenSession());
	}

	partial void OnSelectedModelChanged(SelectorOptionViewModel? value) => RememberPick(value, p => p with { ModelId = value!.Id });

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
