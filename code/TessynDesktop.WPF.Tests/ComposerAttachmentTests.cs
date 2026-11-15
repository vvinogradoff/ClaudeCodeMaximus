using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>File drag-and-drop and paste into the prompt input (FR.31.2): path mode, file mode, folders, cancel and the remembered toggle.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerAttachmentTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-composer-" + Guid.NewGuid().ToString("N"));
	private readonly FakeTessynClient _client = new();
	private readonly List<SessionPaneViewModel> _panes = [];

	public ComposerAttachmentTests()
	{
		Directory.CreateDirectory(_root);
		_client.Sessions["a"] = FakeSession.WithEntries(4, "Session A");
		_client.Sessions["b"] = FakeSession.WithEntries(4, "Session B");
	}

	public void Dispose()
	{
		foreach (var pane in _panes)
			pane.Dispose();

		Directory.Delete(_root, recursive: true);
	}

	private JsonClientStateStore NewStore() =>
		new(new AppPaths(Path.Combine(_root, "data")), NullLogger<JsonClientStateStore>.Instance, TimeSpan.FromMilliseconds(10));

	private async Task<SessionPaneViewModel> OpenPaneAsync(IClientStateStore? store = null, string nodeId = "a")
	{
		var pane = new SessionPaneViewModel(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance,
			Constants.Limits.MaxCachedTranscripts, stateStore: store);
		_panes.Add(pane);
		await pane.OpenAsync(nodeId);
		return pane;
	}

	private string MakeFile(string name)
	{
		var path = Path.Combine(_root, name);
		File.WriteAllText(path, "x");
		return path;
	}

	private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "The condition was not reached in time.");
	}

	// ── path mode (the default) ──────────────────────────────────────────────

	[Fact]
	public async Task PathMode_IsTheDefault_AndInsertsThePathAtTheCaret()
	{
		var pane = await OpenPaneAsync();
		pane.InputText = "look at  now";
		int? caret = null;
		pane.InputCaretRequested += (_, c) => caret = c;

		await pane.HandleFilesAsync([@"C:\work\report.md"], selectionStart: 8, selectionLength: 0);

		Assert.False(pane.AttachAsFiles);
		Assert.Equal(@"look at C:\work\report.md now", pane.InputText);
		Assert.Equal(8 + @"C:\work\report.md".Length, caret);   // the caret sits right after the inserted path
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("attachment.import", StringComparison.Ordinal));
	}

	[Fact]
	public async Task PathMode_ReplacesTheSelection()
	{
		var pane = await OpenPaneAsync();
		pane.InputText = "see [placeholder] please";
		int? caret = null;
		pane.InputCaretRequested += (_, c) => caret = c;

		await pane.HandleFilesAsync([@"C:\a b\c.txt"], selectionStart: 4, selectionLength: "[placeholder]".Length);

		Assert.Equal(@"see C:\a b\c.txt please", pane.InputText);
		Assert.Equal(4 + @"C:\a b\c.txt".Length, caret);
	}

	[Fact]
	public async Task PathMode_SeveralFiles_GoOnePerLine()
	{
		var pane = await OpenPaneAsync();
		pane.InputText = "ab";
		int? caret = null;
		pane.InputCaretRequested += (_, c) => caret = c;

		await pane.HandleFilesAsync([@"C:\one.txt", @"C:\two.txt", @"D:\three.txt"], 1, 0);

		var inserted = Lines(@"C:\one.txt", @"C:\two.txt", @"D:\three.txt");
		Assert.Equal("a" + inserted + "b", pane.InputText);
		Assert.Equal(1 + inserted.Length, caret);
	}

	[Fact]
	public async Task PathMode_AFolder_InsertsItsPath()
	{
		var pane = await OpenPaneAsync();
		var folder = Path.Combine(_root, "some folder");
		Directory.CreateDirectory(folder);

		await pane.HandleFilesAsync([folder], 0, 0);

		Assert.Equal(folder, pane.InputText);
		Assert.Null(pane.Notice);
	}

	[Fact]
	public async Task PathMode_WithNoSession_StillInsertsText()
	{
		using var pane = new SessionPaneViewModel(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);

		await pane.HandleFilesAsync([@"C:\x.txt"], 0, 0);

		Assert.Equal(@"C:\x.txt", pane.InputText);
	}

	[Fact]
	public async Task AnEmptyDrop_ChangesNothing()
	{
		var pane = await OpenPaneAsync();
		pane.InputText = "keep";

		await pane.HandleFilesAsync([], 0, 0);

		Assert.Equal("keep", pane.InputText);
	}

	[Fact]
	public async Task ASelectionBeyondTheText_IsClamped()
	{
		var pane = await OpenPaneAsync();
		pane.InputText = "ab";

		await pane.HandleFilesAsync(["X"], selectionStart: 50, selectionLength: 9);

		Assert.Equal("abX", pane.InputText);
	}

	// ── file mode ────────────────────────────────────────────────────────────

	[Fact]
	public async Task FileMode_CopiesEachFileAndInsertsAttachedFileLines()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		pane.InputText = "Please review: ";
		var one = MakeFile("one.txt");
		var two = MakeFile("two.txt");
		int? caret = null;
		pane.InputCaretRequested += (_, c) => caret = c;

		await pane.HandleFilesAsync([one, two], pane.InputText.Length, 0);

		Assert.Contains($"attachment.import:a:{one}", _client.Calls);
		Assert.Contains($"attachment.import:a:{two}", _client.Calls);
		var pathOne = Path.Combine(Path.GetTempPath(), "attachments", "a", "20260101-000000-one.txt");
		var pathTwo = Path.Combine(Path.GetTempPath(), "attachments", "a", "20260101-000000-two.txt");
		var inserted = Lines("Attached file: " + pathOne, "Attached file: " + pathTwo);
		Assert.Equal("Please review: " + inserted, pane.InputText);
		Assert.Equal("Please review: ".Length + inserted.Length, caret);
		Assert.Null(pane.Notice);
		Assert.False(pane.IsImporting);
		Assert.Equal(2, pane.ImportDone);
	}

	[Fact]
	public async Task FileMode_ReplacesTheSelectionOnceTheCopyIsDone()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		pane.InputText = "x SELECTED y";

		await pane.HandleFilesAsync([MakeFile("f.txt")], 2, "SELECTED".Length);

		Assert.StartsWith("x Attached file: ", pane.InputText);
		Assert.EndsWith(" y", pane.InputText);
		Assert.DoesNotContain("SELECTED", pane.InputText);
	}

	[Fact]
	public async Task FileMode_WhenTheUserTypedDuringTheCopy_NothingIsRemoved()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		pane.InputText = "AB";
		var release = new TaskCompletionSource();
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			await release.Task;
			return new AttachmentDto("att.1", @"C:\att\f.txt", "f.txt", 1, "text/plain", DateTimeOffset.UnixEpoch);
		};

		var task = pane.HandleFilesAsync([MakeFile("f.txt")], 0, 2);   // "AB" is selected when the file is dropped
		await WaitForAsync(() => pane.IsImporting);
		pane.InputText = "AB typed more";
		release.SetResult();
		await task;

		Assert.Equal(@"Attached file: C:\att\f.txtAB typed more", pane.InputText);
	}

	[Fact]
	public async Task FileMode_ARefusedFile_ShowsANoticeAndInsertsNothingForIt()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		var secret = MakeFile("credentials.json");
		var fine = MakeFile("fine.txt");
		_client.AttachmentImportHandler = (node, path, ct) => path == secret
			? throw new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "That file is on the deny list")
			: Task.FromResult(new AttachmentDto("att.2", @"C:\att\fine.txt", "fine.txt", 1, "text/plain", DateTimeOffset.UnixEpoch));

		await pane.HandleFilesAsync([secret, fine], 0, 0);

		Assert.Equal(@"Attached file: C:\att\fine.txt", pane.InputText);
		Assert.Contains("credentials.json", pane.Notice);
		Assert.Contains("That file is on the deny list", pane.Notice);
		Assert.DoesNotContain("fine.txt", pane.Notice);
	}

	[Fact]
	public async Task FileMode_EveryFileRefused_LeavesTheTextAlone()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		pane.InputText = "draft";
		_client.AttachmentImportHandler = (node, path, ct) => throw new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "The file is larger than the 200 MB limit");

		await pane.HandleFilesAsync([MakeFile("big.bin")], 5, 0);

		Assert.Equal("draft", pane.InputText);
		Assert.Contains("200 MB", pane.Notice);
		Assert.Contains("big.bin", pane.Notice);
	}

	[Fact]
	public async Task FileMode_ATransportFailure_IsANoticeToo()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		_client.AttachmentImportHandler = (node, path, ct) => throw new HttpRequestException("The daemon is not reachable");

		await pane.HandleFilesAsync([MakeFile("a.txt")], 0, 0);

		Assert.Equal(string.Empty, pane.InputText);
		Assert.Contains("not reachable", pane.Notice);
	}

	[Fact]
	public async Task FileMode_AFolder_IsRefusedWithoutCallingTheDaemon()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		var folder = Path.Combine(_root, "docs");
		Directory.CreateDirectory(folder);
		var file = MakeFile("ok.txt");

		await pane.HandleFilesAsync([folder, file], 0, 0);

		Assert.DoesNotContain($"attachment.import:a:{folder}", _client.Calls);
		Assert.Contains($"attachment.import:a:{file}", _client.Calls);
		Assert.Contains("docs", pane.Notice);
		Assert.Contains("folder", pane.Notice);
		Assert.DoesNotContain(folder, pane.InputText);
		Assert.StartsWith("Attached file: ", pane.InputText);
	}

	[Fact]
	public async Task FileMode_WithNoSession_ShowsANoticeAndDoesNothing()
	{
		using var pane = new SessionPaneViewModel(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
		pane.AttachAsFiles = true;

		await pane.HandleFilesAsync([MakeFile("a.txt")], 0, 0);

		Assert.Equal(Constants.Texts.AttachNeedsSession, pane.Notice);
		Assert.Equal(string.Empty, pane.InputText);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("attachment.import", StringComparison.Ordinal));
	}

	[Fact]
	public async Task FileMode_ProgressIsShownWhileCopying_AndCancelStopsTheRest()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		var first = MakeFile("first.bin");
		var second = MakeFile("second.bin");
		var third = MakeFile("third.bin");
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			if (path == first)
				return new AttachmentDto("att.f", @"C:\att\first.bin", "first.bin", 1, "application/octet-stream", DateTimeOffset.UnixEpoch);

			await Task.Delay(Timeout.Infinite, ct);   // the second copy is "large": it only ends when cancelled
			throw new InvalidOperationException();
		};
		Assert.False(pane.CancelImportCommand.CanExecute(null));

		var task = pane.HandleFilesAsync([first, second, third], 0, 0);
		await WaitForAsync(() => _client.Calls.Contains($"attachment.import:a:{second}"));

		Assert.True(pane.IsImporting);
		Assert.Equal("Attaching 2 of 3: second.bin", pane.ImportStatus);
		Assert.Equal(3, pane.ImportTotal);
		Assert.Equal(1, pane.ImportDone);
		Assert.True(pane.CancelImportCommand.CanExecute(null));

		pane.CancelImportCommand.Execute(null);
		await task;

		Assert.False(pane.IsImporting);
		Assert.DoesNotContain($"attachment.import:a:{third}", _client.Calls);
		Assert.Equal(@"Attached file: C:\att\first.bin", pane.InputText);   // the copy that finished stays attached
		Assert.Contains(Constants.Texts.AttachCancelled, pane.Notice);
		Assert.Contains("1 file(s) were already attached", pane.Notice);
		Assert.False(pane.CancelImportCommand.CanExecute(null));
	}

	[Fact]
	public async Task FileMode_ADropWhileCopying_IsRefusedWithANotice()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		var release = new TaskCompletionSource();
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			await release.Task;
			return new AttachmentDto("att.1", @"C:\att\a.txt", "a.txt", 1, "text/plain", DateTimeOffset.UnixEpoch);
		};
		var first = pane.HandleFilesAsync([MakeFile("a.txt")], 0, 0);
		await WaitForAsync(() => pane.IsImporting);

		await pane.HandleFilesAsync([MakeFile("b.txt")], 0, 0);

		Assert.Equal(Constants.Texts.AttachBusy, pane.Notice);
		release.SetResult();
		await first;
		Assert.Single(_client.Calls, c => c.StartsWith("attachment.import", StringComparison.Ordinal));
	}

	[Fact]
	public async Task FileMode_SwitchingSessionDuringTheCopy_CancelsItAndKeepsTheOtherDraftClean()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			await Task.Delay(Timeout.Infinite, ct);
			throw new InvalidOperationException();
		};
		var task = pane.HandleFilesAsync([MakeFile("a.txt")], 0, 0);
		await WaitForAsync(() => pane.IsImporting);

		await pane.OpenAsync("b");
		await task;

		Assert.False(pane.IsImporting);
		Assert.Equal(string.Empty, pane.InputText);
	}

	// ── toggle persistence ───────────────────────────────────────────────────

	[Fact]
	public async Task TheToggle_IsRememberedGloballyInTheClientState()
	{
		var first = NewStore();
		await first.LoadAsync();
		var pane = await OpenPaneAsync(first, "a");
		Assert.False(pane.AttachAsFiles);

		pane.AttachAsFiles = true;
		await first.FlushAsync();

		var second = NewStore();
		await second.LoadAsync();
		Assert.True(second.State.AttachAsFiles);
		var other = await OpenPaneAsync(second, "b");   // another pane, another session: the same global setting
		Assert.True(other.AttachAsFiles);

		other.AttachAsFiles = false;
		await second.FlushAsync();
		var third = NewStore();
		await third.LoadAsync();
		Assert.False(third.State.AttachAsFiles);
	}

	[Fact]
	public async Task TheToggle_DoesNotChangeWhenSwitchingSessions()
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = true;

		await pane.OpenAsync("b");

		Assert.True(pane.AttachAsFiles);
	}

	// ── what is read from the drag or the clipboard ──────────────────────────

	[Fact]
	public void FileDropReader_ReadsAFileListFromADataObjectAndNothingElse()
	{
		StaTestRunner.Run(() =>
		{
			var reader = new FileDropReader();
			var files = new System.Windows.DataObject();
			files.SetFileDropList([@"C:\a.txt", @"C:\b folder"]);   // what Explorer puts on the clipboard for Ctrl+C on files
			var text = new System.Windows.DataObject();
			text.SetText(@"C:\a.txt");

			Assert.Equal([@"C:\a.txt", @"C:\b folder"], reader.Read(files));
			Assert.Empty(reader.Read(text));
			Assert.Empty(reader.Read(null));
		});
	}
}
