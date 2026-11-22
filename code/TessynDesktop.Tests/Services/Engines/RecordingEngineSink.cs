using System;
using System.Collections.Generic;
using TessynDesktop.Models.Engines;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Engines;

/// <summary>Records every neutral engine call as one readable line, in order.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingEngineSink : IEngineEventSink
{
	private readonly List<string> _calls = [];

	public IReadOnlyList<string> Calls
	{
		get { lock (_calls) return _calls.ToArray(); }
	}

	public List<EngineTurnEnd> Ends { get; } = [];

	public void AssistantText(EngineAssistantText message) => Add($"AssistantText({message.Text}, limit={message.IsLimitError})");
	public void Thinking(string text) => Add($"Thinking({text})");
	public void ToolStarted(string toolId, string? name, string? summary) => Add($"ToolStarted({toolId}, {name}, {summary})");
	public void ToolFinished(string toolId, string? summary, bool isError) => Add($"ToolFinished({toolId}, {summary}, error={isError})");
	public void Progress(string text, DateTimeOffset timestamp) => Add($"Progress({text})");
	public void Compacted(DateTimeOffset timestamp) => Add("Compacted");
	public void Error(EngineError error) => Add($"Error({error.Text}, auth={error.IsAuthError}, limit={error.IsLimitError})");
	public void RetryPending(EngineRetryKind kind) => Add($"RetryPending({kind})");
	public void Retrying(EngineRetryKind kind) => Add($"Retrying({kind})");
	public void RetryRunning() => Add("RetryRunning");
	public void PersistentProcessStarted() => Add("PersistentProcessStarted");

	public void TurnEnded(EngineTurnEnd end)
	{
		lock (_calls) Ends.Add(end);
		Add($"TurnEnded(success={end.Success}, error={end.ErrorText}, auth={end.IsAuthError}, limit={end.IsLimitError}, conversation={end.ConversationId}, tokens={end.InputTokens}/{end.OutputTokens}, cost={end.CostUsd})");
	}

	private void Add(string call)
	{
		lock (_calls) _calls.Add(call);
	}
}
