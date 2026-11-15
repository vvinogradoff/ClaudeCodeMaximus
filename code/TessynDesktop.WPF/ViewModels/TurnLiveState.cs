using System.Text;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One turn of a session as the live row follows it: its state, the prompt it carries and the preview of what it produced so far.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnLiveState
{
	private readonly StringBuilder _text = new();
	private readonly StringBuilder _thinking = new();
	private readonly List<TranscriptLiveTool> _tools = [];

	public TurnLiveState(string turnId)
	{
		TurnId = turnId;
	}

	public string TurnId { get; }

	/// <summary>The prompt of a turn this client sent (for its queued row); null for a turn started elsewhere.</summary>
	public string? Prompt { get; set; }

	public TurnState State { get; set; } = TurnState.Queued;

	/// <summary>Why a retry happens (context or auth).</summary>
	public string? Detail { get; set; }

	/// <summary>When the turn began to run; the page counts its timer from here.</summary>
	public long? StartedAtUnixMs { get; set; }

	public string? Usage { get; set; }

	/// <summary>A jump in the delta sequence: deltas were dropped, so the preview has a hole (the final entry is complete).</summary>
	public bool HasGap { get; private set; }

	private long _lastSeq;

	public string Text => _text.ToString();

	public string Thinking => _thinking.ToString();

	public IReadOnlyList<TranscriptLiveTool> Tools => _tools;

	public bool IsActive => State != TurnState.Queued;

	public void AddDelta(TurnDeltaPayload delta)
	{
		if (_lastSeq != 0 && delta.Seq != _lastSeq + 1)
			HasGap = true;

		_lastSeq = delta.Seq;
		if (delta.Kind == TurnDeltaKind.Thinking)
			Append(_thinking, delta.Text, Constants.TurnLimits.PreviewThinkingChars);
		else
			Append(_text, delta.Text, Constants.TurnLimits.PreviewTextChars);
	}

	public void ApplyTool(TurnToolPayload tool)
	{
		var index = _tools.FindIndex(t => t.Id == tool.ToolUseId);
		if (tool.Status == TurnToolStatus.Started)
		{
			if (index < 0)
				_tools.Add(new TranscriptLiveTool(tool.ToolUseId, tool.Name, tool.Summary, Done: false, IsError: false));

			if (_tools.Count > Constants.TurnLimits.PreviewTools)
				_tools.RemoveRange(0, _tools.Count - Constants.TurnLimits.PreviewTools);

			return;
		}

		if (index >= 0)
			_tools[index] = _tools[index] with { Done = true, IsError = tool.IsError == true };
	}

	private static void Append(StringBuilder builder, string text, int max)
	{
		builder.Append(text);
		if (builder.Length > max)
			builder.Remove(0, builder.Length - max);
	}
}
