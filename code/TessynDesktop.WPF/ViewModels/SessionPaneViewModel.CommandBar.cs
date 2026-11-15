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
	private readonly Dictionary<string, SessionSelection> _selections = new();
	private LastUsedSettingsDto? _lastUsed;
	private bool _applyingSelection;

	[ObservableProperty]
	private SelectorOptionViewModel? _selectedModel;

	[ObservableProperty]
	private SelectorOptionViewModel? _selectedEffort;

	[ObservableProperty]
	private SelectorOptionViewModel? _selectedProfile;

	public ObservableCollection<SelectorOptionViewModel> Models { get; } = [];

	public ObservableCollection<SelectorOptionViewModel> Efforts { get; } = [];

	public ObservableCollection<SelectorOptionViewModel> Profiles { get; } = [];

	/// <summary>Reads the model, effort and profile lists; a failure leaves them as they were (a send then lets the daemon choose).</summary>
	public async Task LoadCatalogAsync()
	{
		try
		{
			var catalog = await _client.CatalogGetAsync();
			Fill(Models, catalog.Models.Select(m => new SelectorOptionViewModel(m.Id, m.DisplayName)));
			Fill(Efforts, catalog.Efforts.Select(e => new SelectorOptionViewModel(e, Capitalize(e))));
			Fill(Profiles, catalog.Profiles.Select(p => new SelectorOptionViewModel(p.Id, p.DisplayName)));
			ApplyCurrentSelection();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the model, effort and profile lists failed");
		}
	}

	private static void Fill(ObservableCollection<SelectorOptionViewModel> target, IEnumerable<SelectorOptionViewModel> items)
	{
		target.Clear();
		foreach (var item in items)
			target.Add(item);
	}

	private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

	/// <summary>A session was opened or its detail re-read: the remembered pick wins, else the settings it was last used with.</summary>
	private void ApplySelection(LastUsedSettingsDto lastUsed)
	{
		_lastUsed = lastUsed;
		ApplyCurrentSelection();
	}

	private void ApplyCurrentSelection()
	{
		var id = NodeId;
		if (id == null)
			return;

		var wanted = _selections.TryGetValue(id, out var remembered)
			? remembered
			: _lastUsed == null ? null : new SessionSelection(_lastUsed.ModelId, _lastUsed.Effort, _lastUsed.ProfileId);
		if (wanted == null)
			return;

		_applyingSelection = true;
		try
		{
			SelectedModel = Pick(Models, wanted.ModelId);
			SelectedEffort = Pick(Efforts, wanted.EffortId);
			SelectedProfile = Pick(Profiles, wanted.ProfileId);
		}
		finally
		{
			_applyingSelection = false;
		}
	}

	/// <summary>The option with this id; the "default" option when the id is not in the list (a deleted profile, a model that went away).</summary>
	private static SelectorOptionViewModel? Pick(IReadOnlyList<SelectorOptionViewModel> options, string id) =>
		options.FirstOrDefault(o => o.Id == id) ?? options.FirstOrDefault(o => o.Id == Constants.TurnTexts.DefaultOption);

	private void RememberSelection()
	{
		var id = NodeId;
		if (_applyingSelection || id == null)
			return;

		_selections[id] = new SessionSelection(
			SelectedModel?.Id ?? Constants.TurnTexts.DefaultOption,
			SelectedEffort?.Id ?? Constants.TurnTexts.DefaultOption,
			SelectedProfile?.Id ?? Constants.TurnTexts.DefaultOption);
	}

	partial void OnSelectedModelChanged(SelectorOptionViewModel? value) => RememberSelection();

	partial void OnSelectedEffortChanged(SelectorOptionViewModel? value) => RememberSelection();

	partial void OnSelectedProfileChanged(SelectorOptionViewModel? value) => RememberSelection();

	/// <summary>Reads the session's header again (after a resync) so its running flag and settings are current.</summary>
	private async Task RefreshDetailAsync(string nodeId)
	{
		try
		{
			var detail = await _client.SessionGetAsync(nodeId);
			_ui.Post(() =>
			{
				if (nodeId == NodeId)
					ApplyDetail(detail);
			});
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Re-reading session {NodeId} after a resync failed", nodeId);
		}
	}
}
