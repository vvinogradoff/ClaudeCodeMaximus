using System;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <summary>A single rendered message in the session view.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MessageEntryViewModel : ViewModelBase
{
	private string _content = string.Empty;
	private bool _isCurrentSearchMatch;

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
