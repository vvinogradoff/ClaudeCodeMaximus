using System;
using System.Collections.ObjectModel;
using System.IO;
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

		switch (trigger.Mode)
		{
			case AutocompleteMode.File:
				PopulateFileSuggestions(workingDirectory, trigger.Query);
				break;
			case AutocompleteMode.Symbol:
				PopulateSymbolSuggestions(workingDirectory, trigger.Query);
				break;
			case AutocompleteMode.Path:
				PopulatePathSuggestions(trigger.Query);
				break;
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

	private void PopulateFileSuggestions(string workingDirectory, string query)
	{
		var files = _indexService.SearchFiles(workingDirectory, query, Constants.CodeIndex.MaxSuggestions);
		foreach (var file in files)
		{
			var matchStart = FindMatchIndex(file.FileName, query);
			Suggestions.Add(new AutocompleteSuggestionModel
			{
				DisplayText = file.FileName,
				InsertText = file.RelativePath,
				SecondaryText = file.RelativePath,
				Kind = null,
				MatchStart = matchStart,
				MatchLength = query.Length,
				IsFileSuggestion = true
			});
		}
	}

	private void PopulateSymbolSuggestions(string workingDirectory, string query)
	{
		var symbols = _indexService.SearchSymbols(workingDirectory, query, Constants.CodeIndex.MaxSuggestions);
		foreach (var symbol in symbols)
		{
			var matchStart = FindMatchIndex(symbol.FullyQualifiedName, query);
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
				MatchLength = query.Length,
				IsFileSuggestion = false
			});
		}
	}

	private void PopulatePathSuggestions(string pathQuery)
	{
		try
		{
			// Split into directory part and partial name filter
			var lastSep = pathQuery.LastIndexOfAny(new[] { '\\', '/' });
			string directoryPath;
			string nameFilter;

			if (lastSep >= 0)
			{
				directoryPath = pathQuery.Substring(0, lastSep + 1);
				nameFilter = pathQuery.Substring(lastSep + 1);
			}
			else
			{
				return; // No separator found — not a valid path yet
			}

			if (!Directory.Exists(directoryPath))
				return;

			var count = 0;

			// Directories first
			foreach (var dir in Directory.EnumerateDirectories(directoryPath))
			{
				if (count >= Constants.CodeIndex.MaxSuggestions) break;
				var dirName = Path.GetFileName(dir);
				if (!string.IsNullOrEmpty(nameFilter)
					&& !dirName.StartsWith(nameFilter, StringComparison.OrdinalIgnoreCase))
					continue;

				Suggestions.Add(new AutocompleteSuggestionModel
				{
					DisplayText = dirName,
					InsertText = dir + "\\",
					SecondaryText = "<dir>",
					Kind = null,
					MatchStart = 0,
					MatchLength = nameFilter.Length,
					IsFileSuggestion = true
				});
				count++;
			}

			// Then files
			foreach (var file in Directory.EnumerateFiles(directoryPath))
			{
				if (count >= Constants.CodeIndex.MaxSuggestions) break;
				var fileName = Path.GetFileName(file);
				if (!string.IsNullOrEmpty(nameFilter)
					&& !fileName.StartsWith(nameFilter, StringComparison.OrdinalIgnoreCase))
					continue;

				Suggestions.Add(new AutocompleteSuggestionModel
				{
					DisplayText = fileName,
					InsertText = file,
					SecondaryText = Path.GetExtension(file),
					Kind = null,
					MatchStart = 0,
					MatchLength = nameFilter.Length,
					IsFileSuggestion = true
				});
				count++;
			}
		}
		catch (UnauthorizedAccessException)
		{
			// Skip inaccessible directories
		}
		catch (DirectoryNotFoundException)
		{
			// Path doesn't exist yet
		}
		catch (IOException ex)
		{
			_log.Debug(ex, "Error enumerating path {Path}", pathQuery);
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
