using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The J1 session option service over a real registry: tri-state Auto-Commit, the FR.11.12 corrections, the reset of the conversation.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionOptionsServiceTests : IDisposable
{
	private readonly TreeMutationTestEnv _env = new();
	private readonly RecordingTurnEngine _engine = new();
	private readonly ScriptedStreamingManager _streaming = new();
	private readonly SharedLockTurnService _turns = new();
	private readonly SessionOptionsService _service;
	private readonly SessionNodeModel _session;
	private readonly List<string> _settingsChanged = [];

	public SessionOptionsServiceTests()
	{
		_service = new SessionOptionsService(_env.Registry, _env.Settings, _engine, new FixedSettingsResolver(), TestEngines.Claude(_streaming), _turns);
		_session = _env.AddSession(_env.AddDirectory("proj"), "S");
		_session.ClaudeSessionId = "claude-1";
		_env.Registry.SessionSettingsChanged += (_, e) => _settingsChanged.Add(e.NodeId);
	}

	public void Dispose() => _env.Dispose();

	private static SessionOptionsPatch Patch(bool changeAutoCommit = false, bool? autoCommit = null, bool? autoDocument = null, bool? sound = null) =>
		new(changeAutoCommit, autoCommit, autoDocument, sound);

	[Fact]
	public async Task SetAsync_AppliesOnlyWhatTheKeysSay_AndAbsentKeysStay()
	{
		_session.IsAutoCommit          = false;
		_session.IsAutoDocument        = true;
		_session.PlayCompletionSound   = true;

		var result = await _service.SetAsync(_session.NodeId, Patch(sound: false));

		Assert.Equal(new TessynDesktop.Models.Remote.SessionOptionsDto(false, true, false), result.Options);
		Assert.Null(result.Corrections);
		Assert.Equal([_session.NodeId], _settingsChanged);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	[InlineData(null)]
	public async Task SetAsync_AutoCommitIsTriState_NullMeansNoPreference(bool? value)
	{
		_session.IsAutoCommit = value is null ? false : null;

		var result = await _service.SetAsync(_session.NodeId, Patch(changeAutoCommit: true, autoCommit: value));

		Assert.Equal(value, _session.IsAutoCommit);
		Assert.Equal(value, result.Options.AutoCommit);
	}

	[Fact]
	public async Task SetAsync_AChangeThatChangesNothing_RaisesNoEvent_AndSendsNoCorrection()
	{
		_session.IsAutoCommit = true;
		_engine.Busy          = true;

		var result = await _service.SetAsync(_session.NodeId, Patch(changeAutoCommit: true, autoCommit: true));

		Assert.Empty(_settingsChanged);
		Assert.Empty(_engine.Corrections);
		Assert.Null(result.Corrections);
	}

	[Fact]
	public async Task SetAsync_WhileTheSessionRuns_SendsTheFr1112CorrectionForEachChangedToggle_AndReturnsTheNotices()
	{
		_session.IsAutoCommit   = false;
		_session.IsAutoDocument = false;
		_engine.Busy            = true;

		var result = await _service.SetAsync(_session.NodeId, Patch(changeAutoCommit: true, autoCommit: true, autoDocument: true, sound: false));

		Assert.Equal([Constants.Instructions.MidRunAutoCommitOn, Constants.Instructions.MidRunAutoDocumentOn], _engine.Corrections.Select(c => c.Prompt));
		Assert.All(_engine.Corrections, c => Assert.Same(_session, c.Node));
		Assert.Equal(["[AutoCommit was enabled for this run]", "[AutoDocument was enabled for this run]"], result.Corrections);
	}

	[Fact]
	public async Task SetAsync_ChangingAutoCommitToDoNotCommit_SendsTheOffCorrection()
	{
		_session.IsAutoCommit = true;
		_engine.Busy          = true;

		var result = await _service.SetAsync(_session.NodeId, Patch(changeAutoCommit: true, autoCommit: false));

		Assert.Equal(Constants.Instructions.MidRunAutoCommitOff, Assert.Single(_engine.Corrections).Prompt);
		Assert.Equal("[AutoCommit was disabled for this run]", Assert.Single(result.Corrections!));
	}

	[Fact]
	public async Task SetAsync_ChangingAutoCommitToNoPreference_SendsTheSilentCorrection()
	{
		_session.IsAutoCommit = false;
		_engine.Busy          = true;

		var result = await _service.SetAsync(_session.NodeId, Patch(changeAutoCommit: true, autoCommit: null));

		Assert.Equal(Constants.Instructions.MidRunAutoCommitSilent, Assert.Single(_engine.Corrections).Prompt);
		Assert.Equal("[AutoCommit was set to no preference for this run]", Assert.Single(result.Corrections!));
	}

	[Fact]
	public async Task SetAsync_ThePlayCompletionSoundAlone_NeverSendsACorrection_EvenWhileRunning()
	{
		_engine.Busy = true;

		var result = await _service.SetAsync(_session.NodeId, Patch(sound: false));

		Assert.False(result.Options.PlayCompletionSound);
		Assert.Empty(_engine.Corrections);
	}

	[Fact]
	public async Task SetAsync_ARunningSessionWithoutAClaudeSessionId_SavesTheOption_ButSendsNoCorrection()
	{
		_session.ClaudeSessionId = null;
		_session.IsAutoDocument  = false;
		_engine.Busy             = true;

		var result = await _service.SetAsync(_session.NodeId, Patch(autoDocument: true));

		Assert.True(_session.IsAutoDocument);
		Assert.Empty(_engine.Corrections);
		Assert.Null(result.Corrections);
	}

	[Fact]
	public async Task SetAsync_AHeadlessRun_CountsAsRunning()
	{
		_session.IsAutoDocument = false;
		_env.Registry.SetSessionRunning(_session.NodeId, true);

		await _service.SetAsync(_session.NodeId, Patch(autoDocument: true));

		Assert.Single(_engine.Corrections);
	}

	[Fact]
	public async Task SetAsync_AnUnknownSession_IsRefused() =>
		Assert.Equal("Session not found", (await Assert.ThrowsAsync<TdcpException>(() => _service.SetAsync("nope", Patch(sound: true)))).Message);

	// ── resetConversation ────────────────────────────────────────────────────

	[Fact]
	public void ResetConversation_DetachesTheClaudeSessionId_KeepsItAmongThePriorOnes_AndRaisesTheEvent()
	{
		var result = _service.ResetConversation(_session.NodeId);

		Assert.True(result.Cleared);
		Assert.Null(_session.ClaudeSessionId);
		Assert.Contains("claude-1", _session.PriorClaudeSessionIds);
		Assert.Equal([_session.NodeId], _settingsChanged);
	}

	[Fact]
	public void ResetConversation_WithoutASessionId_ClearsNothing_AndRaisesNoEvent()
	{
		_session.ClaudeSessionId = null;

		Assert.False(_service.ResetConversation(_session.NodeId).Cleared);
		Assert.Empty(_settingsChanged);
		Assert.Empty(_session.PriorClaudeSessionIds);
	}

	[Fact]
	public void ResetConversation_IsRefusedWhileATurnRuns_AndChangesNothing()
	{
		_engine.Busy = true;

		var error = Assert.Throws<TdcpException>(() => _service.ResetConversation(_session.NodeId));

		Assert.Equal(Constants.TreeMutation.TurnRunning, error.Message);
		Assert.Equal("claude-1", _session.ClaudeSessionId);
	}

	[Fact]
	public void ResetConversation_StopsALiveStreamingProcess_SoTheDetachTakesEffect()
	{
		_streaming.EnsureStarted(_session.NodeId, new StreamingSpawnParams("wd", "claude", null, null, null, null, null, null, false, false), _ => { });

		_service.ResetConversation(_session.NodeId);

		Assert.Equal([_session.NodeId], _streaming.Stopped);
	}

	[Fact]
	public void ResetConversation_WhileTheNodesTurnLockIsHeld_IsRefused_AndChangesNothing()
	{
		_streaming.EnsureStarted(_session.NodeId, new StreamingSpawnParams("wd", "claude", null, null, null, null, null, null, false, false), _ => { });
		var gate = _turns.GetTurnLock(_session.NodeId);
		gate.Wait();
		try
		{
			var error = Assert.Throws<TdcpException>(() => _service.ResetConversation(_session.NodeId));
			Assert.Equal(Constants.TreeMutation.TurnRunning, error.Message);
		}
		finally
		{
			gate.Release();
		}

		Assert.Equal("claude-1", _session.ClaudeSessionId);
		Assert.Empty(_streaming.Stopped);
	}

	[Fact]
	public void ResetConversation_ATurnThatStartsAfterTheFirstCheck_DoesNotGetItsStreamingProcessStopped()
	{
		// The engine reports idle on the entry check and busy on the next one, as when a turn begins between the two.
		_streaming.EnsureStarted(_session.NodeId, new StreamingSpawnParams("wd", "claude", null, null, null, null, null, null, false, false), _ => { });
		_engine.BusyFromCall = 2;

		_service.ResetConversation(_session.NodeId);

		// The detach ran under the node's turn lock (nothing could start), but the process of the turn that began is left alone.
		Assert.Empty(_streaming.Stopped);
	}

	[Fact]
	public void ResetConversation_ATurnAlreadyRunningAtTheLockedCheck_IsRefused()
	{
		_engine.BusyFromCall = 1;

		var error = Assert.Throws<TdcpException>(() => _service.ResetConversation(_session.NodeId));

		Assert.Equal(Constants.TreeMutation.TurnRunning, error.Message);
		Assert.Equal("claude-1", _session.ClaudeSessionId);
	}

	/// <summary>An engine that only knows whether it is busy and records the corrections it was asked to send.</summary>
	private sealed class RecordingTurnEngine : ITurnEngine
	{
		private int _isBusyCalls;

		public bool Busy { get; set; }

		/// <summary>From this call of <see cref="IsBusy"/> on the engine reports busy (a turn that begins in the middle of a reset).</summary>
		public int BusyFromCall { get; set; } = int.MaxValue;

		public List<(SessionNodeModel Node, string Prompt)> Corrections { get; } = [];

		public bool IsBusy(string nodeId) => Busy || ++_isBusyCalls >= BusyFromCall;
		public void SendCorrection(SessionNodeModel node, TurnSettings settings, string prompt) => Corrections.Add((node, prompt));
		public Task<TurnOutcome> RunAsync(TurnRequest request, ITurnObserver? observer = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public bool CancelRuns(string nodeId) => false;
		public void ReleaseObserver(string nodeId, ITurnObserver observer) { }
	}

	private sealed class FixedSettingsResolver : ITurnSettingsResolver
	{
		public Task<TurnSettings> ResolveAsync(SessionNodeModel node, TurnOptionsModel? options) =>
			Task.FromResult(new TurnSettings(null, null, null, null, null, false, false, null, null));
	}
}
