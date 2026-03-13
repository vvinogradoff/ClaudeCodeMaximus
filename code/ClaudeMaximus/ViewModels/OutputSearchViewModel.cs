using System;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <summary>Drives the output-area search overlay (FR.10).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class OutputSearchViewModel : ViewModelBase
{
	private readonly ObservableCollection<MessageEntryViewModel> _messages;

	private bool _isActive;
	private int _currentIndex = -1;
	private int _totalMatches;
	private int[] _matchIndices = Array.Empty<int>();

	public bool IsActive
	{
		get => _isActive;
		private set => this.RaiseAndSetIfChanged(ref _isActive, value);
	}

	public int CurrentIndex
	{
		get => _currentIndex;
		private set => this.RaiseAndSetIfChanged(ref _currentIndex, value);
	}

	public int TotalMatches
	{
		get => _totalMatches;
		private set => this.RaiseAndSetIfChanged(ref _totalMatches, value);
	}

	public string StatusText
	{
		get
		{
			if (!IsActive)
				return string.Empty;
			if (TotalMatches == 0)
				return "no matches";
			return $"{CurrentIndex + 1} of {TotalMatches} matches";
		}
	}

	public OutputSearchViewModel(ObservableCollection<MessageEntryViewModel> messages)
	{
		_messages = messages;
	}

	/// <summary>
	/// Runs a case-insensitive search across all messages. Returns the message index
	/// of the first match (or -1 if none), so the view can scroll to it.
	/// </summary>
	public int Search(string query)
	{
		if (string.IsNullOrEmpty(query))
		{
			Dismiss();
			return -1;
		}

		_matchIndices = _messages
			.Select((m, i) => (m, i))
			.Where(t => t.m.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
			.Select(t => t.i)
			.ToArray();

		TotalMatches = _matchIndices.Length;
		CurrentIndex = TotalMatches > 0 ? 0 : -1;
		IsActive = true;
		this.RaisePropertyChanged(nameof(StatusText));

		return CurrentMatchMessageIndex;
	}

	/// <summary>Advances to the next match (wraps around). Returns message index or -1.</summary>
	public int NextMatch()
	{
		if (TotalMatches == 0)
			return -1;

		CurrentIndex = (CurrentIndex + 1) % TotalMatches;
		this.RaisePropertyChanged(nameof(StatusText));
		return CurrentMatchMessageIndex;
	}

	/// <summary>Goes to the previous match (wraps around). Returns message index or -1.</summary>
	public int PreviousMatch()
	{
		if (TotalMatches == 0)
			return -1;

		CurrentIndex = (CurrentIndex - 1 + TotalMatches) % TotalMatches;
		this.RaisePropertyChanged(nameof(StatusText));
		return CurrentMatchMessageIndex;
	}

	/// <summary>Hides the overlay. Does not clear the search text (that's the view's job).</summary>
	public void Dismiss()
	{
		IsActive = false;
		CurrentIndex = -1;
		TotalMatches = 0;
		_matchIndices = Array.Empty<int>();
		this.RaisePropertyChanged(nameof(StatusText));
	}

	/// <summary>The index into the Messages collection of the current match, or -1.</summary>
	public int CurrentMatchMessageIndex =>
		CurrentIndex >= 0 && CurrentIndex < _matchIndices.Length
			? _matchIndices[CurrentIndex]
			: -1;
}
