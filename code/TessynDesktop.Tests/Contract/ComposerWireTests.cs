using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Package J2 on the wire: the capability flags, the new <c>turn.send</c> flags, <c>draft.*</c> and <c>autocomplete.suggest</c> (golden shapes over a
/// real loopback server), and that every new method is loopback only: the relay answers -32601.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerWireTests : IDisposable
{
	private readonly ComposerWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private async Task<JsonNode> CallAsync(string method, JsonObject @params) => await _env.Host.CallAsync(method, @params);

	private JsonObject P(params (string Key, JsonNode? Value)[] pairs)
	{
		var obj = new JsonObject { ["nodeId"] = _env.NodeId };
		foreach (var (key, value) in pairs)
			obj[key] = value;
		return obj;
	}

	private static int ErrorCode(JsonNode response) => response["error"]!["code"]!.GetValue<int>();

	// -- Capabilities and loopback ------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Hello_AnnouncesTheFourComposerCapabilities_WhenTheirServicesAreWired()
	{
		var result = (await _env.Host.CallAsync("client.hello"))["result"]!;

		var capabilities = result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		Assert.Equal(
			["events.local", "events.resume", "turn.stream", "turn.options", "session.authError", "drafts", "autocomplete"], capabilities);
	}

	[Fact]
	public async Task Hello_LeavesOutDraftsAndAutocomplete_WhenTheirServicesAreMissing()
	{
		using var env = new ComposerWireEnv(withDrafts: false, withCodeIndex: false);

		var result = (await env.Host.CallAsync("client.hello"))["result"]!;

		var capabilities = result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		Assert.DoesNotContain("drafts", capabilities);
		Assert.DoesNotContain("autocomplete", capabilities);
		Assert.Contains("turn.options", capabilities);
	}

	[Theory]
	[InlineData("draft.get")]
	[InlineData("draft.set")]
	[InlineData("draft.delete")]
	[InlineData("autocomplete.suggest")]
	public async Task TheNewMethods_AreLoopbackOnly_TheRelayAnswersMethodNotFound(string method)
	{
		var body = new JsonObject
		{
			["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method,
			["params"] = new JsonObject { ["nodeId"] = _env.NodeId, ["text"] = "x", ["caret"] = 1 },
		}.ToJsonString();

		var response = await _env.Dispatcher.HandleTextAsync(body, TdcpTransport.Relay, CancellationToken.None);

		Assert.Equal(-32601, ErrorCode(response!));
	}

	// -- turn.send flags ----------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task TurnSend_AutoCompact_CompactsAfterTheAnswer_OnTheWire()
	{
		_env.Process.ScriptEvents(Evt.Assistant("a"), Evt.Result());

		var sent = await CallAsync("turn.send", P(("text", "Hi"), ("autoCompact", true)));
		Assert.True(sent["result"]!["accepted"]!.GetValue<bool>());
		await Waits.Until(() => _env.Compaction.Calls.Count == 1);

		Assert.Equal("compactHeld", _env.Compaction.Calls[0].Variant);
		Assert.Equal(0, _env.Compaction.Calls[0].LockCount);
	}

	[Fact]
	public async Task TurnSend_EmptyTextWithAutoCompact_IsAcceptedAndOnlyCompacts()
	{
		var sent = await CallAsync("turn.send", P(("text", ""), ("autoCompact", true)));

		Assert.True(sent["result"]!["accepted"]!.GetValue<bool>());
		Assert.False(sent["result"]!["queued"]!.GetValue<bool>());
		await Waits.Until(() => _env.Compaction.Calls.Count == 1);
		Assert.Equal("compactHeld", _env.Compaction.Calls[0].Variant); // the engine took the lock itself
		Assert.Empty(_env.Process.Calls);
	}

	// -- A compaction-only turn.send is a turn of its own on the stream (V2) ----------------------------------------------------

	private async Task<(JsonNode Started, JsonNode Completed)> CompactionOnlyAsync(SessionCompactionOutcome outcome, string? error)
	{
		_env.Compaction.Result = new SessionCompactionResultModel(outcome, error, "[" + outcome + "]", SessionDetached: false);
		using var sse = await _env.Host.OpenStreamAsync();

		var sent = await CallAsync("turn.send", P(("text", ""), ("autoCompact", true), ("clientRequestId", "req-c")));
		var turnId = sent["result"]!["turnId"]!.GetValue<string>();

		var frames = await TurnWireEnv.ReadUntilCompletedAsync(sse, turnId);
		var started = frames.Single(f => f.Method == "turn.started");
		Assert.Equal(("req-c", turnId), (started.Params!["clientRequestId"]!.GetValue<string>(), started.Params["turnId"]!.GetValue<string>()));
		Assert.Equal("turn.started", frames[0].Method);
		return (started.Params, frames[^1].Params!);
	}

	[Fact]
	public async Task CompactionOnly_Success_PublishesStartedThenCompleted_WithoutAnError()
	{
		var (_, completed) = await CompactionOnlyAsync(SessionCompactionOutcome.Compacted, null);

		Assert.False(completed["cancelled"]!.GetValue<bool>());
		Assert.Null(completed["error"]);
		Assert.Empty(_env.Process.Calls);
	}

	[Fact]
	public async Task CompactionOnly_NothingToCompact_IsNoError_AndNothingIsAppended()
	{
		var (_, completed) = await CompactionOnlyAsync(SessionCompactionOutcome.NothingToCompact, null);

		Assert.Null(completed["error"]);
		Assert.DoesNotContain(_env.Files.Log, line => line.StartsWith("append:SYSTEM", StringComparison.Ordinal));
	}

	[Fact]
	public async Task CompactionOnly_Failure_CompletesWithTheError_AndAppendsTheSystemLine()
	{
		var (_, completed) = await CompactionOnlyAsync(SessionCompactionOutcome.Failed, "the cli died");

		Assert.Equal("the cli died", completed["error"]!.GetValue<string>());
		Assert.Contains("append:SYSTEM:[Failed]", _env.Files.Log);
	}

	[Fact]
	public async Task CompactionOnly_FailureWithoutAnErrorText_UsesTheSystemLineAsTheError()
	{
		var (_, completed) = await CompactionOnlyAsync(SessionCompactionOutcome.Failed, null);

		Assert.Equal("[Failed]", completed["error"]!.GetValue<string>());
	}

	[Fact]
	public async Task CompactionOnly_StopOnTheWire_EndsTheTurnAsCancelled()
	{
		_env.Compaction.Gate = new TaskCompletionSource();
		using var sse = await _env.Host.OpenStreamAsync();
		var sent = await CallAsync("turn.send", P(("text", ""), ("autoCompact", true)));
		await _env.Compaction.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

		var stopped = await CallAsync("turn.stop", P());
		var frames = await TurnWireEnv.ReadUntilCompletedAsync(sse, sent["result"]!["turnId"]!.GetValue<string>());

		Assert.True(stopped["result"]!["stopped"]!.GetValue<bool>());
		Assert.True(frames[^1].Params!["cancelled"]!.GetValue<bool>());
	}

	[Fact]
	public async Task CompactionOnly_StoppedWhileItWaitsForTheLock_CompletesAsNeverRan()
	{
		var turnLock = _env.Turns.GetTurnLock(_env.NodeId);
		await turnLock.WaitAsync(); // another turn holds the node
		using var sse = await _env.Host.OpenStreamAsync();
		var sent = await CallAsync("turn.send", P(("text", ""), ("autoCompact", true)));
		await Task.Delay(150);

		await CallAsync("turn.stop", P());
		var frames = await TurnWireEnv.ReadUntilCompletedAsync(sse, sent["result"]!["turnId"]!.GetValue<string>());
		turnLock.Release();

		var completed = frames[^1].Params!;
		Assert.True(completed["neverRan"]!.GetValue<bool>());
		Assert.Empty(_env.Compaction.Calls);
	}

	[Fact]
	public async Task TurnSend_AutoCompactWithoutTextParam_IsAccepted_ButBlankTextWithoutItStaysAnError()
	{
		var withFlag = await CallAsync("turn.send", P(("autoCompact", true)));
		var blank    = await CallAsync("turn.send", P(("text", "  ")));
		var missing  = await CallAsync("turn.send", P(("autoCompact", false)));

		Assert.True(withFlag["result"]!["accepted"]!.GetValue<bool>());
		Assert.Equal(-32602, ErrorCode(blank));
		Assert.Equal(-32602, ErrorCode(missing));
	}

	[Fact]
	public async Task TurnSend_ANonBooleanFlag_IsInvalidParams()
	{
		var response = await CallAsync("turn.send", P(("text", "x"), ("newBranch", "yes")));

		Assert.Equal(-32602, ErrorCode(response));
	}

	[Fact]
	public async Task TurnSend_NewBranch_ReachesTheInstructionBlock_AndIsPerRun()
	{
		_env.Process.ScriptEvents(Evt.Result());
		_env.Process.ScriptEvents(Evt.Result());

		await CallAsync("turn.send", P(("text", "one"), ("newBranch", true)));
		await Waits.Until(() => _env.Process.Calls.Count == 1);
		await Waits.Until(() => _env.Files.Completions.Count == 1);
		await CallAsync("turn.send", P(("text", "two")));
		await Waits.Until(() => _env.Process.Calls.Count == 2);

		// The line of the New Branch toggle (FR.11.4) is in the first turn's instruction block only.
		Assert.Contains(Constants.Instructions.NewBranch, _env.Process.Calls[0].Message);
		Assert.DoesNotContain(Constants.Instructions.NewBranch, _env.Process.Calls[1].Message);
	}

	// -- Drafts -------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Drafts_GetSetDelete_HaveTheGoldenShapes_AndRoundTrip()
	{
		var empty   = await CallAsync("draft.get", P());
		var set     = await CallAsync("draft.set", P(("text", "hello"), ("baseRevision", RemoteProtocol.Composer.EmptyDraftRevision)));
		var read    = await CallAsync("draft.get", P());
		var deleted = await CallAsync("draft.delete", P());
		var gone    = await CallAsync("draft.delete", P());

		AssertGolden("draft.get.empty", empty["result"]!);
		AssertGolden("draft.set", set["result"]!);
		AssertGolden("draft.get", read["result"]!);
		AssertGolden("draft.delete", deleted["result"]!);
		Assert.False(gone["result"]!["deleted"]!.GetValue<bool>());
		Assert.Null(_env.Drafts.LoadDraft(_env.Session.FileName));
	}

	[Fact]
	public async Task DraftSet_WithAStaleBaseRevision_IsDaemonErrorDraftChanged_AndKeepsTheStoredText()
	{
		_env.Drafts.SaveDraft(_env.Session.FileName, "typed in the desktop composer");

		var response = await CallAsync("draft.set", P(("text", "mine"), ("baseRevision", RemoteProtocol.Composer.EmptyDraftRevision)));

		Assert.Equal(-32000, ErrorCode(response));
		Assert.Equal("Draft changed", response["error"]!["message"]!.GetValue<string>());
		Assert.Equal("typed in the desktop composer", _env.Drafts.LoadDraft(_env.Session.FileName));
	}

	[Fact]
	public async Task DraftSet_WithoutBaseRevision_Overwrites_AndWithTheCurrentOne_Succeeds()
	{
		var first  = await CallAsync("draft.set", P(("text", "one")));
		var second = await CallAsync("draft.set", P(("text", "two"), ("baseRevision", first["result"]!["revision"]!.GetValue<string>())));
		var third  = await CallAsync("draft.set", P(("text", "three")));

		Assert.Null(second["error"]);
		Assert.Null(third["error"]);
		Assert.Equal("three", _env.Drafts.LoadDraft(_env.Session.FileName));
	}

	[Fact]
	public async Task DraftSet_WithAnEmptyBaseRevisionString_IsNotTheSameAsNone_ItIsAStaleRevision()
	{
		_env.Drafts.SaveDraft(_env.Session.FileName, "stored");

		var response = await CallAsync("draft.set", P(("text", "mine"), ("baseRevision", "")));

		Assert.Equal(-32000, ErrorCode(response));
		Assert.Equal("stored", _env.Drafts.LoadDraft(_env.Session.FileName));
	}

	[Fact]
	public async Task DraftSet_EmptyText_DeletesTheDraft_AndAnswersTheEmptyRevision()
	{
		_env.Drafts.SaveDraft(_env.Session.FileName, "something");

		var response = await CallAsync("draft.set", P(("text", "")));

		Assert.Equal(RemoteProtocol.Composer.EmptyDraftRevision, response["result"]!["revision"]!.GetValue<string>());
		Assert.Null(_env.Drafts.LoadDraft(_env.Session.FileName));
	}

	[Fact]
	public async Task TheRevisionIsTheSameForTheSameText_SoTheTwoReadersAgree()
	{
		var set  = await CallAsync("draft.set", P(("text", "same")));
		var read = await CallAsync("draft.get", P());

		Assert.Equal(set["result"]!["revision"]!.GetValue<string>(), read["result"]!["revision"]!.GetValue<string>());
	}

	[Fact]
	public async Task Drafts_Validation_UnknownSessionAndMissingParams()
	{
		var unknown     = await _env.Host.CallAsync("draft.get", new JsonObject { ["nodeId"] = "nope" });
		var missingText = await CallAsync("draft.set", P());
		var missingNode = await _env.Host.CallAsync("draft.delete", new JsonObject());

		Assert.Equal(-32000, ErrorCode(unknown));
		Assert.Equal(-32602, ErrorCode(missingText));
		Assert.Equal(-32602, ErrorCode(missingNode));
	}

	// -- Autocomplete -------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Autocomplete_FileMode_HasTheGoldenShape_OnceTheIndexIsReady()
	{
		_env.CodeIndex.Files = [new IndexedFileModel { FileName = "Foo.cs", RelativePath = @"src\Foo.cs", Root = _env.ProjectDirectory }];
		_env.CodeIndex.SetReady();

		var response = await CallAsync("autocomplete.suggest", P(("text", "see ##Foo"), ("caret", 9)));

		AssertGolden("autocomplete.suggest.file", response["result"]!);
	}

	[Fact]
	public async Task Autocomplete_SymbolMode_HasTheGoldenShape()
	{
		_env.CodeIndex.Symbols =
		[
			new CodeSymbolModel { Name = "Foo", FullyQualifiedName = "Foo", Namespace = "My.Space", Kind = CodeSymbolKind.Class, FilePath = "Foo.cs", Line = 3 },
		];
		_env.CodeIndex.SetReady();

		var response = await CallAsync("autocomplete.suggest", P(("text", "#Fo"), ("caret", 3)));

		AssertGolden("autocomplete.suggest.symbol", response["result"]!);
	}

	[Fact]
	public async Task Autocomplete_PathMode_ListsDirectoriesFirst_ThenFiles()
	{
		var root = Path.Combine(_env.ProjectDirectory, "listing");
		Directory.CreateDirectory(Path.Combine(root, "alpha"));
		File.WriteAllText(Path.Combine(root, "alpha.txt"), "x");
		File.WriteAllText(Path.Combine(root, "beta.txt"), "x");
		var text = root + "\\al";

		var response = await CallAsync("autocomplete.suggest", P(("text", text), ("caret", text.Length)));

		var result = response["result"]!;
		Assert.Equal("path", result["mode"]!.GetValue<string>());
		var items = result["items"]!.AsArray();
		Assert.Equal(["directory", "file"], items.Select(i => i!["kind"]!.GetValue<string>()));
		Assert.Equal(root + "\\alpha\\", items[0]!["insertText"]!.GetValue<string>());
		Assert.Equal(root + "\\alpha.txt", items[1]!["insertText"]!.GetValue<string>());
		Assert.Equal(".txt", items[1]!["detail"]!.GetValue<string>());
	}

	[Fact]
	public async Task Autocomplete_PathMode_LooksAtABoundedNumberOfEntries_InAHugeDirectory()
	{
		var root = Path.Combine(_env.ProjectDirectory, "huge");
		Directory.CreateDirectory(root);
		for (var i = 0; i < Constants.Composer.MaxPathEntriesExamined + 100; i++)
			File.Create(Path.Combine(root, "a" + i.ToString("D5"))).Dispose();
		File.Create(Path.Combine(root, "zmatch.txt")).Dispose(); // listed after every non-matching entry
		var text = root + "\\zm";

		var response = await CallAsync("autocomplete.suggest", P(("text", text), ("caret", text.Length)));

		Assert.Empty(response["result"]!["items"]!.AsArray()); // beyond the examined bound: the scan stopped, it did not walk the whole directory
		var small = Path.Combine(_env.ProjectDirectory, "small");
		Directory.CreateDirectory(small);
		File.Create(Path.Combine(small, "zmatch.txt")).Dispose();
		var smallText = small + "\\zm";
		var found = await CallAsync("autocomplete.suggest", P(("text", smallText), ("caret", smallText.Length)));
		Assert.Single(found["result"]!["items"]!.AsArray());
	}

	[Fact]
	public async Task Autocomplete_NoTrigger_AnswersModeNone()
	{
		var response = await CallAsync("autocomplete.suggest", P(("text", "plain words"), ("caret", 5)));

		AssertGolden("autocomplete.suggest.none", response["result"]!);
	}

	[Fact]
	public async Task Autocomplete_AnIndexThatIsStillBuilding_IsNotWaitedFor_TheAnswerIsNotReady_AndTheBuildContinues()
	{
		var response = await CallAsync("autocomplete.suggest", P(("text", "##Foo"), ("caret", 5))).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.False(response["result"]!["indexReady"]!.GetValue<bool>());
		Assert.Empty(response["result"]!["items"]!.AsArray());
		Assert.Equal("file", response["result"]!["mode"]!.GetValue<string>());
		await Waits.Until(() => _env.CodeIndex.BuildStarts == 1);

		// The next request while it builds asks nothing more of the index; once it is built the answer is ready.
		await CallAsync("autocomplete.suggest", P(("text", "##Foo"), ("caret", 5)));
		Assert.Equal(1, _env.CodeIndex.BuildStarts);
		_env.CodeIndex.FinishBuild();
		await Waits.Until(() => _env.CodeIndex.Acquired == 1);
		var ready = await CallAsync("autocomplete.suggest", P(("text", "##Foo"), ("caret", 5)));
		Assert.True(ready["result"]!["indexReady"]!.GetValue<bool>());
	}

	[Fact]
	public async Task Autocomplete_Validation_CaretIsRequired_UnknownSessionIsAnError()
	{
		var noCaret = await CallAsync("autocomplete.suggest", P(("text", "#a")));
		var unknown = await _env.Host.CallAsync("autocomplete.suggest", new JsonObject { ["nodeId"] = "nope", ["text"] = "#a", ["caret"] = 2 });

		Assert.Equal(-32602, ErrorCode(noCaret));
		Assert.Equal(-32000, ErrorCode(unknown));
	}

	// -- Golden files -------------------------------------------------------------------------------------------------------------

	private static void AssertGolden(string name, JsonNode result, [CallerFilePath] string thisFile = "")
	{
		var actual = result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n");
		var path = Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");

		// Paths that came from the temp dir are not part of the shape.
		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
			File.WriteAllText(path, actual + "\n");

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd(), actual.TrimEnd());
	}
}
