using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Fake claude launcher: emits canned stream events (assistant text, then a result carrying a session id) with a configurable delay and error.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeClaudeProcessManager : IClaudeProcessManager
{
	private static int _sequence;

	private readonly object _gate = new();
	private readonly List<ProcessCall> _calls = new();

	/// <summary>Time every launch takes before it answers; cancellation interrupts it.</summary>
	public TimeSpan Delay { get; set; } = TimeSpan.Zero;

	/// <summary>When set, every launch ends with an error result instead of an answer.</summary>
	public string? ErrorText { get; set; }

	/// <summary>Builds the assistant text from the prompt; the default echoes a counter so replies are distinguishable.</summary>
	public Func<string, string> Reply { get; set; } = _ => "Fake reply " + Interlocked.Increment(ref _sequence);

	public int ActiveProcessCount => 0;

	public IReadOnlyList<ProcessCall> Snapshot()
	{
		lock (_gate)
			return [.. _calls];
	}

	public async Task SendMessageAsync(
		string workingDirectory,
		string claudePath,
		string? sessionId,
		string userMessage,
		Action<ClaudeStreamEvent> onEvent,
		string? model = null,
		string? profileConfigDir = null,
		string? effort = null,
		string? mcpConfigPath = null,
		string? ollamaBaseUrl = null,
		bool disableTools = false,
		bool chrome = false,
		bool disallowNativeScheduling = false,
		LocalToolSpec? localToolSpec = null,
		bool strictMcpConfig = false,
		CancellationToken cancellationToken = default)
	{
		lock (_gate)
			_calls.Add(new ProcessCall(userMessage, model, effort, profileConfigDir, sessionId));

		if (Delay > TimeSpan.Zero)
			await Task.Delay(Delay, cancellationToken);

		cancellationToken.ThrowIfCancellationRequested();

		if (ErrorText is not null)
		{
			onEvent(new ClaudeStreamEvent { Type = "result", Subtype = "error_during_execution", IsError = true, Content = ErrorText });
			return;
		}

		var text = Reply(userMessage);
		onEvent(new ClaudeStreamEvent { Type = "assistant", Content = text });
		onEvent(new ClaudeStreamEvent
		{
			Type = "result",
			Subtype = "success",
			Content = text,
			SessionId = "fake-claude-session-" + Interlocked.Increment(ref _sequence),
			InputTokens = 10,
			OutputTokens = 5,
		});
	}

	public Task<string?> RunPrintModeAsync(string claudePath, string prompt, string? model = null, string? profileConfigDir = null, int timeoutMs = 60000, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<string?>(null);
	}

	public void TerminateAll()
	{
	}
}
