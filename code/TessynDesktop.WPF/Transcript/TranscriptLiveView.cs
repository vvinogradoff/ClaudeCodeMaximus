namespace TessynDesktop.WPF.Transcript;

/// <summary>
/// What the page shows below the last entry while a turn of the session is in flight: the thinking indicator with its timer, the streamed
/// text so far, the tool calls, the turns waiting behind it and a failure. It is a preview; the persisted entries stay the source of truth.
/// </summary>
/// <param name="Status">The state line ("Thinking...", "Resuming session..."), or null.</param>
/// <param name="Text">The assistant text streamed so far (its tail when long).</param>
/// <param name="Thinking">The model's thinking streamed so far (its tail when long).</param>
/// <param name="Tools">The tool calls of the running turn, oldest first.</param>
/// <param name="Queued">The prompts of the turns waiting behind the running one, shortened.</param>
/// <param name="StartedAtUnixMs">When the running turn started (the page counts the timer from it); null when no turn runs.</param>
/// <param name="Error">The error of the last turn, shown until the next send; null when none.</param>
/// <param name="Usage">Tokens and cost of the response, once reported; null before.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TranscriptLiveView(
	string? Status,
	string Text,
	string Thinking,
	IReadOnlyList<TranscriptLiveTool> Tools,
	IReadOnlyList<string> Queued,
	long? StartedAtUnixMs,
	string? Error,
	string? Usage);
