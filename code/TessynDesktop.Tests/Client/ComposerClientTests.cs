using System.Text.Json;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Client;

/// <summary>The package J2 methods of <see cref="TessynClient"/> (turn options, drafts, autocomplete, the auth error flag) against a real loopback daemon.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerClientTests : IDisposable
{
	private readonly ComposerWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private TessynClient NewClient() =>
		new(new Uri(_env.Host.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token);

	[Fact]
	public async Task Connect_ExposesTheComposerCapabilities()
	{
		await using var client = NewClient();

		await client.ConnectAsync();

		var capabilities = client.ServerInfo!.Capabilities;
		Assert.Contains(RemoteProtocol.Composer.TurnOptionsCapability, capabilities);
		Assert.Contains(RemoteProtocol.Composer.AuthErrorCapability, capabilities);
		Assert.Contains(RemoteProtocol.Composer.DraftsCapability, capabilities);
		Assert.Contains(RemoteProtocol.Composer.AutocompleteCapability, capabilities);
	}

	// -- turn.send flags ----------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task TurnSendAsync_AutoCompact_CompactsAfterTheAnswer()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		_env.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		var sent = await client.TurnSendAsync(_env.NodeId, "Hi", autoCompact: true);

		Assert.True(sent.Accepted);
		await Waits.Until(() => _env.Compaction.Calls.Count == 1);
		Assert.Equal("compactHeld", _env.Compaction.Calls[0].Variant);
	}

	[Fact]
	public async Task TurnSendAsync_EmptyTextWithAutoCompact_OnlyCompacts()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var sent = await client.TurnSendAsync(_env.NodeId, string.Empty, autoCompact: true);

		Assert.True(sent.Accepted);
		await Waits.Until(() => _env.Compaction.Calls.Count == 1);
		Assert.Equal("compactHeld", _env.Compaction.Calls[0].Variant); // the engine took the lock itself
		Assert.Empty(_env.Process.Calls);
	}

	[Fact]
	public async Task TurnSendAsync_NewBranch_AddsItsInstructionLine_ToThatTurnOnly()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		_env.Process.ScriptEvents(Evt.Result());
		_env.Process.ScriptEvents(Evt.Result());

		await client.TurnSendAsync(_env.NodeId, "one", newBranch: true);
		await Waits.Until(() => _env.Files.Completions.Count == 1);
		await client.TurnSendAsync(_env.NodeId, "two");
		await Waits.Until(() => _env.Process.Calls.Count == 2);

		Assert.Contains(Constants.Instructions.NewBranch, _env.Process.Calls[0].Message);
		Assert.DoesNotContain(Constants.Instructions.NewBranch, _env.Process.Calls[1].Message);
	}

	[Fact]
	public async Task TurnSendAsync_BlankTextWithoutAutoCompact_IsStillRefused()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.TurnSendAsync(_env.NodeId, "   "));

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, error.Code);
	}

	// -- Drafts -------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Drafts_RoundTrip_ThroughTheClient()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var empty = await client.DraftGetAsync(_env.NodeId);
		var set   = await client.DraftSetAsync(_env.NodeId, "unsent", empty.Revision);
		var read  = await client.DraftGetAsync(_env.NodeId);
		var del   = await client.DraftDeleteAsync(_env.NodeId);
		var after = await client.DraftGetAsync(_env.NodeId);

		Assert.Equal(new DraftGetResult(string.Empty, RemoteProtocol.Composer.EmptyDraftRevision), empty);
		Assert.Equal("unsent", read.Text);
		Assert.Equal(set.Revision, read.Revision);
		Assert.True(del.Deleted);
		Assert.Equal(empty, after);
	}

	[Fact]
	public async Task ReplyDrafts_CarryTheQuestionValues_ThroughTheClient()   // FR.23.37
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		var item = _env.Attention.Service.Post(_env.Session,
		[
			new AttentionPostRequestModel
			{
				Kind      = AttentionKind.Ask,
				Title     = "Plan questions",
				Questions = [new AttentionQuestionModel { Number = 1, Type = AttentionQuestionType.YesNo, Text = "Ship it?" }],
			},
		]).Single().Item;
		List<AttentionQuestionAnswerParams> questions = [new(1, "yes"), new(2, null, ["EU"])];

		var empty = await client.ReplyDraftGetAsync(_env.NodeId, item.Id);
		var set   = await client.ReplyDraftSetAsync(_env.NodeId, item.Id, "my note", empty.Revision, questions);
		var read  = await client.ReplyDraftGetAsync(_env.NodeId, item.Id);

		Assert.Null(empty.Questions);
		Assert.Equal("my note", read.Text);
		Assert.Equal(set.Revision, read.Revision);
		Assert.Equal([1, 2], read.Questions!.Select(q => q.Number));
		Assert.Equal("yes", read.Questions![0].Value);
		Assert.Equal(["EU"], read.Questions![1].Choices);

		// without question values the draft is the old text-only one
		var plain = await client.ReplyDraftSetAsync(_env.NodeId, item.Id, "only a note", read.Revision);
		var back  = await client.ReplyDraftGetAsync(_env.NodeId, item.Id);
		Assert.Equal(plain.Revision, back.Revision);
		Assert.Null(back.Questions);
	}

	[Fact]
	public async Task DraftSetAsync_WithAStaleRevision_ThrowsDraftChanged_AndNothingIsOverwritten()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		var mine = await client.DraftSetAsync(_env.NodeId, "mine");
		_env.Drafts.SaveDraft(_env.Session.FileName, "typed on the desktop");

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.DraftSetAsync(_env.NodeId, "mine, edited", mine.Revision));

		Assert.Equal(RemoteProtocol.ErrorCodes.DaemonError, error.Code);
		Assert.Equal("Draft changed", error.Message);
		Assert.Equal("typed on the desktop", (await client.DraftGetAsync(_env.NodeId)).Text);
	}

	[Fact]
	public async Task DraftSetAsync_EmptyText_DeletesTheDraft()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		await client.DraftSetAsync(_env.NodeId, "x");

		var set = await client.DraftSetAsync(_env.NodeId, string.Empty);

		Assert.Equal(RemoteProtocol.Composer.EmptyDraftRevision, set.Revision);
		Assert.Null(_env.Drafts.LoadDraft(_env.Session.FileName));
	}

	[Fact]
	public async Task DraftChanged_ArrivesOnTheLocalStream_AsATypedNotification()
	{
		await using var client = NewClient();
		var probe = new ClientProbe(client);
		await client.ConnectAsync();

		_env.Host.Bus.Publish(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(_env.NodeId, "abc123"));

		DraftChangedPayload? changed = null;
		while (changed == null)
		{
			var notification = await probe.NextNotificationAsync();
			Assert.False(notification.TryGetTurnStarted(out _));
			notification.TryGetDraftChanged(out changed);
		}

		Assert.Equal(new DraftChangedPayload(_env.NodeId, "abc123"), changed);
	}

	// -- Autocomplete -------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task AutocompleteSuggestAsync_ReadsTheItemsAndTheTriggerSpan()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		_env.CodeIndex.Files = [new IndexedFileModel { FileName = "Foo.cs", RelativePath = @"src\Foo.cs", Root = _env.ProjectDirectory }];
		_env.CodeIndex.SetReady();

		var result = await client.AutocompleteSuggestAsync(_env.NodeId, "see ##Foo", 9);

		Assert.Equal(RemoteProtocol.Composer.AutocompleteModes.File, result.Mode);
		Assert.Equal((4, 5, true), (result.TriggerStart, result.TriggerLength, result.IndexReady));
		Assert.Equal(new AutocompleteItemDto("Foo.cs", @"src\Foo.cs", "file", @"src\Foo.cs", 0, 3), Assert.Single(result.Items));
	}

	[Fact]
	public async Task AutocompleteSuggestAsync_WhileTheIndexBuilds_AnswersNotReadyWithoutWaiting()
	{
		_env.Settings.Settings.CodeReferencesEnabled = true;   // # is the symbol trigger only while Code references is on (FR.7.13)
		await using var client = NewClient();
		await client.ConnectAsync();

		var result = await client.AutocompleteSuggestAsync(_env.NodeId, "#Foo", 4).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.False(result.IndexReady);
		Assert.Empty(result.Items);
	}

	// -- The session auth error ---------------------------------------------------------------------------------------------------

	[Fact]
	public void SessionStateDto_ReadsAuthError_AndAnOlderDaemonWithoutItMeansNoError()
	{
		const string withFlag = """{"id":"n","running":false,"streamingAlive":false,"favorite":false,"hasDraft":false,"authError":true}""";
		const string withoutFlag = """{"id":"n","running":false,"streamingAlive":false,"favorite":false,"hasDraft":false}""";

		Assert.True(JsonSerializer.Deserialize<SessionStateDto>(withFlag, TdcpJsonOptions.Default)!.AuthError);
		Assert.False(JsonSerializer.Deserialize<SessionStateDto>(withoutFlag, TdcpJsonOptions.Default)!.AuthError);
	}
}
