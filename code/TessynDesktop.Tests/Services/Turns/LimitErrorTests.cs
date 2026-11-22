using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// The limits-error warning (FR.1.29, UC.LE.01, TC.LE.01 to TC.LE.09): a turn that ended with a usage or limits error sets <c>SessionStateDto.LimitError</c>, a later successful turn clears it,
/// and the change is published as <c>session.changed</c>. Detection is structured first (the CLI's <c>error: "rate_limit"</c> stamp); the text patterns are tested with messages found in real sessions.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LimitErrorTests : IDisposable
{
	// Real messages from session files (read-only grep of %AppData%\TessynDesktop\sessions).
	public static TheoryData<string> RealMessages => new()
	{
		"You've hit your session limit · resets 5:20pm (Asia/Makassar)",
		"You've hit your weekly limit · resets Nov 11, 1pm (Asia/Makassar)",
		"You've hit your 5-hour limit · resets 1:10pm (Asia/Makassar)",
		"Claude AI usage limit reached|1731445200",
	};

	// A real assistant line of the CLI for a rate limit (trimmed): the structured stamp is "error" and "quotaLimits".
	private const string RealAssistantLine = """{"type":"assistant","message":{"model":"<synthetic>","role":"assistant","stop_reason":"stop_sequence","content":[{"type":"text","text":"You've hit your session limit · resets 1:30pm (Asia/Makassar)"}]},"quotaLimits":{"status":"rejected","rateLimitType":"five_hour"},"error":"rate_limit","isApiErrorMessage":true,"apiErrorStatus":429}""";

	private readonly TurnEngineHarness _h = new();
	private readonly List<string> _changes = [];

	public LimitErrorTests() => _h.AuthErrors.Changed += nodeId => _changes.Add(nodeId);

	public void Dispose() => _h.Dispose();

	private static ClaudeStreamEvent LimitAssistant(string text) => new() { Type = "assistant", Content = text, ApiError = "rate_limit", QuotaRejected = true };

	[Theory]
	[MemberData(nameof(RealMessages))]
	public void TheTextPatterns_MatchTheRealMessages_OnAResultError(string message) =>   // TC.LE.01
		Assert.True(LimitErrorDetector.IsLimit(Evt.ResultError(message)));

	[Theory]
	[InlineData("API Error: 401 Invalid authentication credentials")]
	[InlineData("API Error: 500 internal server error")]
	[InlineData("Tool failed: file not found")]
	[InlineData("rate limited")]
	public void OtherErrors_AreNotLimitErrors(string message) =>
		Assert.False(LimitErrorDetector.IsLimit(Evt.ResultError(message)));

	[Fact]
	public void AnAssistantTextAboutLimits_IsNotMatchedByText_OnlyTheStampCounts()   // TC.LE.02
	{
		Assert.False(LimitErrorDetector.IsStructuredLimit(Evt.Assistant("You've hit your session limit · resets 5pm (X) is what the CLI prints when...")));
		Assert.True(LimitErrorDetector.IsStructuredLimit(LimitAssistant("anything")));
		Assert.True(LimitErrorDetector.IsStructuredLimit(new ClaudeStreamEvent { Type = "assistant", Content = "x", QuotaRejected = true }));
		Assert.False(LimitErrorDetector.IsStructuredLimit(new ClaudeStreamEvent { Type = "assistant", Content = "x", ApiError = "server_error" }));
	}

	[Fact]
	public void TheParser_ReadsTheStampOfARealAssistantLine()   // TC.LE.03
	{
		var evt = ClaudeStreamEventParser.TryParseEvent(RealAssistantLine)!;

		Assert.Equal("rate_limit", evt.ApiError);
		Assert.True(evt.QuotaRejected);
		Assert.StartsWith("You've hit your session limit", evt.Content);
		Assert.True(LimitErrorDetector.IsStructuredLimit(evt));

		var plain = ClaudeStreamEventParser.TryParseEvent("""{"type":"assistant","message":{"content":[{"type":"text","text":"Hello"}]}}""")!;
		Assert.Null(plain.ApiError);
		Assert.False(plain.QuotaRejected);
	}

	[Fact]
	public void TheText_IsShortenedToOneLine_AtMostTheLimit()
	{
		Assert.Equal("first line", LimitErrorDetector.Shorten("  first line\nsecond line  "));
		var shortened = LimitErrorDetector.Shorten(new string('x', 500));
		Assert.Equal(Constants.LimitErrors.TooltipMaxChars, shortened.Length);
		Assert.EndsWith("…", shortened);
	}

	[Fact]
	public async Task AStampedAssistantMessage_SetsTheLimitError_AndTheTrackerAnnounces()   // TC.LE.04
	{
		_h.Process.ScriptEvents(LimitAssistant("You've hit your session limit · resets 5:20pm (Asia/Makassar)"), Evt.ResultError("You've hit your session limit · resets 5:20pm (Asia/Makassar)"));

		await _h.Run(TurnEngineHarness.Node(), "Hi").Within();

		Assert.Equal("You've hit your session limit · resets 5:20pm (Asia/Makassar)", _h.AuthErrors.GetLimitError(TurnEngineHarness.NodeId));
		Assert.False(_h.AuthErrors.Has(TurnEngineHarness.NodeId));   // not an auth error
		Assert.Single(_changes);   // the same text from the result error changes nothing
	}

	[Fact]
	public async Task AResultErrorWithALimitText_SetsIt_WithoutTheStamp()
	{
		_h.Process.ScriptEvents(Evt.ResultError("You've hit your weekly limit · resets Nov 11, 1pm (Asia/Makassar)"));

		await _h.Run(TurnEngineHarness.Node(), "Hi").Within();

		Assert.StartsWith("You've hit your weekly limit", _h.AuthErrors.GetLimitError(TurnEngineHarness.NodeId));
	}

	[Fact]
	public async Task ASystemErrorWithALimitText_SetsIt()
	{
		_h.Process.ScriptEvents(Evt.SystemError("You've hit your 5-hour limit · resets 1:10pm (Asia/Makassar)"));

		await _h.Run(TurnEngineHarness.Node(), "Hi").Within();

		Assert.StartsWith("You've hit your 5-hour limit", _h.AuthErrors.GetLimitError(TurnEngineHarness.NodeId));
	}

	[Fact]
	public async Task ASuccessfulLaterTurn_ClearsIt_AndAnnouncesTheChange()   // TC.LE.05
	{
		var node = TurnEngineHarness.Node();
		_h.Process.ScriptEvents(Evt.ResultError("You've hit your session limit · resets 5pm (X)"));
		_h.Process.ScriptEvents(Evt.Result());

		await _h.Run(node, "one").Within();
		Assert.NotNull(_h.AuthErrors.GetLimitError(TurnEngineHarness.NodeId));

		await _h.Run(node, "two").Within();

		Assert.Null(_h.AuthErrors.GetLimitError(TurnEngineHarness.NodeId));
		Assert.Equal(2, _changes.Count);
	}

	[Fact]
	public async Task AnOtherError_DoesNotClearIt_AndANormalAnswerWithoutAResultDoesNot()
	{
		var node = TurnEngineHarness.Node();
		_h.Process.ScriptEvents(Evt.ResultError("You've hit your session limit · resets 5pm (X)"));
		_h.Process.ScriptEvents(Evt.ResultError("API Error: 500 internal server error"));

		await _h.Run(node, "one").Within();
		await _h.Run(node, "two").Within();

		Assert.NotNull(_h.AuthErrors.GetLimitError(TurnEngineHarness.NodeId));
	}

	[Fact]
	public async Task AStreamingTurn_SetsAndClearsItToo()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		_h.Streaming.ScriptEvents(LimitAssistant("You've hit your session limit · resets 5pm (X)"), Evt.ResultError("You've hit your session limit · resets 5pm (X)"));
		_h.Streaming.ScriptEvents(Evt.Result());

		await _h.Run(node, "one").Within();
		Assert.NotNull(_h.AuthErrors.GetLimitError(TurnEngineHarness.NodeId));

		await _h.Run(node, "two").Within();
		Assert.Null(_h.AuthErrors.GetLimitError(TurnEngineHarness.NodeId));
	}

	[Fact]
	public void TheTracker_AnnouncesOnlyChanges()
	{
		var tracker = new SessionAuthErrorTracker();
		var changed = new List<string>();
		tracker.Changed += changed.Add;

		tracker.SetLimitError("a", "x");
		tracker.SetLimitError("a", "x");
		tracker.SetLimitError("a", null);
		tracker.SetLimitError("a", null);

		Assert.Equal(["a", "a"], changed);
	}

	// ---- the state and the event (TC.LE.06 to TC.LE.08) ----

	[Fact]
	public void TheState_CarriesTheLimitError_OmittedWhenNone()   // TC.LE.06
	{
		using var env = new RemoteTreeTestEnv();
		var tracker = new SessionAuthErrorTracker();
		var states = new SessionStateBuilder(env.Registry, TestEngines.Claude(new FakeStreamingManagerForTurnService()), env.Attention, env.Activity, tracker);
		var session = env.AddSession(env.AddDirectory("p"), "S");

		var clean = states.Build(session);
		Assert.Null(clean.LimitError);
		Assert.False(JsonSerializer.SerializeToNode(clean, TdcpJsonOptions.Default)!.AsObject().ContainsKey("limitError"));

		tracker.SetLimitError(session.NodeId, "You've hit your session limit · resets 5pm (X)");
		var json = JsonSerializer.SerializeToNode(states.Build(session), TdcpJsonOptions.Default)!.AsObject();
		Assert.Equal("You've hit your session limit · resets 5pm (X)", json["limitError"]!.GetValue<string>());
	}

	[Fact]
	public void AChange_PublishesSessionChanged_WithTheTextThenWithout()   // TC.LE.07
	{
		using var env = new RemoteTreeTestEnv();
		var tracker = new SessionAuthErrorTracker();
		var states = new SessionStateBuilder(env.Registry, TestEngines.Claude(new FakeStreamingManagerForTurnService()), env.Attention, env.Activity, tracker);
		var session = env.AddSession(env.AddDirectory("p"), "S");
		var daemon = FakeDaemonProxy.Create(out var fake);
		fake.ByNodeId[session.NodeId] = session;
		var bus = new RemoteEventBus();
		var received = new List<RemoteNotification>();
		bus.Published += n => { lock (received) received.Add(n); };
		var files = new SessionFileService(env.Settings);
		var publisher = new RemoteNotificationPublisher(daemon, files, bus, new TreeVersionTracker(), new SessionEpochTracker(files), states, authErrors: tracker);
		publisher.Start();
		try
		{
			tracker.SetLimitError(session.NodeId, "You've hit your session limit · resets 5pm (X)");
			var first = WaitFor(received, 1);
			Assert.Equal("You've hit your session limit · resets 5pm (X)", first[0].Params["state"]!["limitError"]!.GetValue<string>());

			tracker.SetLimitError(session.NodeId, null);
			var second = WaitFor(received, 2);
			Assert.Null(second[1].Params["state"]!["limitError"]);
		}
		finally
		{
			publisher.Stop();
		}
	}

	[Fact]
	public void ARepeatedIdenticalLimit_PublishesNothingNew()   // TC.LE.08
	{
		var tracker = new SessionAuthErrorTracker();
		var changes = 0;
		tracker.Changed += _ => changes++;

		for (var i = 0; i < 50; i++)
			tracker.SetLimitError("n", "same");

		Assert.Equal(1, changes);
	}

	private static List<RemoteNotification> WaitFor(List<RemoteNotification> received, int count)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
		while (DateTime.UtcNow < deadline)
		{
			lock (received)
			{
				var changed = received.Where(n => n.Method == Constants.Remote.Events.SessionChanged).ToList();
				if (changed.Count >= count)
					return changed;
			}

			Thread.Sleep(20);
		}

		throw new TimeoutException("session.changed did not arrive");
	}
}
