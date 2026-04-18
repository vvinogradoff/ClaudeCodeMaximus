using System;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <summary>A single rendered message in the session view.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MessageEntryViewModel : ViewModelBase
{
	private string _content = string.Empty;
	private bool _isCurrentSearchMatch;

	public required string Role { get; init; }

	/// <summary>
	/// Flat text content. Used for: user messages, system messages, simple assistant
	/// messages, and as the searchable aggregate for rich assistant messages.
	/// Kept in sync with Blocks for rich messages so search continues to work.
	/// </summary>
	public string Content
	{
		get => _content;
		set => this.RaiseAndSetIfChanged(ref _content, value);
	}

	public required DateTimeOffset Timestamp { get; init; }

	/// <summary>True for live task_progress / task_started entries that are updated in-place.</summary>
	public bool IsProgress { get; init; }

	/// <summary>
	/// Structured content blocks for rich assistant messages (tool calls, thinking, text).
	/// When non-empty, the UI renders these instead of Content. Content is still maintained
	/// as a searchable aggregate.
	/// </summary>
	public ObservableCollection<MessageBlockViewModel> Blocks { get; } = [];

	/// <summary>True when this message has rich blocks that should be rendered instead of flat Content.</summary>
	public bool HasBlocks => Blocks.Count > 0;

	/// <summary>
	/// Rebuild the flat Content string from blocks, so search keeps working.
	/// Call after block text changes or new blocks are added.
	/// </summary>
	public void SyncContentFromBlocks()
	{
		var parts = Blocks
			.Select(b => b switch
			{
				TextBlockViewModel t => t.Text,
				ThinkingBlockViewModel th => th.Text,
				ToolUseBlockViewModel tu => $"[Tool: {tu.ToolName}] {tu.InputSummary}",
				_ => ""
			})
			.Where(s => !string.IsNullOrEmpty(s));
		_content = string.Join("\n", parts);
		this.RaisePropertyChanged(nameof(Content));
	}

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
