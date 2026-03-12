using System;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <summary>A single rendered message in the session view.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MessageEntryViewModel : ViewModelBase
{
	private string _content = string.Empty;

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

	public bool IsUser       => Role == Constants.SessionFile.RoleUser;
	public bool IsAssistant  => Role == Constants.SessionFile.RoleAssistant;
	public bool IsSystem     => Role == Constants.SessionFile.RoleSystem;
	public bool IsCompaction => Role == Constants.SessionFile.RoleCompaction;
}
