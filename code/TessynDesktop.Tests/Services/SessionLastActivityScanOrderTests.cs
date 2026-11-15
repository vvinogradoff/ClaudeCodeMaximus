using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Newest-first chunked startup scan and the repair sequencing (ISSUE.016, startup tree metadata).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionLastActivityScanOrderTests : IDisposable
{
	private static readonly DateTimeOffset Base = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

	private readonly string _tempDir;
	private readonly SessionFileService _files;
	private readonly DraftService _drafts;
	private readonly SessionLastActivityScanner _scanner;

	public SessionLastActivityScanOrderTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_scanorder_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(settings);
		_drafts = new DraftService(Path.Combine(_tempDir, "drafts"));
		_scanner = new SessionLastActivityScanner(_files, _drafts);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	// --- Ordering and chunking ---

	[Fact]
	public void ScanNewestFirst_OrdersByLastWriteTime_MissingFilesLast_EveryFileOnce()
	{
		var old = WriteSession("old.txt", UserEntry(0, "A"), AssistantEntry(1, "x"));
		var mid = WriteSession("mid.txt", UserEntry(2, "B"), AssistantEntry(3, "x"));
		var recent = WriteSession("recent.txt", UserEntry(4, "C"), AssistantEntry(5, "x"));
		File.SetLastWriteTimeUtc(Path.Combine(_tempDir, old), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		File.SetLastWriteTimeUtc(Path.Combine(_tempDir, mid), new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
		File.SetLastWriteTimeUtc(Path.Combine(_tempDir, recent), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

		var seen = CollectChunks(["missing.txt", old, recent, mid], maxFiles: 100, interval: TimeSpan.FromHours(1));

		var names = seen.SelectMany(c => c).Select(r => r.FileName).ToList();
		Assert.Equal([recent, mid, old, "missing.txt"], names);
		Assert.Equal("C", seen.SelectMany(c => c).First().ProfileName);
		Assert.Null(seen.SelectMany(c => c).Last().LastPromptTimestamp);
	}

	[Fact]
	public void ScanNewestFirst_ChunksBySize_AndFlushesTheRemainder()
	{
		var names = Enumerable.Range(0, 5).Select(i => WriteSession($"s{i}.txt", UserEntry(i, "P"))).ToList();

		var chunks = CollectChunks(names, maxFiles: 2, interval: TimeSpan.FromHours(1));

		Assert.Equal([2, 2, 1], chunks.Select(c => c.Count));
		Assert.Equal(names.OrderBy(n => n), chunks.SelectMany(c => c).Select(r => r.FileName).OrderBy(n => n));
	}

	[Fact]
	public void ScanNewestFirst_ZeroInterval_HandsOverEveryFileAsItsOwnChunk()
	{
		var names = Enumerable.Range(0, 4).Select(i => WriteSession($"z{i}.txt", UserEntry(i, "P"))).ToList();

		var chunks = CollectChunks(names, maxFiles: 1000, interval: TimeSpan.Zero);

		Assert.Equal(4, chunks.Count);
		Assert.All(chunks, c => Assert.Single(c));
	}

	[Fact]
	public void ScanNewestFirst_Cancellation_StopsBeforeTheNextFile()
	{
		var names = Enumerable.Range(0, 6).Select(i => WriteSession($"c{i}.txt", UserEntry(i, "P"))).ToList();
		using var cts = new CancellationTokenSource();
		var delivered = 0;

		_scanner.ScanNewestFirst(names, 1, TimeSpan.FromHours(1), chunk =>
		{
			delivered += chunk.Count;
			if (delivered == 2)
				cts.Cancel();
		}, cts.Token);

		Assert.Equal(2, delivered);
	}

	[Fact]
	public void ScanNewestFirst_ResultsEqualScan_ForTheSameFiles()
	{
		var names = new List<string>
		{
			WriteSession("a.txt", UserEntry(0, "One"), AssistantEntry(1, "r"), UserEntry(2, "Two")),
			WriteSession("b.txt", UserEntry(3, "Three"), AssistantEntry(4, "r")),
			WriteSession("empty.txt"),
		};

		var chunked = CollectChunks(names, 2, TimeSpan.FromHours(1)).SelectMany(c => c).OrderBy(r => r.FileName).ToList();
		var plain = _scanner.Scan(names).OrderBy(r => r.FileName).ToList();

		Assert.Equal(plain, chunked);
	}

	// --- Chunked apply keeps the merge semantics ---

	[Fact]
	public void ApplyingChunksInOrder_NeverOverwritesValuesGainedDuringTheScan()
	{
		// The node gained a newer prompt/profile while the scan ran; later chunks carrying older values must not undo it.
		var merger = new SessionLastActivityMerger();
		var live = new SessionLastActivityModel("f", Base.AddHours(5), Base.AddHours(5), "Live", null);
		var scanned = new SessionLastActivityModel("f", Base.AddHours(1), Base.AddHours(2), "FromDisk", Base.AddHours(3));

		var merged = merger.Merge(live, scanned);

		Assert.Equal(Base.AddHours(5), merged.LastPromptTimestamp);
		Assert.Equal("Live", merged.ProfileName);
		Assert.Equal(Base.AddHours(5), merged.LastResponseTimestamp);
		Assert.Equal(Base.AddHours(3), merged.LastDraftEditTimestamp);
	}

	// --- Repair sequencing ---

	[Fact]
	public async Task RunAfter_DoesNotStartBeforeThePrerequisiteFinishes()
	{
		var gate = new TaskCompletionSource();
		var started = new TaskCompletionSource();

		var run = StartupWork.RunAfter(gate.Task, () => started.SetResult());
		await Task.Delay(100);
		Assert.False(started.Task.IsCompleted);

		gate.SetResult();
		await run.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(started.Task.IsCompleted);
	}

	[Fact]
	public async Task RunAfter_StillRuns_WhenThePrerequisiteFaulted()
	{
		var ran = false;
		var faulted = Task.FromException(new InvalidOperationException("scan failed"));

		await StartupWork.RunAfter(faulted, () => ran = true).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.True(ran);
	}

	[Fact]
	public void ScanNewestFirst_ManyFiles_ReadInParallelBatches_StayInNewestFirstOrder()
	{
		var names = new List<string>();
		for (var i = 0; i < 70; i++)
		{
			var name = WriteSession($"p{i:D3}.txt", UserEntry(i, "P" + i), AssistantEntry(i + 1, "r"));
			File.SetLastWriteTimeUtc(Path.Combine(_tempDir, name), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
			names.Add(name);
		}

		var chunks = CollectChunks(names.OrderBy(_ => Guid.NewGuid()).ToList(), maxFiles: 7, interval: TimeSpan.FromHours(1));

		var flat = chunks.SelectMany(c => c).ToList();
		Assert.Equal(names.AsEnumerable().Reverse(), flat.Select(r => r.FileName));
		Assert.Equal("P69", flat[0].ProfileName);
		Assert.Equal("P0", flat[^1].ProfileName);
	}

	// --- helpers ---

	private List<List<SessionLastActivityModel>> CollectChunks(IReadOnlyList<string> names, int maxFiles, TimeSpan interval)
	{
		var chunks = new List<List<SessionLastActivityModel>>();
		_scanner.ScanNewestFirst(names, maxFiles, interval, chunk => chunks.Add(chunk.ToList()));
		return chunks;
	}

	private string WriteSession(string fileName, params string[] entries) => WriteRaw(fileName, string.Concat(entries), bom: false);

	private string WriteRaw(string fileName, string content, bool bom)
	{
		File.WriteAllText(Path.Combine(_tempDir, fileName), content, new UTF8Encoding(bom));
		return fileName;
	}

	private static string Stamp(int minutes) => Base.AddMinutes(minutes).ToString(Constants.SessionFile.TimestampFormat);

	private static string UserEntry(int minutes, string profile, string body = "prompt")
		=> $"[{Stamp(minutes)}] USER profile=\"{profile}\"\n{body}\n\n";

	private static string AssistantEntry(int minutes, string body)
		=> $"[{Stamp(minutes)}] ASSISTANT model=\"m\"\n{body}\n\n";

	private static string Compaction(int minutes) => $"[{Stamp(minutes)}] COMPACTION\n";
}
