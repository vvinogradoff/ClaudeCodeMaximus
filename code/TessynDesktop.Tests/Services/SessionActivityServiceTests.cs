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
	private readonly Dictionary<string, (string Text, DateTimeOffset Time)> _drafts = new();

	public void Set(string fileName, string text, DateTimeOffset time) => _drafts[fileName] = (text, time);

	public string? LoadDraft(string sessionFileName) => _drafts.TryGetValue(sessionFileName, out var d) ? d.Text : null;
	public void SaveDraft(string sessionFileName, string text) => _drafts[sessionFileName] = (text, DateTimeOffset.UtcNow);
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
	public void AppendedFile_IsParsedAgain_AndReflectsTheNewEntries()
	{
		var node = NodeWith(Entry(T0, "USER", "q"));
		Assert.Equal(T0, _sut.GetActivity(node).LastPromptUtc);

		File.AppendAllText(_files.GetFullPath(node.FileName), Entry(T0.AddMinutes(5), "USER", "q2", " profile=\"P\""), new UTF8Encoding(false));
		var activity = _sut.GetActivity(node);

		Assert.Equal(T0.AddMinutes(5), activity.LastPromptUtc);
		Assert.Equal("P", activity.LastPromptProfileName);
		Assert.Equal(2, _recorder.CountOf(nameof(ISessionFileService.ReadTailEntries)));
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
	public void LargeFileWhoseTailHoldsNoUserEntry_IsParsedInFull()
	{
		var bigAnswer = new string('y', Constants.Remote.ActivityTailBytes + 4096);
		var node = NodeWith(
			Entry(T0, "USER", "the prompt", " profile=\"Deep\"") +
			Entry(T0.AddMinutes(1), "ASSISTANT", bigAnswer));

		var activity = _sut.GetActivity(node);

		Assert.Equal(T0, activity.LastPromptUtc);
		Assert.Equal("Deep", activity.LastPromptProfileName);
		Assert.Equal(T0.AddMinutes(1), activity.LastResponseUtc);
		Assert.Equal(1, _recorder.CountOf(nameof(ISessionFileService.ReadEntries)));
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
