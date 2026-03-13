using System;
using System.Collections.ObjectModel;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ReactiveUI;
using Serilog;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteViewModel : ViewModelBase
{
	private static readonly ILogger _log = Log.ForContext<AutocompleteViewModel>();

	private readonly ICodeIndexService _indexService;
	private bool _isOpen;
	private int _selectedIndex;

	public ObservableCollection<AutocompleteSuggestionModel> Suggestions { get; } = new();

	public bool IsOpen
	{
		get => _isOpen;
		set => this.RaiseAndSetIfChanged(ref _isOpen, value);
	}

	public int SelectedIndex
	{
		get => _selectedIndex;
		set => this.RaiseAndSetIfChanged(ref _selectedIndex, value);
	}

	public AutocompleteViewModel(ICodeIndexService indexService)
	{
		_indexService = indexService;
	}

	public void UpdateSuggestions(string workingDirectory, AutocompleteTriggerModel trigger)
	{
		if (trigger.Mode == AutocompleteMode.None || string.IsNullOrEmpty(trigger.Query))
		{
			Dismiss();
			return;
		}

		Suggestions.Clear();

		if (trigger.Mode == AutocompleteMode.File)
		{
			var files = _indexService.SearchFiles(workingDirectory, trigger.Query, Constants.CodeIndex.MaxSuggestions);
			foreach (var file in files)
			{
				var matchStart = FindMatchIndex(file.FileName, trigger.Query);
				Suggestions.Add(new AutocompleteSuggestionModel
				{
					DisplayText = file.FileName,
					InsertText = file.RelativePath,
					SecondaryText = file.RelativePath,
					Kind = null,
					MatchStart = matchStart,
					MatchLength = trigger.Query.Length,
					IsFileSuggestion = true
				});
			}
		}
		else
		{
			var symbols = _indexService.SearchSymbols(workingDirectory, trigger.Query, Constants.CodeIndex.MaxSuggestions);
			foreach (var symbol in symbols)
			{
				var matchStart = FindMatchIndex(symbol.FullyQualifiedName, trigger.Query);
				var insertText = string.IsNullOrEmpty(symbol.Namespace)
					? symbol.FullyQualifiedName
					: symbol.Namespace + "." + symbol.FullyQualifiedName;

				Suggestions.Add(new AutocompleteSuggestionModel
				{
					DisplayText = symbol.FullyQualifiedName,
					InsertText = insertText,
					SecondaryText = symbol.Namespace,
					Kind = symbol.Kind,
					MatchStart = matchStart,
					MatchLength = trigger.Query.Length,
					IsFileSuggestion = false
				});
			}
		}

		if (Suggestions.Count > 0)
		{
			SelectedIndex = 0;
			IsOpen = true;
		}
		else
		{
			IsOpen = false;
		}
	}

	public void MoveSelection(int delta)
	{
		if (Suggestions.Count == 0) return;
		var newIndex = SelectedIndex + delta;
		if (newIndex < 0) newIndex = Suggestions.Count - 1;
		else if (newIndex >= Suggestions.Count) newIndex = 0;
		SelectedIndex = newIndex;
	}

	public AutocompleteSuggestionModel? AcceptSelection()
	{
		if (!IsOpen || Suggestions.Count == 0 || SelectedIndex < 0 || SelectedIndex >= Suggestions.Count)
			return null;

		var selected = Suggestions[SelectedIndex];
		Dismiss();
		return selected;
	}

	public void Dismiss()
	{
		IsOpen = false;
		Suggestions.Clear();
		SelectedIndex = 0;
	}

	private static int FindMatchIndex(string text, string query)
	{
		var idx = text.IndexOf(query, StringComparison.Ordinal);
		if (idx >= 0) return idx;
		idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
		return idx >= 0 ? idx : 0;
	}
}
