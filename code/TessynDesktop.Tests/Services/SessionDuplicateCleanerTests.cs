using System;
using System.IO;
using System.Linq;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionDuplicateCleanerTests : IDisposable
{
	private readonly string _dir;
	private readonly SessionFileService _files;

	public SessionDuplicateCleanerTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "cm_dedupe_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_dir);
		var settings = new AppSettingsService(_dir);
		settings.Settings.SessionFilesRoot = _dir;
		_files = new SessionFileService(settings);
	}

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	private const string Answer = "Done.\n\n- one\n- two";

	private static string Entry(string time, string role, string content, string meta = "") =>
		$"[2026-11-17T{time}Z] {role}{(meta.Length == 0 ? "" : " " + meta)}\r\n{content.Replace("\n", "\r\n")}\r\n\r\n";

	[Fact]
	public void WriteBackCopyOfAMultiLineAnswer_IsRemoved_OriginalKept()
	{
		var raw = Entry("09:00:02", "ASSISTANT", Answer, "model=\"claude-opus-5-5\" effort=\"default\"")
		          + Entry("09:00:03", "ASSISTANT", Answer)
		          + Entry("09:01:11", "USER", "next", "profile=\"p\"");

		var result = SessionDuplicateCleaner.Clean(raw);

		Assert.Single(result.Removed);
		Assert.Equal(Entry("09:00:02", "ASSISTANT", Answer, "model=\"claude-opus-5-5\" effort=\"default\"") + Entry("09:01:11", "USER", "next", "profile=\"p\""),
			result.CleanedText);
	}

	[Fact]
	public void FileProducedThroughTheRealAppendPath_IsCleanedToOneCopy()
	{
		var file = _files.CreateSessionFile();
		_files.AppendMessage(file, "USER", "Please:\n1. a\n2. b", profileName: "p");
		_files.AppendMessage(file, "USER", "Please:\n1. a\n2. b"); // what the faulty write-back appended for the prompt
		_files.AppendMessage(file, "ASSISTANT", Answer, modelId: "m", effort: "e");
		_files.AppendMessage(file, "ASSISTANT", Answer); // and for the answer

		var result = SessionDuplicateCleaner.Clean(File.ReadAllText(_files.GetFullPath(file)));

		Assert.Equal(1, result.Removed.Count(r => r.Role == "ASSISTANT"));
		Assert.Equal(1, result.Removed.Count(r => r.Role == "USER"));
		var roles = SessionFileService.ParseEntries(result.CleanedText.ReplaceLineEndings("\n").Split('\n')).Select(e => e.Role).ToList();
		Assert.Equal(["USER", "ASSISTANT"], roles);
	}

	[Fact]
	public void SingleLineRepeat_IsKept() // two notifications answered with the same sentence are two real turns
	{
		var raw = Entry("06:13:36", "ASSISTANT", "Another expired watch on the stopped full E2E run. Nothing to act on.")
		          + Entry("06:13:37", "ASSISTANT", "Another expired watch on the stopped full E2E run. Nothing to act on.");

		var result = SessionDuplicateCleaner.Clean(raw);

		Assert.Empty(result.Removed);
		Assert.Equal(raw, result.CleanedText);
	}

	[Fact]
	public void SameTextAfterALongGap_IsKept()
	{
		var raw = Entry("09:00:02", "ASSISTANT", Answer) + Entry("09:05:00", "ASSISTANT", Answer);

		Assert.Empty(SessionDuplicateCleaner.Clean(raw).Removed);
	}

	[Fact]
	public void CopyThatCarriesMetadata_IsKept() // a Core-written entry (model stamp, profile) is never a write-back copy
	{
		var raw = Entry("09:00:02", "ASSISTANT", Answer, "model=\"m\"") + Entry("09:00:03", "ASSISTANT", Answer, "model=\"m\"");

		Assert.Empty(SessionDuplicateCleaner.Clean(raw).Removed);
	}

	[Fact]
	public void DifferentRole_OrDifferentText_IsKept()
	{
		var raw = Entry("09:00:02", "SYSTEM", Answer) + Entry("09:00:03", "ASSISTANT", Answer) + Entry("09:00:04", "ASSISTANT", Answer + "\nmore");

		Assert.Empty(SessionDuplicateCleaner.Clean(raw).Removed);
	}

	[Fact]
	public void TripleCopy_IsReducedToTheOriginal()
	{
		var raw = Entry("09:00:02", "ASSISTANT", Answer, "model=\"m\"") + Entry("09:00:03", "ASSISTANT", Answer) + Entry("09:00:04", "ASSISTANT", Answer);

		var result = SessionDuplicateCleaner.Clean(raw);

		Assert.Equal(2, result.Removed.Count);
		Assert.Equal(Entry("09:00:02", "ASSISTANT", Answer, "model=\"m\""), result.CleanedText);
	}

	[Fact]
	public void CleanFile_IsReturnedUnchanged()
	{
		var raw = Entry("09:00:02", "USER", "hello", "profile=\"p\"") + Entry("09:00:03", "ASSISTANT", Answer);

		Assert.Equal(raw, SessionDuplicateCleaner.Clean(raw).CleanedText);
	}
}
