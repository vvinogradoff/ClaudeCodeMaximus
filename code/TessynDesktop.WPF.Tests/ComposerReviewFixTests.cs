using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>Review fixes of the composer drop (C2-C7 and the test seam): drafts, dispose, typing during a copy, notices, hidden characters.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerReviewFixTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-composer-fix-" + Guid.NewGuid().ToString("N"));
	private readonly FakeTessynClient _client = new();
	private readonly List<SessionPaneViewModel> _panes = [];

	public ComposerReviewFixTests()
	{
		Directory.CreateDirectory(_root);
		_client.Sessions["a"] = FakeSession.WithEntries(4, "Session A");
		_client.Sessions["b"] = FakeSession.WithEntries(4, "Session B");
	}

	public void Dispose()
	{
		foreach (var pane in _panes)
			pane.Dispose();

		TestDirectory.Delete(_root);
	}

	private async Task<SessionPaneViewModel> OpenPaneAsync(Func<string, bool>? probe = null, IReadOnlyList<string>? testDrop = null)
	{
		var pane = new SessionPaneViewModel(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			DirectoryProbe = probe ?? Directory.Exists,
			TestDropPaths = testDrop ?? [],
		};
		_panes.Add(pane);
		await pane.OpenAsync("a");
		pane.AttachAsFiles = true;
		return pane;
	}

	private string MakeFile(string name)
	{
		var path = Path.Combine(_root, name);
		File.WriteAllText(path, "x");
		return path;
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "The condition was not reached in time.");
	}

	private static AttachmentDto Stored(string path) => new("att.x", path, Path.GetFileName(path), 1, "text/plain", DateTimeOffset.UnixEpoch);

	// ── C2: the old session's draft is never damaged ─────────────────────────

	[Fact]
	public Task C2_SwitchingAwayAfterTypingKeepsTheOldDraftAndAddsTheCopiedFileOnANewLine() => UiThread.RunAsync(async () =>
	{
		// On one UI-like thread: the copy that ends when the pane leaves the session resumes after the switch step that awaited, as it does under the WPF dispatcher,
		// instead of racing it on the thread pool (where it could change the box after the draft was saved and the box was replaced).
		var pane = await OpenPaneAsync();
		pane.InputText = "SELECTED tail";
		var first = MakeFile("first.txt");
		var second = MakeFile("second.txt");
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			if (path == first)
				return Stored(@"C:\att\first.txt");

			await Task.Delay(Timeout.Infinite, ct);
			throw new InvalidOperationException();
		};
		var task = pane.HandleFilesAsync([first, second], 0, "SELECTED".Length);
		await WaitForAsync(() => _client.Calls.Contains($"attachment.import:a:{second}"));
		pane.InputText = "SELECTED tail and more typing";

		await pane.OpenAsync("b");
		await task;

		Assert.Equal(string.Empty, pane.InputText);
		Assert.Null(pane.Notice);   // C5: nothing about A's copy is shown in B
		await pane.OpenAsync("a");
		Assert.Equal("SELECTED tail and more typing" + Environment.NewLine + @"Attached file: C:\att\first.txt", pane.InputText);
	});

	// ── C3: dispose stops the copy ───────────────────────────────────────────

	[Fact]
	public async Task C3_DisposeCancelsTheImportAndNoFurtherFileIsSent()
	{
		var pane = await OpenPaneAsync();
		var first = MakeFile("d1.txt");
		var second = MakeFile("d2.txt");
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			await Task.Delay(Timeout.Infinite, ct);
			throw new InvalidOperationException();
		};
		var task = pane.HandleFilesAsync([first, second], 0, 0);
		await WaitForAsync(() => _client.Calls.Contains($"attachment.import:a:{first}"));

		pane.Dispose();
		await task;

		Assert.False(pane.IsImporting);
		Assert.DoesNotContain($"attachment.import:a:{second}", _client.Calls);
	}

	// ── C4: typing during the copy ───────────────────────────────────────────

	[Fact]
	public async Task C4_WhenTheTextChangedDuringTheCopy_TheLinesGoOnTheirOwnLineAtTheEnd()
	{
		var pane = await OpenPaneAsync();
		pane.InputText = "AB";
		var release = new TaskCompletionSource();
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			await release.Task;
			return Stored(@"C:\att\f.txt");
		};
		var task = pane.HandleFilesAsync([MakeFile("f.txt")], 1, 0);   // dropped between A and B
		await WaitForAsync(() => pane.IsImporting);
		pane.InputText = "AB typed more";
		release.SetResult();
		await task;

		Assert.Equal("AB typed more" + Environment.NewLine + @"Attached file: C:\att\f.txt", pane.InputText);
	}

	[Fact]
	public async Task C4_AChangedTextThatAlreadyEndsInANewLineGetsNoExtraBlankLine()
	{
		var pane = await OpenPaneAsync();
		pane.InputText = "x";
		var release = new TaskCompletionSource();
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			await release.Task;
			return Stored(@"C:\att\f.txt");
		};
		var task = pane.HandleFilesAsync([MakeFile("g.txt")], 0, 0);
		await WaitForAsync(() => pane.IsImporting);
		pane.InputText = "typed" + Environment.NewLine;
		release.SetResult();
		await task;

		Assert.Equal("typed" + Environment.NewLine + @"Attached file: C:\att\f.txt", pane.InputText);
	}

	// ── C5: the notice stays with its session ────────────────────────────────

	[Fact]
	public async Task C5_AFailureNoticeIsNotShownInAnotherSessionOpenedMeanwhile()
	{
		var pane = await OpenPaneAsync();
		var release = new TaskCompletionSource();
		_client.AttachmentImportHandler = async (node, path, ct) =>
		{
			await release.Task;   // not cancellable: the call ends with a failure after the switch
			throw new InvalidOperationException("refused after the switch");
		};
		var task = pane.HandleFilesAsync([MakeFile("n.txt")], 0, 0);
		await WaitForAsync(() => pane.IsImporting);

		await pane.OpenAsync("b");
		release.SetResult();
		await task;

		Assert.Null(pane.Notice);
	}

	// ── C6: the folder check runs off the UI thread ──────────────────────────

	[Fact]
	public async Task C6_TheFolderProbeRunsOnAnotherThread()
	{
		var callerThread = Environment.CurrentManagedThreadId;
		var probeThreads = new List<int>();
		var pane = await OpenPaneAsync(probe: p =>
		{
			lock (probeThreads)
				probeThreads.Add(Environment.CurrentManagedThreadId);
			return false;
		});

		await pane.HandleFilesAsync([MakeFile("p1.txt"), MakeFile("p2.txt")], 0, 0);

		Assert.Equal(2, probeThreads.Count);
		Assert.All(probeThreads, id => Assert.NotEqual(callerThread, id));
	}

	// ── C7: control, format and line-separator characters ────────────────────

	[Theory]
	[InlineData("report\u202Egnp.exe")]    // right-to-left override: shows "reportexe.png"
	[InlineData("a\u200Bb.txt")]           // zero-width space (Cf)
	[InlineData("x\u2066y.txt")]           // isolate (Cf)
	[InlineData("line\u2028break.txt")]    // line separator (Zl)
	[InlineData("para\u2029graph.txt")]    // paragraph separator (Zp)
	[InlineData("ctrl\u0085.txt")]         // C1 control (Cc)
	public async Task C7_APathWithHiddenCharactersIsRefusedInPathModeWithANoticeNamingTheFile(string name)
	{
		var pane = await OpenPaneAsync();
		pane.AttachAsFiles = false;

		await pane.HandleFilesAsync([@"C:\dir\" + name, @"C:\dir\fine.txt"], 0, 0);

		Assert.Equal(@"C:\dir\fine.txt", pane.InputText);
		Assert.Contains(Constants.Texts.InsertFailedHeading, pane.Notice);
		Assert.Contains(Constants.Texts.HiddenCharactersRefused, pane.Notice);
		Assert.Contains("\uFFFD", pane.Notice);   // the hidden character itself is shown as a placeholder, never raw
		Assert.DoesNotContain(name[name.IndexOfAny(['\u202E', '\u200B', '\u2066', '\u2028', '\u2029', '\u0085'])], pane.Notice!);
	}

	[Fact]
	public async Task C7_APathWithHiddenCharactersIsRefusedInFileModeWithoutCallingTheDaemon()
	{
		var pane = await OpenPaneAsync();
		var evil = MakeFile("evil\u202Egnp.exe");

		await pane.HandleFilesAsync([evil], 0, 0);

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("attachment.import", StringComparison.Ordinal));
		Assert.Equal(string.Empty, pane.InputText);
		Assert.Contains("evil", pane.Notice);
		Assert.Contains(Constants.Texts.HiddenCharactersRefused, pane.Notice);
	}

	[Fact]
	public async Task C7_ADaemonReturnedPathWithHiddenCharactersIsNotInserted()
	{
		var pane = await OpenPaneAsync();
		_client.AttachmentImportHandler = (node, path, ct) => Task.FromResult(Stored("C:\\att\\20260101-000000-x\u202Etxt.exe"));

		await pane.HandleFilesAsync([MakeFile("x.txt")], 0, 0);

		Assert.Equal(string.Empty, pane.InputText);
		Assert.Contains(Constants.Texts.StoredPathRefused, pane.Notice);
	}

	[Fact]
	public void C7_TheGuardAcceptsOrdinaryNamesIncludingNonLatinOnes()
	{
		var guard = new PromptPathGuard();

		Assert.True(guard.IsSafe(@"C:\Users\Анна\отчёт 2026 (final).md"));
		Assert.True(guard.IsSafe(@"C:\日本語\ファイル.txt"));
		Assert.False(guard.IsSafe("C:\\a\tb"));
		Assert.Equal("evil\uFFFDgnp.exe", guard.DisplayName("C:\\d\\evil\u202Egnp.exe"));
	}

	// ── 1,000 paths ──────────────────────────────────────────────────────────

	[Fact]
	public async Task ThousandPaths_InBothModes_CompleteAndTheFailureNoticeIsCapped()
	{
		var pane = await OpenPaneAsync();
		var paths = Enumerable.Range(0, 1000).Select(i => @"C:\bulk\f" + i + ".txt").ToList();

		pane.AttachAsFiles = false;
		await pane.HandleFilesAsync(paths, 0, 0);
		Assert.Equal(1000, pane.InputText.Split(Environment.NewLine).Length);

		pane.InputText = string.Empty;
		pane.AttachAsFiles = true;
		_client.AttachmentImportHandler = (node, path, ct) => throw new HttpRequestException("down");
		await pane.HandleFilesAsync(paths, 0, 0);

		Assert.Equal(string.Empty, pane.InputText);
		var lines = pane.Notice!.Split(Environment.NewLine);
		Assert.Equal(1 + Constants.Limits.MaxListedAttachFailures + 1, lines.Length);   // heading, the capped list, "and N more"
		Assert.Equal("and 995 more", lines[^1]);
	}

	// ── the --test-drop seam ─────────────────────────────────────────────────

	[Fact]
	public async Task Seam_TheViewModelHandsOutTheTestDropPathsOnlyOnce()
	{
		var pane = await OpenPaneAsync(testDrop: [@"C:\a.txt", @"C:\b.txt"]);

		Assert.Equal([@"C:\a.txt", @"C:\b.txt"], pane.TakeTestDropPaths());
		Assert.Empty(pane.TakeTestDropPaths());
	}

	[Fact]
	public async Task Seam_NoTestDropPaths_MeansAnEmptyListAndNothingHappens()
	{
		var pane = await OpenPaneAsync();

		Assert.Empty(pane.TakeTestDropPaths());
	}
}
