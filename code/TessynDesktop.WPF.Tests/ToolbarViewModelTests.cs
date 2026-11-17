using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The instruction toolbar, the command-bar toggles and the local-tools control of the session pane (W4, FR.11, FR.19).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ToolbarViewModelTests
{
	private const string Node = "a";

	private sealed class ScriptedDialogs : IDialogService
	{
		public List<ConfirmDialogRequest> Confirms { get; } = [];

		public bool Answer { get; set; } = true;

		public void CancelPending()
		{
		}

		public Task<bool> ConfirmAsync(ConfirmDialogRequest request)
		{
			Confirms.Add(request);
			return Task.FromResult(Answer);
		}

		public Task<string?> InputAsync(InputDialogRequest request) => Task.FromResult<string?>(null);

		public Task<string?> ChoiceAsync(ChoiceDialogRequest request) => Task.FromResult<string?>(null);
	}

	private readonly FakeTessynClient _client = new();
	private readonly ScriptedDialogs _dialogs = new();
	private SessionOptionsDto _stored = new(false, false, true);

	/// <summary>A daemon that remembers the options and answers a no-change call with them.</summary>
	private void StatefulOptions(IReadOnlyList<string>? corrections = null)
	{
		_client.SetOptionsHandler = (_, autoCommit, autoDocument, sound) =>
		{
			_stored = new SessionOptionsDto(
				autoCommit == null ? _stored.AutoCommit : autoCommit == AutoCommitSetting.Commit ? true : autoCommit == AutoCommitSetting.DoNotCommit ? false : null,
				autoDocument ?? _stored.AutoDocument,
				sound ?? _stored.PlayCompletionSound);
			return new SessionOptionsDto(_stored.AutoCommit, _stored.AutoDocument, _stored.PlayCompletionSound) is var o
				? new SessionSetOptionsResult(o, autoCommit != null || autoDocument != null ? corrections : null)
				: throw new InvalidOperationException();
		};
	}

	private async Task<SessionPaneViewModel> OpenedAsync(string id = Node, bool running = false, Action<FakeSession>? configure = null, Action<SessionPaneViewModel>? beforeOpen = null)
	{
		var session = FakeSession.WithEntries(4);
		session.Running = running;
		configure?.Invoke(session);
		_client.Sessions[id] = session;
		var pane = new SessionPaneViewModel(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			LiveFlushInterval = TimeSpan.Zero,
			Dialogs = _dialogs,
		};
		beforeOpen?.Invoke(pane);
		await pane.OpenAsync(id);
		await pane.SessionOptions.Idle;
		await pane.LocalTools.Idle;
		return pane;
	}

	// ---- Auto-Commit and the sticky options ----

	[Fact]
	public async Task Open_ReadsTheStoredOptionsWithANoChangeCall()
	{
		_stored = new SessionOptionsDto(true, true, false);
		StatefulOptions();
		using var pane = await OpenedAsync();

		Assert.True(pane.SessionOptions.IsKnown);
		Assert.Equal(AutoCommitSetting.Commit, pane.SessionOptions.AutoCommit);
		Assert.True(pane.SessionOptions.AutoDocument);
		Assert.False(pane.SessionOptions.PlayCompletionSound);
		Assert.Contains("session.setOptions:a:::", _client.Calls);   // nothing to change: it only reads
	}

	[Fact]
	public async Task AutoCommit_CyclesDoNotCommitCommitNoPreference()
	{
		_stored = new SessionOptionsDto(false, false, true);
		StatefulOptions();
		using var pane = await OpenedAsync();
		var shown = new List<AutoCommitSetting> { pane.SessionOptions.AutoCommit };

		for (var i = 0; i < 3; i++)
		{
			await pane.Instruction.CycleAutoCommitCommand.ExecuteAsync(null);
			shown.Add(pane.SessionOptions.AutoCommit);
		}

		Assert.Equal([AutoCommitSetting.DoNotCommit, AutoCommitSetting.Commit, AutoCommitSetting.NoPreference, AutoCommitSetting.DoNotCommit], shown);
		Assert.Contains("session.setOptions:a:Commit::", _client.Calls);
		Assert.Contains("session.setOptions:a:NoPreference::", _client.Calls);
		Assert.Contains("session.setOptions:a:DoNotCommit::", _client.Calls);
	}

	[Fact]
	public async Task ToolbarIsDisabledUntilTheOptionsAreKnown()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		Assert.True(pane.Instruction.CycleAutoCommitCommand.CanExecute(null));

		pane.SessionOptions.Open(null);   // as while a session opens: nothing is known yet

		Assert.False(pane.SessionOptions.IsKnown);
		Assert.False(pane.Instruction.CycleAutoCommitCommand.CanExecute(null));
		Assert.False(pane.Toggles.ToggleSoundCommand.CanExecute(null));
	}

	[Fact]
	public async Task AutoDocumentAndSound_AreSentAndKept()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();

		await pane.Instruction.ToggleAutoDocumentCommand.ExecuteAsync(null);
		await pane.Toggles.ToggleSoundCommand.ExecuteAsync(null);

		Assert.True(pane.SessionOptions.AutoDocument);
		Assert.False(pane.SessionOptions.PlayCompletionSound);
		Assert.True(_stored.AutoDocument);
		Assert.False(_stored.PlayCompletionSound);
	}

	[Fact]
	public async Task MidRunChange_ShowsTheCorrectionTheDaemonReports()
	{
		StatefulOptions(["[Auto-Commit was set to Commit for this run]"]);
		using var pane = await OpenedAsync(running: true);

		await pane.Instruction.CycleAutoCommitCommand.ExecuteAsync(null);

		Assert.Equal("[Auto-Commit was set to Commit for this run]", pane.Instruction.Notice);
	}

	[Fact]
	public async Task FailedChange_ShowsTheErrorAndReadsTheRealValueAgain()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		var real = _client.SetOptionsHandler!;
		_client.SetOptionsHandler = (id, commit, doc, sound) => commit != null || doc != null || sound != null ? throw new InvalidOperationException("refused") : real(id, commit, doc, sound);

		await pane.Instruction.ToggleAutoDocumentCommand.ExecuteAsync(null);
		await pane.SessionOptions.Idle;

		Assert.StartsWith(Constants.ToolbarTexts.OptionsFailed, pane.Instruction.Notice, StringComparison.Ordinal);
		Assert.False(pane.SessionOptions.AutoDocument);
	}

	[Fact]
	public async Task SwitchingSession_NeverShowsTheOptionsOfTheOtherOne()
	{
		_stored = new SessionOptionsDto(true, true, true);
		StatefulOptions();
		using var pane = await OpenedAsync();
		_client.Sessions["b"] = FakeSession.WithEntries(2);
		_stored = new SessionOptionsDto(null, false, false);

		await pane.OpenAsync("b");
		await pane.SessionOptions.Idle;

		Assert.Equal(AutoCommitSetting.NoPreference, pane.SessionOptions.AutoCommit);
		Assert.False(pane.SessionOptions.AutoDocument);
	}

	// ---- one-shot flags ----

	[Fact]
	public async Task NewBranchAndAutoCompact_TravelWithTheSendAndSwitchOff()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		pane.Instruction.ToggleNewBranchCommand.Execute(null);
		pane.Instruction.ToggleAutoCompactCommand.Execute(null);
		pane.InputText = "go";

		await pane.SendCommand.ExecuteAsync(null);

		var send = Assert.Single(_client.TurnSends);
		Assert.True(send.NewBranch);
		Assert.True(send.AutoCompact);
		Assert.False(pane.Instruction.NewBranch);
		Assert.False(pane.Instruction.AutoCompact);

		pane.InputText = "again";
		await pane.SendCommand.ExecuteAsync(null);
		Assert.False(_client.TurnSends[1].NewBranch);
		Assert.False(_client.TurnSends[1].AutoCompact);
	}

	[Fact]
	public async Task NotAcceptedSend_GivesTheOneShotFlagsBack()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		pane.Instruction.ToggleNewBranchCommand.Execute(null);
		_client.TurnSendHandler = _ => new TurnSendResult("t", false, false);
		pane.InputText = "go";

		await pane.SendCommand.ExecuteAsync(null);

		Assert.True(pane.Instruction.NewBranch);
		Assert.Equal("go", pane.InputText);
	}

	[Fact]
	public async Task FailedSend_GivesTheOneShotFlagsBack()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		pane.Instruction.ToggleAutoCompactCommand.Execute(null);
		_client.TurnSendException = new InvalidOperationException("down");
		pane.InputText = "go";

		await pane.SendCommand.ExecuteAsync(null);

		Assert.True(pane.Instruction.AutoCompact);
	}

	[Fact]
	public async Task OneShotFlags_BelongToTheirSession()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		pane.Instruction.ToggleNewBranchCommand.Execute(null);
		_client.Sessions["b"] = FakeSession.WithEntries(2);

		await pane.OpenAsync("b");
		Assert.False(pane.Instruction.NewBranch);

		await pane.OpenAsync(Node);
		Assert.True(pane.Instruction.NewBranch);
	}

	[Fact]
	public async Task OneShotToggleMidRun_SaysItAppliesToTheNextSend()
	{
		StatefulOptions();
		using var pane = await OpenedAsync(running: true);

		pane.Instruction.ToggleNewBranchCommand.Execute(null);

		Assert.Contains("next send", pane.Instruction.Notice, StringComparison.Ordinal);
		Assert.Contains("enabled", pane.Instruction.Notice, StringComparison.Ordinal);
	}

	// ---- Compact and Terminate ----

	[Fact]
	public async Task Compact_CallsSessionCompactWithThePickedSelection()
	{
		StatefulOptions();
		_client.Catalog = new CatalogResult([new ModelOptionDto("default", "Default", null), new ModelOptionDto("opus", "Opus", ModelProvider.Anthropic)], ["default"], [new ProfileOptionDto("default", "Default")]);
		using var pane = await OpenedAsync();
		await pane.LoadCatalogAsync();
		pane.SelectedModel = pane.Models.Single(m => m.Id == "opus");

		await pane.Instruction.CompactCommand.ExecuteAsync(null);

		Assert.Contains("session.compact:a:opus::", _client.Calls);
	}

	[Fact]
	public async Task Terminate_AsksFirstAndDetachesOnlyWhenConfirmed()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		_dialogs.Answer = false;

		await pane.Instruction.ClearCommand.ExecuteAsync(null);
		Assert.DoesNotContain("session.resetConversation:a", _client.Calls);

		_dialogs.Answer = true;
		await pane.Instruction.ClearCommand.ExecuteAsync(null);
		Assert.Contains("session.resetConversation:a", _client.Calls);
		Assert.Equal(2, _dialogs.Confirms.Count);
		Assert.True(_dialogs.Confirms[0].IsDestructive);
	}

	[Fact]
	public async Task Terminate_RefusedWhileATurnRunsIsSaid()
	{
		StatefulOptions();
		_client.ResetConversationHandler = _ => new SessionResetConversationResult(false);
		using var pane = await OpenedAsync();

		await pane.Instruction.ClearCommand.ExecuteAsync(null);

		Assert.Equal(Constants.ToolbarTexts.ClearRefused, pane.Instruction.Notice);
	}

	// ---- Streaming Session Mode ----

	[Fact]
	public async Task Streaming_ShowsTheStoredModeAndTurnsOnWithoutAsking()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		Assert.True(pane.Toggles.IsStreamingKnown);
		Assert.False(pane.Toggles.IsStreamingMode);

		await pane.Toggles.ToggleStreamingCommand.ExecuteAsync(null);

		Assert.True(pane.Toggles.IsStreamingMode);
		Assert.Contains("session.setStreaming:a:True:", _client.Calls);
		Assert.Empty(_dialogs.Confirms);
	}

	[Fact]
	public async Task Streaming_OffWithALiveProcessAsksAndForcesOnConfirm()
	{
		StatefulOptions();
		_client.SetStreamingHandler = (_, enabled, force) => !enabled && force != true ? new SetStreamingResult(true, true) : new SetStreamingResult(enabled, false);
		using var pane = await OpenedAsync(configure: s => { s.StreamingMode = true; s.StreamingAlive = true; });
		Assert.True(pane.Toggles.IsStreamingMode);
		Assert.True(pane.Toggles.IsStreamingAlive);

		await pane.Toggles.ToggleStreamingCommand.ExecuteAsync(null);

		Assert.Single(_dialogs.Confirms);
		Assert.Contains("session.setStreaming:a:False:True", _client.Calls);
		Assert.False(pane.Toggles.IsStreamingMode);
	}

	[Fact]
	public async Task Streaming_OffCancelledKeepsItOn()
	{
		StatefulOptions();
		_client.SetStreamingHandler = (_, enabled, force) => !enabled && force != true ? new SetStreamingResult(true, true) : new SetStreamingResult(enabled, false);
		_dialogs.Answer = false;
		using var pane = await OpenedAsync(configure: s => { s.StreamingMode = true; s.StreamingAlive = true; });

		await pane.Toggles.ToggleStreamingCommand.ExecuteAsync(null);

		Assert.True(pane.Toggles.IsStreamingMode);
		Assert.DoesNotContain("session.setStreaming:a:False:True", _client.Calls);
	}

	// ---- local tools ----

	private static CatalogResult LocalCatalog() => new(
		[new ModelOptionDto("default", "Default", null), new ModelOptionDto("llama", "Llama", ModelProvider.Ollama), new ModelOptionDto("opus", "Opus", ModelProvider.Anthropic)],
		["default"],
		[new ProfileOptionDto("default", "Default")]);

	private static LocalToolsDto Profile(bool enabled = true) =>
		new(enabled, ["Read"], false, [], ["Read", "Write", "Bash"], [new LocalToolsMcpServerDto("srv", "Server")]);

	[Fact]
	public async Task LocalTools_ShowOnlyForALocalModel()
	{
		StatefulOptions();
		_client.Catalog = LocalCatalog();
		_client.LocalToolsGetHandler = _ => Profile();
		using var pane = await OpenedAsync();
		await pane.LoadCatalogAsync();
		Assert.False(pane.LocalTools.IsVisible);

		pane.SelectedModel = pane.Models.Single(m => m.Id == "llama");
		Assert.True(pane.LocalTools.IsVisible);

		pane.SelectedModel = pane.Models.Single(m => m.Id == "opus");
		Assert.False(pane.LocalTools.IsVisible);
	}

	[Fact]
	public async Task LocalTools_ReadTheProfileOfTheSessionsDirectoryOnce()
	{
		StatefulOptions();
		_client.LocalToolsGetHandler = _ => Profile(enabled: false);
		using var pane = await OpenedAsync(configure: s => s.DirectoryId = "d1");

		Assert.Contains("localTools.get:d1", _client.Calls);
		Assert.False(pane.LocalTools.IsEnabled);
		Assert.Equal(["Read", "Write", "Bash"], pane.LocalTools.NativeTools.Select(t => t.Id));
		Assert.Equal(["Read"], pane.LocalTools.NativeTools.Where(t => t.IsChecked).Select(t => t.Id));
		Assert.Single(pane.LocalTools.McpServers);
	}

	[Fact]
	public async Task LocalTools_ShortClickFlipsTheMasterSwitchAndStoresIt()
	{
		StatefulOptions();
		_client.LocalToolsGetHandler = _ => Profile();
		IReadOnlyList<string>? sentTools = null;
		var sentEnabled = true;
		_client.LocalToolsSetHandler = (_, enabled, tools, cmx, servers) =>
		{
			sentEnabled = enabled;
			sentTools = tools;
			return new LocalToolsDto(enabled, tools, cmx, servers, ["Read", "Write", "Bash"], []);
		};
		using var pane = await OpenedAsync();

		await pane.LocalTools.ToggleEnabledCommand.ExecuteAsync(null);

		Assert.False(sentEnabled);
		Assert.Equal(["Read"], sentTools);
		Assert.False(pane.LocalTools.IsEnabled);
		Assert.Equal(Constants.ToolbarTexts.LocalToolsOffTip, pane.LocalTools.Tooltip);
	}

	[Fact]
	public async Task LocalTools_TickingAToolStoresTheSelectionAndKeepsThePopupOpen()
	{
		StatefulOptions();
		_client.LocalToolsGetHandler = _ => Profile();
		IReadOnlyList<string>? sentTools = null;
		_client.LocalToolsSetHandler = (_, enabled, tools, cmx, servers) =>
		{
			sentTools = tools;
			return new LocalToolsDto(enabled, tools, cmx, servers, ["Read", "Write", "Bash"], []);
		};
		using var pane = await OpenedAsync();
		pane.LocalTools.TogglePopupCommand.Execute(null);

		pane.LocalTools.NativeTools.Single(t => t.Id == "Bash").IsChecked = true;
		await pane.LocalTools.Idle;

		Assert.Equal(["Read", "Bash"], sentTools);
		Assert.True(pane.LocalTools.IsPopupOpen);
		Assert.True(pane.LocalTools.IsEnabled);   // the checklist never flips the master switch
	}

	[Fact]
	public async Task LocalTools_FailedStoreShowsTheDaemonsValuesAgain()
	{
		StatefulOptions();
		_client.LocalToolsGetHandler = _ => Profile();
		_client.LocalToolsSetHandler = (_, _, _, _, _) => throw new InvalidOperationException("no");
		using var pane = await OpenedAsync();

		await pane.LocalTools.ToggleEnabledCommand.ExecuteAsync(null);

		Assert.True(pane.LocalTools.IsEnabled);
		Assert.StartsWith(Constants.ToolbarTexts.LocalToolsFailed, pane.Instruction.Notice, StringComparison.Ordinal);
	}

	[Fact]
	public async Task LocalTools_PopupClosesWhenASessionOpens()
	{
		StatefulOptions();
		_client.LocalToolsGetHandler = _ => Profile();
		using var pane = await OpenedAsync();
		pane.LocalTools.TogglePopupCommand.Execute(null);
		_client.Sessions["b"] = FakeSession.WithEntries(2);

		await pane.OpenAsync("b");

		Assert.False(pane.LocalTools.IsPopupOpen);
	}

	// ---- review fixes (W4 review, findings 2 to 6) ----

	[Fact]
	public async Task AutoCompact_AllowsSendingAnEmptyPromptThatOnlyCompacts()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		Assert.False(pane.SendCommand.CanExecute(null));   // empty box, nothing to compact

		pane.Instruction.AutoCompactChecked = true;
		Assert.True(pane.SendCommand.CanExecute(null));
		await pane.SendCommand.ExecuteAsync(null);

		var send = Assert.Single(_client.TurnSends);
		Assert.Equal(string.Empty, send.Text);
		Assert.True(send.AutoCompact);
		Assert.False(pane.Instruction.AutoCompact);        // switched off with the send
		Assert.False(pane.SendCommand.CanExecute(null));    // and an empty box cannot be sent again
	}

	[Fact]
	public async Task AutoDocumentChecked_IsNotifiedWhenTheValueChangesUnderneath()
	{
		StatefulOptions();
		using var pane = await OpenedAsync();
		var notified = new List<string?>();
		pane.Instruction.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
		_stored = new SessionOptionsDto(false, true, true);   // another client changed it

		await pane.SessionOptions.RefreshAsync();

		Assert.True(pane.Instruction.AutoDocumentChecked);
		Assert.Contains(string.Empty, notified);   // "everything changed": the toolbar refreshes all its bindings
	}

	[Fact]
	public async Task FailedFirstRead_RetriesOnceAndClearsTheNotice()
	{
		StatefulOptions();
		var real = _client.SetOptionsHandler!;
		var reads = 0;
		_client.SetOptionsHandler = (id, c, d, s) => ++reads == 1 ? throw new InvalidOperationException("down") : real(id, c, d, s);
		using var pane = await OpenedAsync(beforeOpen: p => p.SessionOptions.RetryDelay = TimeSpan.Zero);

		Assert.Equal(2, reads);
		Assert.True(pane.SessionOptions.IsKnown);
		Assert.Equal(string.Empty, pane.SessionOptions.Notice);
	}

	[Fact]
	public async Task FailedFirstRead_StaysDisabledWithANoticeWhenTheRetryFailsToo()
	{
		StatefulOptions();
		var reads = 0;
		_client.SetOptionsHandler = (_, _, _, _) =>
		{
			reads++;
			throw new InvalidOperationException("down");
		};
		using var pane = await OpenedAsync(beforeOpen: p => p.SessionOptions.RetryDelay = TimeSpan.Zero);

		Assert.Equal(2, reads);   // once, and one retry
		Assert.False(pane.SessionOptions.IsKnown);
		Assert.Equal(Constants.ToolbarTexts.OptionsReadFailed, pane.SessionOptions.Notice);
	}

	[Fact]
	public async Task WithoutADialogService_TerminateAndStreamingOffAreNotConfirmed()
	{
		StatefulOptions();
		_client.SetStreamingHandler = (_, enabled, force) => !enabled && force != true ? new SetStreamingResult(true, true) : new SetStreamingResult(enabled, false);
		using var pane = await OpenedAsync(configure: s => s.StreamingMode = true);
		pane.Dialogs = null;

		await pane.Instruction.ClearCommand.ExecuteAsync(null);
		await pane.Toggles.ToggleStreamingCommand.ExecuteAsync(null);

		Assert.DoesNotContain("session.resetConversation:a", _client.Calls);
		Assert.DoesNotContain("session.setStreaming:a:False:True", _client.Calls);
		Assert.True(pane.Toggles.IsStreamingMode);
	}

	[Fact]
	public async Task Streaming_AStaleAnswerIsNeverApplied()
	{
		StatefulOptions();
		var firstGate = new TaskCompletionSource<SetStreamingResult>();
		var calls = 0;
		_client.SetStreamingAsyncHandler = (enabled, _) => ++calls == 1 ? firstGate.Task : Task.FromResult(new SetStreamingResult(enabled, false));
		using var pane = await OpenedAsync();

		pane.Toggles.StreamingChecked = true;    // call 1: held back
		pane.Toggles.StreamingChecked = false;   // call 2: answers at once (off)
		await pane.Toggles.Idle;
		firstGate.SetResult(new SetStreamingResult(true, false));   // the answer of call 1 arrives last
		await Task.Yield();

		Assert.False(pane.Toggles.IsStreamingMode);
	}

	[Fact]
	public async Task LocalTools_AStaleStoreAnswerNeverBecomesTheRevertTarget()
	{
		StatefulOptions();
		_client.LocalToolsGetHandler = _ => Profile();
		var firstGate = new TaskCompletionSource<LocalToolsDto>();
		var calls = 0;
		LocalToolsDto Dto(params string[] tools) => new(true, tools, false, [], ["Read", "Write", "Bash"], []);
		_client.LocalToolsSetAsyncHandler = _ => ++calls switch
		{
			1 => firstGate.Task,                                          // tick Bash: held back
			2 => Task.FromResult(Dto("Read", "Bash", "Write")),           // tick Write: answers at once
			_ => Task.FromException<LocalToolsDto>(new InvalidOperationException("no")),
		};
		using var pane = await OpenedAsync();

		pane.LocalTools.NativeTools.Single(t => t.Id == "Bash").IsChecked = true;
		pane.LocalTools.NativeTools.Single(t => t.Id == "Write").IsChecked = true;
		await pane.LocalTools.Idle;
		firstGate.SetResult(Dto("Read", "Bash"));   // the stale answer of the first store arrives last
		await Task.Yield();
		pane.LocalTools.NativeTools.Single(t => t.Id == "Read").IsChecked = false;   // fails: the list goes back to what the daemon last stored
		await pane.LocalTools.Idle;

		Assert.Equal(["Bash", "Read", "Write"], pane.LocalTools.NativeTools.Where(t => t.IsChecked).Select(t => t.Id).Order());
	}

	// ---- the slots ----

	[Fact]
	public void Module_ContributesTheToolbarAndTheToggles()
	{
		var slots = new ShellSlots();
		new ToolbarShellModule().Register(slots, null!);

		Assert.Single(slots.For(Constants.Slots.TitleBarToolbar));   // the toolbar is in the title bar
		var bar = slots.For(Constants.Slots.CommandBar);
		Assert.Single(bar);
		Assert.True(bar[0].Order > 10);   // after the selectors
	}
}
