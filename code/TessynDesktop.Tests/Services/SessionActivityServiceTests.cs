using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>In-memory <see cref="IDraftService"/>.</summary>
internal sealed class FakeDraftService : IDraftService
{
	private readonly Dictionary<string, (string Text, DateTimeOffset Time, long Version)> _drafts = new();
	private long _version;

	/// <summary>How many times <see cref="LoadDraft"/> read a draft's content.</summary>
	public int LoadCount { get; private set; }

	public void Set(string fileName, string text, DateTimeOffset time) => _drafts[fileName] = (text, time, ++_version);

	public SessionFileStampModel? GetDraftStamp(string sessionFileName) =>
		_drafts.TryGetValue(sessionFileName, out var d) ? new SessionFileStampModel(d.Version, d.Time) : null;

	public string? LoadDraft(string sessionFileName) { LoadCount++; return _drafts.TryGetValue(sessionFileName, out var d) ? d.Text : null; }
	public void SaveDraft(string sessionFileName, string text) => Set(sessionFileName, text, DateTimeOffset.UtcNow);
	public void DeleteDraft(string sessionFileName) => _drafts.Remove(sessionFileName);
	public DateTimeOffset? GetDraftTimestamp(string sessionFileName) => _drafts.TryGetValue(sessionFileName, out var d) ? d.Time : null;
}

/// <remarks>Created by Claude</remarks>
public sealed class SessionActivityServiceTests : IDisposable
{
	private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

	private readonly string _tempDir;
	private readonly SessionFileService _files;
	private readonly ISessionFileService _recordedFiles;
	private readonly RecordingProxy<ISessionFileService> _recorder;
	private readonly FakeDraftService _drafts = new();
	private readonly SessionActivityService _sut;

