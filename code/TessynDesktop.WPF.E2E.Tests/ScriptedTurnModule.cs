using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// A test-only turn API (<c>turn.send</c>, <c>turn.stop</c>) that stands in for the engine and the fake <c>claude</c> process: it publishes the
/// same <c>turn.*</c> notifications, <c>session.changed</c> and <c>session.appended</c> as the daemon would, and writes the user and assistant
/// entries into the real session file (the session module then serves them). A turn is scripted by its prompt: <c>hold:</c> streams a little and
/// then waits until <see cref="ReleaseHeld"/> or a stop; <c>stream:</c> streams six long chunks at a steady pace; anything else answers quickly.
/// Turns of a session run one at a time, the others wait (queued), as in the engine.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ScriptedTurnModule : IRemoteRpcModule
{
	public const string HoldPrefix = "hold:";
	public const string StreamPrefix = "stream:";
	public const int ChunkChars = 1100;
	public const int ChunkCount = 6;
	public static readonly TimeSpan ChunkDelay = TimeSpan.FromMilliseconds(500);

	private sealed class Turn(string nodeId, string turnId, string text, string? model, string? effort, string? profile)
	{
		public string NodeId { get; } = nodeId;
		public string TurnId { get; } = turnId;
		public string Text { get; } = text;
		public string? Model { get; } = model;
		public string? Effort { get; } = effort;
		public string? Profile { get; } = profile;
		public CancellationTokenSource Cancel { get; } = new();
	}

	private readonly FocusRefsEnv _env;
	private readonly Func<RemoteEventBus> _bus;
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
	private readonly ConcurrentDictionary<string, Turn> _running = new();
	private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _turnCounter;

	public ScriptedTurnModule(FocusRefsEnv env, Func<RemoteEventBus> bus)
	{
		_env = env;
		_bus = bus;
	}

	/// <summary>The sends received, in order: node, text, model, effort, profile.</summary>
	public ConcurrentQueue<(string NodeId, string Text, string? Model, string? Effort, string? Profile)> Sends { get; } = new();

	/// <summary>Chunks of streamed answer published so far (all turns): the tests wait on it instead of sleeping.</summary>
	public int ChunksPublished => Volatile.Read(ref _chunks);

	/// <summary>Assistant entries written to the session file (a stopped turn writes none).</summary>
	public int AnswersAppended => Volatile.Read(ref _answers);

	/// <summary>The turns that ended, with whether each was stopped.</summary>
	public ConcurrentQueue<(string TurnId, bool Cancelled)> Completions { get; } = new();

	private int _chunks;
	private int _answers;

	/// <summary>Lets a turn that waits for <see cref="HoldPrefix"/> finish normally.</summary>
	public void ReleaseHeld() => _held.TrySetResult();

	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add("turn.send", (p, _) => Task.FromResult<object?>(Send(p)), remoteCallable: false);
		registry.AddSync("turn.stop", Stop, remoteCallable: false);
	}

	private TurnSendResult Send(JsonNode? p)
	{
		var nodeId = p!["nodeId"]!.GetValue<string>();
		var text = p["text"]!.GetValue<string>();
		var turn = new Turn(nodeId, "turn-" + Interlocked.Increment(ref _turnCounter), text, p["modelId"]?.GetValue<string>(), p["effort"]?.GetValue<string>(), p["profileId"]?.GetValue<string>());
		Sends.Enqueue((nodeId, text, turn.Model, turn.Effort, turn.Profile));

		var gate = _locks.GetOrAdd(nodeId, _ => new SemaphoreSlim(1, 1));
		var queued = gate.CurrentCount == 0;
		_bus().Publish(RemoteProtocol.TurnStream.StartedEvent, new TurnStartedPayload(nodeId, turn.TurnId, p["clientRequestId"]?.GetValue<string>()));
		if (queued)
			_bus().Publish(RemoteProtocol.TurnStream.StatusEvent, new TurnStatusPayload(nodeId, turn.TurnId, TurnState.Queued));

		_ = Task.Run(() => RunAsync(turn, gate));
		return new TurnSendResult(turn.TurnId, Accepted: true, queued);
	}

	private object? Stop(JsonNode? p)
	{
		var nodeId = p!["nodeId"]!.GetValue<string>();
		if (!_running.TryGetValue(nodeId, out var turn))
			return new SessionStopResult(false);

		turn.Cancel.Cancel();
		return new SessionStopResult(true);
	}

	private async Task RunAsync(Turn turn, SemaphoreSlim gate)
	{
		await gate.WaitAsync();
		var cancelled = false;
		try
		{
			_running[turn.NodeId] = turn;
			var node = _env.Base.Registry.FindModelByNodeId(turn.NodeId)!;
			_bus().Publish(RemoteProtocol.TurnStream.StatusEvent, new TurnStatusPayload(turn.NodeId, turn.TurnId, TurnState.Running));
			PublishRunning(turn.NodeId, true);
			_env.Base.Files.AppendMessage(node.FileName, "USER", turn.Text, turn.Profile, null, null);
			PublishAppended(node);

			var answer = new System.Text.StringBuilder();
			long seq = 0;
			try
			{
				await StreamAsync(turn, answer, () => ++seq);
			}
			catch (OperationCanceledException)
			{
				cancelled = true;
			}

			if (!cancelled)
			{
				_bus().Publish(RemoteProtocol.TurnStream.UsageEvent, new TurnUsagePayload(turn.NodeId, turn.TurnId, 120, answer.Length, 0.01));
				_env.Base.Files.AppendMessage(node.FileName, "ASSISTANT", answer.ToString(), null, turn.Model, turn.Effort);
				Interlocked.Increment(ref _answers);
				PublishAppended(node);
			}

			PublishRunning(turn.NodeId, false);
			_bus().Publish(RemoteProtocol.Events.TurnCompleted, new TurnCompletedPayload(turn.NodeId, cancelled, null, turn.TurnId));
			Completions.Enqueue((turn.TurnId, cancelled));
		}
		finally
		{
			_running.TryRemove(turn.NodeId, out _);
			gate.Release();
		}
	}

	private async Task StreamAsync(Turn turn, System.Text.StringBuilder answer, Func<long> nextSeq)
	{
		var token = turn.Cancel.Token;
		void Delta(string text, TurnDeltaKind kind = TurnDeltaKind.Text)
		{
			if (kind == TurnDeltaKind.Text)
				answer.Append(text);
			_bus().Publish(RemoteProtocol.TurnStream.DeltaEvent, new TurnDeltaPayload(turn.NodeId, turn.TurnId, nextSeq(), kind, text));
		}

		Delta("considering the request", TurnDeltaKind.Thinking);
		_bus().Publish(RemoteProtocol.TurnStream.ToolEvent, new TurnToolPayload(turn.NodeId, turn.TurnId, "tool-1", "Read", TurnToolStatus.Started, "notes.md"));
		_bus().Publish(RemoteProtocol.TurnStream.ToolEvent, new TurnToolPayload(turn.NodeId, turn.TurnId, "tool-1", "Read", TurnToolStatus.Result, "42 lines", false));

		if (turn.Text.StartsWith(HoldPrefix, StringComparison.Ordinal))
		{
			Delta("holding until released. ");
			await _held.Task.WaitAsync(token);
			return;
		}

		if (turn.Text.StartsWith(StreamPrefix, StringComparison.Ordinal))
		{
			for (var i = 0; i < ChunkCount; i++)
			{
				await Task.Delay(ChunkDelay, token);
				Delta($"[chunk{i}]" + new string((char)('a' + i), ChunkChars - 8));
				Interlocked.Increment(ref _chunks);
			}

			return;
		}

		await Task.Delay(100, token);
		Delta("Quick answer to: " + turn.Text);
	}

	private void PublishRunning(string nodeId, bool running) =>
		_bus().Publish(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(new SessionStateDto(nodeId, running, false, null, false, null, null, false, null)));

	private void PublishAppended(TessynDesktop.Models.SessionNodeModel node) =>
		_bus().Publish(RemoteProtocol.Events.SessionAppended, new SessionEpochPayload(node.NodeId, _env.Base.Epochs.GetEpoch(node.FileName)));
}
