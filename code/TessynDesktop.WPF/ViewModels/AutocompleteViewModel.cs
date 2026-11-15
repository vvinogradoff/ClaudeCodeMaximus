using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The input after a suggestion was accepted: the new text and where the caret goes.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AutocompleteInsertion(string Text, int Caret, bool ContinuesPath);

/// <summary>
/// The autocomplete popup of the prompt input (W1b): <c>#symbol</c>, <c>##file</c> and drive paths. The input and caret are handed in; after a short
/// quiet the daemon is asked (<c>autocomplete.suggest</c>), and an answer that arrives for text that is no longer there is dropped. While the daemon
/// builds the code index of the session's directory (<c>indexReady: false</c>) it asks again after a pause, so the first <c>#</c> in a project never
/// looks like "no results". Keyboard handling lives in the view; this class only moves the selection and computes the insertion.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class AutocompleteViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly IUiDispatcher _ui;
	private readonly ILogger _logger;
	private readonly Func<string?> _nodeId;

	private CancellationTokenSource? _cts;
	private int _version;
	private string _requestedText = string.Empty;
	private int _requestedCaret = -1;

	private string _shownText = string.Empty;
	private AutocompleteSuggestResult? _shown;

	[ObservableProperty]
	private bool _isOpen;

	[ObservableProperty]
	private int _selectedIndex = -1;

	/// <summary>The daemon is still building the code index: suggestions follow when it is ready.</summary>
	[ObservableProperty]
	private bool _isIndexBuilding;

	public AutocompleteViewModel(ITessynClient client, IUiDispatcher ui, ILogger logger, Func<string?> nodeId)
	{
		_client = client;
		_ui = ui;
		_logger = logger;
		_nodeId = nodeId;
	}

	/// <summary>Quiet needed after the last change before the daemon is asked; zero asks at once (tests).</summary>
	public TimeSpan Debounce { get; set; } = Constants.ComposerLimits.AutocompleteDebounce;

	/// <summary>Pause before asking again while the code index is built.</summary>
	public TimeSpan IndexRetry { get; set; } = Constants.ComposerLimits.AutocompleteIndexRetry;

	public ObservableCollection<AutocompleteItemViewModel> Items { get; } = [];

	/// <summary>The text the shown suggestions were computed for.</summary>
	public string ShownText => _shownText;

	/// <summary>The input or its caret changed: asks for suggestions when the text could contain a trigger, otherwise closes.</summary>
	public void Update(string text, int caret)
	{
		if (text == _requestedText && caret == _requestedCaret)
			return;

		_requestedText = text;
		_requestedCaret = caret;
		Cancel();
		var nodeId = _nodeId();
		if (nodeId == null || !AutocompleteTrigger.MayTrigger(text, caret))
		{
			Reset();
			return;
		}

		var version = ++_version;
		var cts = _cts = new CancellationTokenSource();
		_ = RunAsync(nodeId, text, caret, version, cts.Token);
	}

	/// <summary>Hides the popup and forgets what was asked (the next change asks again).</summary>
	public void Close()
	{
		Cancel();
		_requestedText = string.Empty;
		_requestedCaret = -1;
		Reset();
	}

	/// <summary>Moves the selection by <paramref name="delta"/> rows and wraps at the ends.</summary>
	public void Move(int delta)
	{
		if (Items.Count == 0)
			return;

		SelectedIndex = ((SelectedIndex < 0 ? (delta > 0 ? -1 : 0) : SelectedIndex) + delta + Items.Count) % Items.Count;
	}

	/// <summary>
	/// Replaces the trigger text of <paramref name="currentText"/> by the suggestion at <paramref name="index"/> (the selected one when -1). Null, and the
	/// popup closes, when nothing is selected or the input is no longer the text the suggestions were computed for.
	/// </summary>
	public AutocompleteInsertion? Accept(string currentText, int index = -1)
	{
		var i = index < 0 ? SelectedIndex : index;
		var shown = _shown;
		if (shown == null || i < 0 || i >= Items.Count || currentText != _shownText)
		{
			Close();
			return null;
		}

		var item = Items[i];
		var start = Math.Clamp(shown.TriggerStart, 0, currentText.Length);
		var end = Math.Clamp(shown.TriggerStart + shown.TriggerLength, start, currentText.Length);
		var text = string.Concat(currentText.AsSpan(0, start), item.InsertText, currentText.AsSpan(end));
		Close();
		return new AutocompleteInsertion(text, start + item.InsertText.Length, item.IsDirectory);
	}

	private async Task RunAsync(string nodeId, string text, int caret, int version, CancellationToken ct)
	{
		try
		{
			if (Debounce > TimeSpan.Zero)
				await Task.Delay(Debounce, ct).ConfigureAwait(false);

			for (var attempt = 0; attempt <= Constants.ComposerLimits.AutocompleteIndexRetries; attempt++)
			{
				var result = await _client.AutocompleteSuggestAsync(nodeId, text, caret, ct).ConfigureAwait(false);
				if (ct.IsCancellationRequested)
					return;

				if (result.Mode != RemoteProtocol.Composer.AutocompleteModes.None && !result.IndexReady)
				{
					// Not "no results": the index is being built. Keep the popup as it is, say so, and ask again.
					_ui.Post(() =>
					{
						if (version == _version)
							IsIndexBuilding = true;
					});
					await Task.Delay(IndexRetry, ct).ConfigureAwait(false);
					continue;
				}

				_ui.Post(() =>
				{
					if (version == _version)
						Show(text, result);
				});
				return;
			}

			_ui.Post(() =>
			{
				if (version == _version)
					Reset();
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogInformation("Autocomplete failed: {Message}", ex.Message);
			_ui.Post(() =>
			{
				if (version == _version)
					Reset();
			});
		}
	}

	private void Show(string text, AutocompleteSuggestResult result)
	{
		IsIndexBuilding = false;
		var items = result.Mode == RemoteProtocol.Composer.AutocompleteModes.None
			? []
			: result.Items.Take(Constants.ComposerLimits.MaxSuggestionsShown).Select(i => new AutocompleteItemViewModel(i)).ToList();
		Items.Clear();
		foreach (var item in items)
			Items.Add(item);

		_shownText = text;
		_shown = items.Count == 0 ? null : result;
		SelectedIndex = items.Count == 0 ? -1 : 0;
		IsOpen = items.Count > 0;
	}

	private void Reset()
	{
		_version++;
		IsIndexBuilding = false;
		IsOpen = false;
		SelectedIndex = -1;
		Items.Clear();
		_shown = null;
		_shownText = string.Empty;
	}

	private void Cancel()
	{
		var cts = _cts;
		_cts = null;
		cts?.Cancel();
		cts?.Dispose();
	}

	public void Dispose() => Cancel();
}
