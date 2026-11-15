using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The fixes of the W1b review (M1 to M4, L1, L2, L7): races of the draft sync, the retry of a failed write, the hand-off guard, the serialised read.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerW1bReviewFixTests : IDisposable
{
	private const string A = "a";
	private const string B = "b";

	private readonly FakeTessynClient _client = new();
	private readonly List<IDisposable> _disposables = [];

	public ComposerW1bReviewFixTests()
	{
		_client.Sessions[A] = FakeSession.WithEntries(2, "A");
		_client.Sessions[B] = FakeSession.WithEntries(2, "B");
	}

	public void Dispose()
	{
		foreach (var d in _disposables)
			d.Dispose();
	}

	private SessionPaneViewModel CreatePane(TimeSpan? delay = null)
	{
		var pane = new SessionPaneViewModel(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
		pane.DraftSync.Delay = delay ?? TimeSpan.FromMinutes(5);
		_disposables.Add(pane);
		return pane;
	}

	private static async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "Not reached in time: " + what);
	}

	// ---- M1 ----

	[Fact]
	public async Task M1_ARemoteTextApplied_IsNotOverwrittenByAWriteThatWasStillWaiting()
	{
		_client.DraftTexts[A] = "abc";
		var pane = CreatePane();
		await pane.OpenAsync(A);
		await WaitForAsync(() => pane.InputText == "abc", "draft shown");
		pane.InputText = "abcd";
		pane.InputText = "abc";   // back to the agreed text: a write of "abc" is waiting

		_client.DraftTexts[A] = "abcZ";
		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(A, "phone"));
		await WaitForAsync(() => pane.InputText == "abcZ", "remote text shown");
		await pane.DraftSync.FlushAllAsync();

		Assert.Equal("abcZ", _client.DraftTexts[A]);
		Assert.Equal("abcZ", pane.InputText);
	}

	// ---- M2 ----

	[Fact]
	public async Task M2_AMergedConflict_SurvivesTextTypedWhileItWasBeingMerged()
	{
		var pane = CreatePane();
		await pane.OpenAsync(A);
		await WaitForAsync(() => _client.Calls.Contains("draft.get:a"), "draft.get");
		_client.DraftTexts[A] = "xyz";   // another writer, event not seen
		pane.InputText = "abc";
		var gate = new TaskCompletionSource();
		_client.DraftGetGate = gate;
		var flush = pane.DraftSync.FlushAllAsync();   // conflicts, then waits for the read

		pane.InputText = "abcY";   // typed on meanwhile
		gate.SetResult();
		await flush;
		await pane.DraftSync.FlushAllAsync();

		var expected = "xyz" + Environment.NewLine + "abcY";
		Assert.Equal(expected, pane.InputText);
		Assert.Equal(expected, _client.DraftTexts[A]);
	}

	// ---- M3 ----

	[Fact]
	public async Task M3_ADraftReadIssuedBeforeLeavingASession_DoesNotReplaceWhatWasTypedThere()
	{
		var pane = CreatePane();
		var gate = new TaskCompletionSource();
		_client.DraftGetGate = gate;
		await pane.OpenAsync(A);   // its draft.get is still on its way
		pane.InputText = "x";
		await pane.OpenAsync(B);
		gate.SetResult();            // the old answer ("no draft") arrives after the leave
		await WaitForAsync(() => _client.DraftTexts.GetValueOrDefault(A) == "x", "the left draft was written");

		await pane.OpenAsync(A);

		Assert.Equal("x", pane.InputText);
	}

	// ---- M4 ----

	[Fact]
	public async Task M4_AFailedWrite_IsTriedAgainAfterABackoff_WithoutNewTyping()
	{
		var pane = CreatePane(TimeSpan.Zero);
		pane.DraftSync.RetryBackoff = TimeSpan.FromMilliseconds(40);
		await pane.OpenAsync(A);
		_client.DraftSetException = new IOException("daemon restarting");
		pane.InputText = "kept through the outage";
		await WaitForAsync(() => _client.Calls.Count(c => c.StartsWith("draft.set:a", StringComparison.Ordinal)) >= 1, "first try");

		_client.DraftSetException = null;   // the connection is back

		await WaitForAsync(() => _client.DraftTexts.GetValueOrDefault(A) == "kept through the outage", "the retry wrote the draft");
	}

	[Fact]
	public async Task M4_AResync_FlushesWhatIsStillPending()
	{
		var pane = CreatePane(TimeSpan.Zero);
		pane.DraftSync.RetryBackoff = TimeSpan.FromHours(1);   // only the resync can write it
		await pane.OpenAsync(A);
		_client.DraftSetException = new IOException("down");
		pane.InputText = "waiting for the connection";
		await WaitForAsync(() => _client.Calls.Any(c => c.StartsWith("draft.set:a", StringComparison.Ordinal)), "first try");
		_client.DraftSetException = null;

		_client.RaiseResync();

		await WaitForAsync(() => _client.DraftTexts.GetValueOrDefault(A) == "waiting for the connection", "resync flush");
	}

	// ---- L1 ----

	[Fact]
	public void L1_TwoOverlappingHandoffs_KeepTheGuardUntilTheLastEnds()
	{
		var guard = new HandoffGuard();
		guard.Enter(A);
		guard.Enter(A);
		guard.Enter(B);

		guard.Exit(A);
		Assert.True(guard.IsActive(A));
		guard.Exit(A);
		Assert.False(guard.IsActive(A));
		Assert.True(guard.IsActive(B));
		guard.Exit(A);   // an extra exit never goes negative
		guard.Enter(A);
		Assert.True(guard.IsActive(A));
	}

	// ---- L2 ----

	[Fact]
	public async Task L2_AReadIssuedBeforeAWrite_DoesNotPutTheOlderRevisionBack()
	{
		using var sync = new DraftSynchronizer(_client, NullLogger.Instance, TimeSpan.Zero);
		await sync.LoadAsync("z");   // known: revision "0"
		var gate = new TaskCompletionSource();
		_client.DraftGetGate = gate;
		var slowRead = sync.LoadAsync("z");   // snapshot: no draft, revision "0"

		sync.Schedule("z", "w");              // a write while the read is outstanding
		await Task.Delay(50);
		gate.SetResult();
		await slowRead;
		await WaitForAsync(() => _client.DraftTexts.GetValueOrDefault("z") == "w", "the write happened");
		await sync.FlushAsync("z");

		Assert.True(sync.IsKnownRevision("z", FakeTessynClient.RevisionOf("w")));
	}

	// ---- L7 ----

	[Fact]
	public void L7_TheSoundChannel_IsTheProtocolsName() =>
		Assert.Equal(RemoteProtocol.Notifications.SoundChannel, Constants.SoundRules.Channel);
}