	public SessionActivityServiceTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_activity_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);

		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(settings);
		_recordedFiles = RecordingProxy<ISessionFileService>.Wrap(_files, out _recorder);
		_sut = new SessionActivityService(_recordedFiles, _drafts);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private static string Stamp(DateTimeOffset time) => time.ToString(Constants.SessionFile.TimestampFormat);

	private SessionNodeModel NodeWith(string body)
	{
		var fileName = _files.CreateSessionFile();
		File.WriteAllText(_files.GetFullPath(fileName), body, new UTF8Encoding(false));
		return new SessionNodeModel { Name = "S", FileName = fileName, NodeId = Guid.NewGuid().ToString("N") };
	}

	private static string Entry(DateTimeOffset time, string role, string content, string meta = "") =>
		$"[{Stamp(time)}] {role}{meta}\n{content}\n\n";

	[Fact]
	public void SessionWithoutFileOrDraft_IsAllEmpty()
	{
		var node = new SessionNodeModel { Name = "S", FileName = "missing.txt", NodeId = "n" };

		var activity = _sut.GetActivity(node);

		Assert.Equal(new SessionActivityModel(null, null, null, null, false), activity);
	}

	[Fact]
	public void ReadsLastPromptLastResponseAndProfileFromTheEntries()
	{
		var node = NodeWith(
			Entry(T0, "USER", "first", " profile=\"Old\"") +
			Entry(T0.AddMinutes(1), "ASSISTANT", "a1", " model=\"m\"") +
			Entry(T0.AddMinutes(2), "USER", "second", " profile=\"Work\"") +
			Entry(T0.AddMinutes(3), "ASSISTANT", "a2") +
			Entry(T0.AddMinutes(4), "SYSTEM", "note"));

		var activity = _sut.GetActivity(node);

		Assert.Equal(T0.AddMinutes(2), activity.LastPromptUtc);
		Assert.Equal(T0.AddMinutes(3), activity.LastResponseUtc);
		Assert.Equal(T0.AddMinutes(3), activity.LastActivityUtc);
		Assert.Equal("Work", activity.LastPromptProfileName);
		Assert.False(activity.HasDraft);
	}

	[Fact]
	public void PromptNewerThanResponse_IsTheLastActivity()
	{
		var node = NodeWith(
			Entry(T0, "USER", "q1") +
			Entry(T0.AddMinutes(1), "ASSISTANT", "a1") +
			Entry(T0.AddMinutes(2), "USER", "q2"));

		var activity = _sut.GetActivity(node);

		Assert.Equal(T0.AddMinutes(2), activity.LastActivityUtc);
		Assert.Equal(T0.AddMinutes(1), activity.LastResponseUtc);
	}

	[Fact]
	public void NonBlankDraft_SetsHasDraft_AndItsTimestampCountsTowardLastActivity()
	{
		var node = NodeWith(Entry(T0, "USER", "q") + Entry(T0.AddMinutes(1), "ASSISTANT", "a"));
		_drafts.Set(node.FileName, "typing...", T0.AddMinutes(10));

		var activity = _sut.GetActivity(node);

		Assert.True(activity.HasDraft);
		Assert.Equal(T0.AddMinutes(10), activity.LastActivityUtc);
		Assert.Equal(T0.AddMinutes(1), activity.LastResponseUtc);
	}

	[Fact]
	public void BlankDraft_IsIgnored()
	{
		var node = NodeWith(Entry(T0, "USER", "q"));
		_drafts.Set(node.FileName, "  \n ", T0.AddMinutes(10));

		var activity = _sut.GetActivity(node);

		Assert.False(activity.HasDraft);
		Assert.Equal(T0, activity.LastActivityUtc);
	}

	[Fact]
	public void DraftOlderThanTheLastEntry_DoesNotMoveLastActivityBack()
	{
		var node = NodeWith(Entry(T0.AddHours(1), "USER", "q"));
		_drafts.Set(node.FileName, "old draft", T0);

		var activity = _sut.GetActivity(node);

		Assert.True(activity.HasDraft);
		Assert.Equal(T0.AddHours(1), activity.LastActivityUtc);
	}

	[Fact]
	public void DraftOnASessionWithoutAnyFile_StillCounts()
	{
		var node = new SessionNodeModel { Name = "S", FileName = "missing.txt", NodeId = "n" };
		_drafts.Set("missing.txt", "hello", T0);

		var activity = _sut.GetActivity(node);

		Assert.True(activity.HasDraft);
		Assert.Equal(T0, activity.LastActivityUtc);
		Assert.Null(activity.LastPromptUtc);
	}

	// ── Caching ──────────────────────────────────────────────────────────────────

	[Fact]
	public void UnchangedFile_IsParsedOnce()
	{
		var node = NodeWith(Entry(T0, "USER", "q") + Entry(T0.AddMinutes(1), "ASSISTANT", "a"));

		for (var i = 0; i < 5; i++)
			_sut.GetActivity(node);

		Assert.Equal(1, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
	}

	[Fact]
	public void AppendedFile_ParsesOnlyTheAppendedBytes_AndReflectsTheNewEntries()
	{
		var node = NodeWith(Entry(T0, "USER", "q"));
		Assert.Equal(T0, _sut.GetActivity(node).LastPromptUtc);

		File.AppendAllText(_files.GetFullPath(node.FileName), Entry(T0.AddMinutes(5), "USER", "q2", " profile=\"P\""), new UTF8Encoding(false));
		var activity = _sut.GetActivity(node);

		Assert.Equal(T0.AddMinutes(5), activity.LastPromptUtc);
		Assert.Equal("P", activity.LastPromptProfileName);
		Assert.Equal(1, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
		Assert.Equal(1, _recorder.CountOf(nameof(ISessionFileService.ReadEntriesFrom)));
	}

	[Fact]
	public void GrowingFile_ParsesOnlyTheAppendedBytes_AndMergesThemIntoTheCachedValues()
	{
		var node = NodeWith(Entry(T0, "USER", "q1", " profile=\"P1\"") + Entry(T0.AddMinutes(1), "ASSISTANT", "a1"));
		_sut.GetActivity(node);
		var path = _files.GetFullPath(node.FileName);

		// An append without any USER entry keeps the cached prompt and profile and moves only the response.
		File.AppendAllText(path, Entry(T0.AddMinutes(2), "ASSISTANT", new string('x', 5000)), new UTF8Encoding(false));
		var afterAssistant = _sut.GetActivity(node);
		Assert.Equal(T0, afterAssistant.LastPromptUtc);
		Assert.Equal("P1", afterAssistant.LastPromptProfileName);
		Assert.Equal(T0.AddMinutes(2), afterAssistant.LastResponseUtc);

		// An appended USER entry replaces the prompt; the response stays the cached one.
		File.AppendAllText(path, Entry(T0.AddMinutes(3), "USER", "q2", " profile=\"P2\""), new UTF8Encoding(false));
		var afterUser = _sut.GetActivity(node);
		Assert.Equal(T0.AddMinutes(3), afterUser.LastPromptUtc);
		Assert.Equal("P2", afterUser.LastPromptProfileName);
		Assert.Equal(T0.AddMinutes(2), afterUser.LastResponseUtc);

		Assert.Equal(1, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
		Assert.Equal(2, _recorder.CountOf(nameof(ISessionFileService.ReadEntriesFrom)));
		Assert.Equal(0, _recorder.CountOf(nameof(ISessionFileService.ReadEntries)));
	}

	[Fact]
	public void LongAgenticTurnOfLargeAppends_NeverTriggersAWiderParse()
	{
		var node = NodeWith(Entry(T0, "USER", "prompt") + Entry(T0.AddSeconds(1), "ASSISTANT", "start"));
		_sut.GetActivity(node);
		var path = _files.GetFullPath(node.FileName);

		for (var i = 0; i < 6; i++)
		{
			File.AppendAllText(path, Entry(T0.AddSeconds(2 + i), "ASSISTANT", new string('t', 40_000)), new UTF8Encoding(false));
			var activity = _sut.GetActivity(node);
			Assert.Equal(T0, activity.LastPromptUtc);
			Assert.Equal(T0.AddSeconds(2 + i), activity.LastResponseUtc);
		}

		Assert.Equal(1, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
		Assert.Equal(0, _recorder.CountOf(nameof(ISessionFileService.ReadEntries)));
	}

	[Fact]
	public void RewrittenShorterFile_ResetsTheEntry()
	{
		var node = NodeWith(Entry(T0, "USER", "a long first prompt" + new string('x', 500)) + Entry(T0.AddMinutes(1), "ASSISTANT", "a1"));
		Assert.Equal(T0, _sut.GetActivity(node).LastPromptUtc);

		_files.RewriteSessionFile(node.FileName, Entry(T0.AddHours(1), "USER", "q"));
		var activity = _sut.GetActivity(node);

		Assert.Equal(T0.AddHours(1), activity.LastPromptUtc);
		Assert.Null(activity.LastResponseUtc);
		Assert.Equal(2, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
		Assert.Equal(0, _recorder.CountOf(nameof(ISessionFileService.ReadEntriesFrom)));
	}

	[Fact]
	public void UnchangedDraft_IsReadOnce_AndAChangedDraftIsReadAgain()
	{
		var node = NodeWith(Entry(T0, "USER", "q"));
		_drafts.Set(node.FileName, "typing", T0.AddMinutes(3));

		for (var i = 0; i < 5; i++)
			Assert.True(_sut.GetActivity(node).HasDraft);
		Assert.Equal(1, _drafts.LoadCount);

		_drafts.Set(node.FileName, "typing more", T0.AddMinutes(4));
		Assert.Equal(T0.AddMinutes(4), _sut.GetActivity(node).LastActivityUtc);
		Assert.Equal(2, _drafts.LoadCount);

		_drafts.DeleteDraft(node.FileName);
		Assert.False(_sut.GetActivity(node).HasDraft);
	}

	[Fact]
	public void DraftChange_DoesNotInvalidateTheFileCache_ButIsAlwaysCurrent()
	{
		var node = NodeWith(Entry(T0, "USER", "q"));
		Assert.False(_sut.GetActivity(node).HasDraft);

		_drafts.Set(node.FileName, "typing", T0.AddMinutes(3));
		var activity = _sut.GetActivity(node);

		Assert.True(activity.HasDraft);
		Assert.Equal(1, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
	}

	[Fact]
	public void FileThatDisappears_FallsBackToEmpty()
	{
		var node = NodeWith(Entry(T0, "USER", "q"));
		Assert.NotNull(_sut.GetActivity(node).LastPromptUtc);

		File.Delete(_files.GetFullPath(node.FileName));

		Assert.Null(_sut.GetActivity(node).LastPromptUtc);
	}

	// ── Tail versus full parse ───────────────────────────────────────────────────

	[Fact]
	public void LargeFileWhoseTailHoldsAUserEntry_IsNotParsedInFull()
	{
		var filler = new string('x', 200);
		var sb = new StringBuilder();
		for (var i = 0; i < 600; i++)
			sb.Append(Entry(T0.AddSeconds(i * 2), "USER", filler)).Append(Entry(T0.AddSeconds(i * 2 + 1), "ASSISTANT", filler));
		var node = NodeWith(sb.ToString());
		Assert.True(new FileInfo(_files.GetFullPath(node.FileName)).Length > Constants.Remote.ActivityTailBytes);

		var activity = _sut.GetActivity(node);

		Assert.Equal(T0.AddSeconds(1198), activity.LastPromptUtc);
		Assert.Equal(0, _recorder.CountOf(nameof(ISessionFileService.ReadEntries)));
	}

	[Fact]
	public void ColdFileWhoseTailHoldsNoUserEntry_WidensOnceToTheColdCap_AndNeverParsesInFull()
	{
		var bigAnswer = new string('y', Constants.Remote.ActivityTailBytes + 4096);
		var node = NodeWith(
			Entry(T0, "USER", "the prompt", " profile=\"Deep\"") +
			Entry(T0.AddMinutes(1), "ASSISTANT", bigAnswer));

		var activity = _sut.GetActivity(node);

		Assert.Equal(T0, activity.LastPromptUtc);
		Assert.Equal("Deep", activity.LastPromptProfileName);
		Assert.Equal(T0.AddMinutes(1), activity.LastResponseUtc);
		Assert.Equal(2, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
		Assert.Equal(0, _recorder.CountOf(nameof(ISessionFileService.ReadEntries)));
	}

	[Fact]
	public void ColdFileWithThePromptBeyondTheColdCap_ReportsTheLastPromptAsUnknown()
	{
		var huge = new string('z', Constants.Remote.ActivityColdParseMaxBytes + 1024);
		var node = NodeWith(
			Entry(T0, "USER", "far away") +
			Entry(T0.AddMinutes(1), "ASSISTANT", huge));

		var activity = _sut.GetActivity(node);

		Assert.Null(activity.LastPromptUtc);
		Assert.Null(activity.LastPromptProfileName);
		Assert.Equal(0, _recorder.CountOf(nameof(ISessionFileService.ReadEntries)));
	}

	[Fact]
	public void SmallFileWithoutAnyUserEntry_IsNotParsedInFull()
	{
		var node = NodeWith(Entry(T0, "SYSTEM", "just a note"));

		var activity = _sut.GetActivity(node);

		Assert.Null(activity.LastPromptUtc);
		Assert.Equal(0, _recorder.CountOf(nameof(ISessionFileService.ReadEntries)));
	}

	// ── Volume and concurrency ───────────────────────────────────────────────────

	[Fact]
	public void SixHundredSessions_AreParsedOnceEach_AndAnswerFromCacheAfterwards()
	{
		var nodes = new List<SessionNodeModel>();
		for (var i = 0; i < 600; i++)
			nodes.Add(NodeWith(Entry(T0.AddSeconds(i), "USER", "q") + Entry(T0.AddSeconds(i + 1), "ASSISTANT", "a")));

		foreach (var node in nodes)
			_sut.GetActivity(node);
		var tailReads = _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries));
		foreach (var node in nodes)
			_sut.GetActivity(node);

		Assert.Equal(600, tailReads);
		Assert.Equal(600, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
	}

	[Fact]
	public void ConcurrentCallers_GetConsistentResults()
	{
		var node = NodeWith(Entry(T0, "USER", "q") + Entry(T0.AddMinutes(1), "ASSISTANT", "a"));

		var results = new SessionActivityModel[64];
		Parallel.For(0, results.Length, i => results[i] = _sut.GetActivity(node));

		Assert.All(results, r => Assert.Equal(T0.AddMinutes(1), r.LastActivityUtc));
	}
}
