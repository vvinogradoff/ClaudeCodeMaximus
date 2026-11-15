using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>The session auth error (package J2): the engine publishes what <see cref="ITurnObserver.AuthErrorChanged"/> reports, for every client.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineAuthErrorTests : IDisposable
{
	private const string Invalid = "API Error: 401 Invalid authentication credentials";

	private readonly TurnEngineHarness _h = new();
	private readonly List<string> _changes = [];

	public TurnEngineAuthErrorTests() => _h.AuthErrors.Changed += nodeId => _changes.Add(nodeId);

	public void Dispose() => _h.Dispose();

	[Fact]
	public async Task AnErrorResultForInvalidCredentials_SetsTheFlag_AndTheObserverIsToldToo()
	{
		var observer = new RecordingTurnObserver();
		_h.Process.ScriptEvents(Evt.ResultError(Invalid));

		await _h.Run(TurnEngineHarness.Node(), "Hi", observer).Within();

		Assert.True(_h.AuthErrors.Has(TurnEngineHarness.NodeId));
		Assert.Contains("authError:True", observer.Snapshot());
	}

	[Fact]
	public async Task ASystemErrorForInvalidCredentials_SetsTheFlag()
	{
		_h.Process.ScriptEvents(Evt.SystemError(Invalid));

		await _h.Run(TurnEngineHarness.Node(), "Hi").Within();

		Assert.True(_h.AuthErrors.Has(TurnEngineHarness.NodeId));
	}

	[Fact]
	public async Task ASuccessfulTurn_ClearsTheFlag()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.ScriptEvents(Evt.ResultError(Invalid));
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(node, "one").Within();
		await _h.Run(node, "two").Within();

		Assert.False(_h.AuthErrors.Has(TurnEngineHarness.NodeId));
		Assert.Equal([TurnEngineHarness.NodeId, TurnEngineHarness.NodeId], _changes);
	}

	[Fact]
	public async Task AnotherError_LeavesTheFlagAlone()
	{
		_h.Process.ScriptEvents(Evt.ResultError("rate limited"));

		await _h.Run(TurnEngineHarness.Node(), "Hi").Within();

		Assert.False(_h.AuthErrors.Has(TurnEngineHarness.NodeId));
		Assert.Empty(_changes);
	}

	[Fact]
	public async Task ARepeatedFailure_ChangesTheStateOnce()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.ScriptEvents(Evt.ResultError(Invalid));
		_h.Process.ScriptEvents(Evt.ResultError(Invalid));

		await _h.Run(node, "one").Within();
		await _h.Run(node, "two").Within();

		Assert.Single(_changes);
	}

	[Fact]
	public async Task AStreamingTurn_SetsAndClearsItToo()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		_h.Streaming.ScriptEvents(Evt.ResultError(Invalid));
		_h.Streaming.ScriptEvents(Evt.Result());

		await _h.Run(node, "one").Within();
		Assert.True(_h.AuthErrors.Has(TurnEngineHarness.NodeId));

		await _h.Run(node, "two").Within();
		Assert.False(_h.AuthErrors.Has(TurnEngineHarness.NodeId));
	}

	[Fact]
	public void ClearAll_ClearsEverySession_AndAnnouncesEach()
	{
		var tracker = new SessionAuthErrorTracker();
		var changed = new List<string>();
		tracker.Changed += changed.Add;
		tracker.Set("a", true);
		tracker.Set("b", true);
		changed.Clear();

		tracker.ClearAll();

		Assert.False(tracker.Has("a"));
		Assert.False(tracker.Has("b"));
		Assert.Equal(["a", "b"], changed.Order());
	}
}
