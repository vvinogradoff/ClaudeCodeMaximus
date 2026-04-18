using System;
using ClaudeMaximus.Models;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <summary>
/// ViewModel for a single row in the session import picker.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportSessionItemViewModel : ViewModelBase
{
	private string _displayTitle;
	private bool _isSelected;
	private bool _isTitlePending;

	public ClaudeSessionSummaryModel Summary { get; }

	public string SessionId => Summary.SessionId;

	public string DisplayTitle
	{
		get => _displayTitle;
		set => this.RaiseAndSetIfChanged(ref _displayTitle, value);
	}

	public string DateRange
	{
		get
		{
			// Guard against DateTimeOffset.MinValue which can crash LocalDateTime
			// conversion in negative-offset time zones
			if (Summary.Created == DateTimeOffset.MinValue || Summary.LastUsed == DateTimeOffset.MinValue)
				return "(no date)";

			var created = Summary.Created.LocalDateTime;
			var lastUsed = Summary.LastUsed.LocalDateTime;
			if (created.Date == lastUsed.Date)
				return $"{created:yyyy-MM-dd HH:mm} - {lastUsed:HH:mm}";
			return $"{created:yyyy-MM-dd} - {lastUsed:yyyy-MM-dd}";
		}
	}

	public int MessageCount => Summary.MessageCount;

	public string MessageCountText => $"{MessageCount} messages";

	/// <summary>Display label showing which project this session is from (for cross-project results).</summary>
	public string? ProjectLabel { get; }

	/// <summary>Whether this is a cross-project result (from a different project than the current scan source).</summary>
	public bool IsCrossProject => !string.IsNullOrEmpty(Summary.OriginalProjectPath);

	/// <summary>Original project path for resume. Null for same-project sessions.</summary>
	public string? OriginalProjectPath => Summary.OriginalProjectPath;

	public bool IsAlreadyImported { get; }

	public bool IsEmpty => Summary.MessageCount == 0;

	/// <summary>Whether this row can be checked for import.</summary>
	public bool IsSelectable => !IsAlreadyImported && !IsEmpty;

	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			if (!IsSelectable)
				return;
			this.RaiseAndSetIfChanged(ref _isSelected, value);
		}
	}

	/// <summary>Whether a title is still being generated for this session.</summary>
	public bool IsTitlePending
	{
		get => _isTitlePending;
		set => this.RaiseAndSetIfChanged(ref _isTitlePending, value);
	}

	/// <summary>Opacity for greyed-out items.</summary>
	public double RowOpacity => IsSelectable ? 1.0 : 0.45;

	public string StatusText
	{
		get
		{
			if (IsAlreadyImported)
				return "Already imported";
			if (IsEmpty)
				return "Empty session";
			return string.Empty;
		}
	}

	public ImportSessionItemViewModel(ClaudeSessionSummaryModel summary, bool isAlreadyImported)
	{
		Summary = summary;
		IsAlreadyImported = isAlreadyImported;
		_displayTitle = summary.GeneratedTitle
			?? TruncatePrompt(summary.FirstUserPrompt)
			?? "(empty session)";
		_isTitlePending = summary.GeneratedTitle == null && summary.FirstUserPrompt != null;

		// Derive project label from original path (show last directory component)
		if (!string.IsNullOrEmpty(summary.OriginalProjectPath))
		{
			var trimmed = summary.OriginalProjectPath.TrimEnd('/', '\\');
			ProjectLabel = System.IO.Path.GetFileName(trimmed);
		}
	}

	/// <summary>
	/// Updates the display title when a Claude-generated title becomes available.
	/// </summary>
	public void UpdateTitle(string title)
	{
		Summary.GeneratedTitle = title;
		DisplayTitle = title;
		IsTitlePending = false;
	}

	private static string TruncatePrompt(string? prompt)
	{
		if (string.IsNullOrWhiteSpace(prompt))
			return null!;

		// Show first line only, truncated
		var firstLine = prompt.Split('\n')[0].Trim();
		if (firstLine.Length > 80)
			return firstLine[..77] + "...";
		return firstLine;
	}
}
