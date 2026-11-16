using System;
using ReactiveUI;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

/// <summary>A single rendered message in the session view.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MessageEntryViewModel : ViewModelBase
{
	private string _content = string.Empty;
	private bool _isCurrentSearchMatch;
	private bool _isSelected;
	private readonly InputSourceDisplay _sourceDisplay = new();

	public required string Role { get; init; }

	/// <summary>Mutable so progress messages can be updated in-place.</summary>
	public string Content
	{
		get => _content;
		set => this.RaiseAndSetIfChanged(ref _content, value);
	}

	public required DateTimeOffset Timestamp { get; init; }

	/// <summary>True for live task_progress / task_started entries that are updated in-place.</summary>
	public bool IsProgress { get; init; }

	/// <summary>Profile display name shown next to "You" in user bubbles. Null/empty = not shown.</summary>
	public string? ProfileName { get; init; }

	/// <summary>Model+effort label shown at the top of the first assistant message after each user prompt.</summary>
	public string? ModelLabel { get; set; }

	/// <summary>What produced a user entry (<see cref="EntrySources"/>), resolved on read: "scheduled", "orchestrated", "telegram", "web"; null for one the user typed here.</summary>
	public string? Source { get; init; }

	/// <summary>True for an input a scheduled turn or a supervisor produced (FR.2.6): rendered teal with its own label.</summary>
	public bool IsAutomated => IsUser && _sourceDisplay.IsAutomated(Source);

	/// <summary>The header label of a user entry: "You", SCHEDULED or SUPERVISOR (FR.2.6).</summary>
	public string UserLabel => _sourceDisplay.Label(Source);

	/// <summary>True while this input is the selected one (thin dotted border).</summary>
	public bool IsSelected
	{
		get => _isSelected;
		set => this.RaiseAndSetIfChanged(ref _isSelected, value);
	}

	public bool HasProfileName => !string.IsNullOrEmpty(ProfileName);
	public bool HasModelLabel => !string.IsNullOrEmpty(ModelLabel);

	public string FormattedDate => Timestamp.LocalDateTime.ToString("yyyy-MM-dd");
	public string FormattedTime => Timestamp.LocalDateTime.ToString("HH:mm");

	public bool IsUser       => Role == Constants.SessionFile.RoleUser;
	public bool IsAssistant  => Role == Constants.SessionFile.RoleAssistant;
	public bool IsSystem     => Role == Constants.SessionFile.RoleSystem;
	public bool IsCompaction => Role == Constants.SessionFile.RoleCompaction;

	/// <summary>True when this message is the current search match target (gets orange highlight).</summary>
	public bool IsCurrentSearchMatch
	{
		get => _isCurrentSearchMatch;
		set => this.RaiseAndSetIfChanged(ref _isCurrentSearchMatch, value);
	}
}
