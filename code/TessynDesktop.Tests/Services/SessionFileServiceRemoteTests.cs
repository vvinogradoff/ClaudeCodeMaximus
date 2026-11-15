using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Header parsing rule, tail reads, file stamps and the rewrite event of <see cref="SessionFileService"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionFileServiceRemoteTests : IDisposable
{
	private const string Nl = "\n";

	private readonly string _tempDir;
	private readonly SessionFileService _sut;

	public SessionFileServiceRemoteTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_sessions_remote_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);

		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_sut = new SessionFileService(settings);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private string WriteRaw(string text)
	{
		var fileName = _sut.CreateSessionFile();
		File.WriteAllText(_sut.GetFullPath(fileName), text, new UTF8Encoding(false));
		return fileName;
	}

	private static string Header(int minute, string role, string meta = "") =>
		$"[2026-11-12T10:{minute:00}:00Z] {role}{meta}";

	// ── Header rule: bodies that merely start with '[' keep their content ──────────

	[Theory]
	[InlineData("[Tool: Read] src/Program.cs")]
	[InlineData("[Scheduled] Daily check")]
	[InlineData("[Interrupted by user]")]
	[InlineData("[a markdown link](https://example.com/page)")]
	[InlineData("[ ] an unchecked task")]
	[InlineData("[x] a checked task")]
	[InlineData("[2026-13-45T99:99:99Z] USER looks like a header but is not a date")]
	[InlineData("[2026-11-12T10:05:00Z]")]
	[InlineData("[2026-11-12T10:05:00Z] ")]
	[InlineData("[2026-11-12T10:05:00Z]USER no space after the bracket")]
	public void BodyLineStartingWithBracket_IsKeptAsContent(string bodyLine)
	{
		var fileName = WriteRaw(
			Header(0, "ASSISTANT") + Nl +
			"first line" + Nl +
			bodyLine + Nl +
			"last line" + Nl + Nl);

		var entries = _sut.ReadEntries(fileName);

		var entry = Assert.Single(entries);
		Assert.Equal("ASSISTANT", entry.Role);
		Assert.Equal(string.Join(Environment.NewLine, "first line", bodyLine, "last line"), entry.Content);
	}

	[Fact]
	public void ToolAndScheduledAndInterruptedLines_AcrossEntries_AreNotLostOrSplit()
	{
		var fileName = WriteRaw(
			Header(0, "USER", " profile=\"Work\"") + Nl + "run it" + Nl + Nl +
			Header(1, "ASSISTANT", " model=\"opus\" effort=\"high\"") + Nl +
			"[Tool: Read] a.cs" + Nl + "[Tool: Bash] dotnet build" + Nl + "done" + Nl + Nl +
			Header(2, "SYSTEM") + Nl + "[Interrupted by user]" + Nl + Nl +
			Header(3, "USER") + Nl + "[Scheduled] nightly" + Nl + Nl);

		var entries = _sut.ReadEntries(fileName);

		Assert.Equal(4, entries.Count);
		Assert.Equal("Work", entries[0].ProfileName);
		Assert.Equal("opus", entries[1].ModelId);
		Assert.Equal("[Tool: Read] a.cs" + Environment.NewLine + "[Tool: Bash] dotnet build" + Environment.NewLine + "done", entries[1].Content);
		Assert.Equal("[Interrupted by user]", entries[2].Content);
		Assert.Equal("[Scheduled] nightly", entries[3].Content);
	}

	[Fact]
	public void RealHeaderInsideBody_StillStartsANewEntry()
	{
		var fileName = WriteRaw(
			Header(0, "USER") + Nl + "question" + Nl + Nl +
			Header(1, "ASSISTANT", " model=\"m\"") + Nl + "answer" + Nl + Nl +
			Header(2, "COMPACTION") + Nl);

		var entries = _sut.ReadEntries(fileName);

		Assert.Equal(new[] { "USER", "ASSISTANT", "COMPACTION" }, entries.Select(e => e.Role));
		Assert.Equal("answer", entries[1].Content);
		Assert.Equal("m", entries[1].ModelId);
	}

	[Fact]
	public void EntryBodyEndingJustBeforeARealHeaderWithoutBlankLine_StillSplits()
	{
		var fileName = WriteRaw(
			Header(0, "ASSISTANT") + Nl + "[Tool: Read] x" + Nl +
			Header(1, "USER") + Nl + "next" + Nl);

		var entries = _sut.ReadEntries(fileName);

		Assert.Equal(2, entries.Count);
		Assert.Equal("[Tool: Read] x", entries[0].Content);
		Assert.Equal("next", entries[1].Content);
	}

	[Fact]
	public void HeaderOnlyLineWithoutRole_DoesNotThrow()
	{
		var fileName = WriteRaw("[2026-11-12T10:00:00Z]" + Nl + Header(1, "USER") + Nl + "x" + Nl);

		var entries = _sut.ReadEntries(fileName);

		Assert.Equal("x", Assert.Single(entries).Content);
	}

	// ── Tail reads ───────────────────────────────────────────────────────────────

	private string WriteManyEntries(int pairs, int bodyChars = 200)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < pairs; i++)
		{
			var minute = i % 60;
			sb.Append(Header(minute, "USER")).Append(Nl).Append($"prompt {i} ").Append(new string('p', bodyChars)).Append(Nl).Append(Nl);
			sb.Append(Header(minute, "ASSISTANT")).Append(Nl).Append($"answer {i} ").Append(new string('a', bodyChars)).Append(Nl).Append(Nl);
		}
		return WriteRaw(sb.ToString());
	}

	[Fact]
	public void ReadTailEntries_SmallFile_ReturnsEverything()
	{
		var fileName = WriteManyEntries(3);

		var tail = _sut.ReadTailEntries(fileName, 1024 * 1024);

		Assert.Equal(_sut.ReadEntries(fileName).Count, tail.Count);
		Assert.Equal(6, tail.Count);
	}

	[Fact]
	public void ReadTailEntries_LargeFile_ReturnsOnlyEntriesWholeInsideTheWindow_AndTheLastOnesMatchTheFullParse()
	{
		var fileName = WriteManyEntries(400);
		var full = _sut.ReadEntries(fileName);
		var window = 8 * 1024;

		var tail = _sut.ReadTailEntries(fileName, window);

		Assert.NotEmpty(tail);
		Assert.True(tail.Count < full.Count);
		Assert.Equal(full[^1].Content, tail[^1].Content);
		for (var i = 1; i <= tail.Count; i++)
		{
			Assert.Equal(full[^i].Role, tail[^i].Role);
			Assert.Equal(full[^i].Content, tail[^i].Content);
		}
	}

	[Fact]
	public void ReadTailEntries_WindowStartingInsideAnEntry_DoesNotInventAnEntryFromThePartialText()
	{
		var fileName = WriteManyEntries(100);
		var full = _sut.ReadEntries(fileName);

		for (var window = 500; window < 1500; window += 37)
		{
			var tail = _sut.ReadTailEntries(fileName, window);
			Assert.All(tail, e => Assert.Contains(full, f => f.Content == e.Content && f.Role == e.Role));
			Assert.Equal(full[^1].Content, tail[^1].Content);
		}
	}

	[Fact]
	public void ReadEntriesFrom_ReturnsExactlyTheEntriesAppendedAfterTheOffset()
	{
		var fileName = WriteManyEntries(5);
		var path = _sut.GetFullPath(fileName);
		var before = _sut.ReadEntries(fileName);
		var offset = new FileInfo(path).Length;

		File.AppendAllText(path, Header(30, "USER") + Nl + "appended é prompt" + Nl + Nl + Header(31, "ASSISTANT") + Nl + "appended answer" + Nl + Nl);
		var appended = _sut.ReadEntriesFrom(fileName, offset);

		Assert.Equal(2, appended.Count);
		Assert.Equal("USER", appended[0].Role);
		Assert.Equal("appended é prompt", appended[0].Content.Trim());
		Assert.Equal("appended answer", appended[1].Content.Trim());
		Assert.Equal(before.Count + 2, _sut.ReadEntries(fileName).Count);
	}

	[Fact]
	public void ReadEntriesFrom_OffsetAtOrBeyondTheLength_OrMissingFile_ReturnsEmpty()
	{
		var fileName = WriteManyEntries(3);
		var length = new FileInfo(_sut.GetFullPath(fileName)).Length;

		Assert.Empty(_sut.ReadEntriesFrom(fileName, length));
		Assert.Empty(_sut.ReadEntriesFrom(fileName, length + 100));
		Assert.Empty(_sut.ReadEntriesFrom("missing.txt", 0));
	}

	[Fact]
	public void ReadEntriesFrom_ZeroOffset_EqualsTheFullParse()
	{
		var fileName = WriteManyEntries(10);

		Assert.Equal(_sut.ReadEntries(fileName).Count, _sut.ReadEntriesFrom(fileName, 0).Count);
	}

	[Fact]
	public void ReadTailEntries_MissingFile_ReturnsEmpty()
	{
		Assert.Empty(_sut.ReadTailEntries("missing.txt", 100));
	}

	[Fact]
	public void ReadTailEntries_MultiByteTextAtTheCut_DoesNotThrow()
	{
		var sb = new StringBuilder();
		for (var i = 0; i < 200; i++)
			sb.Append(Header(i % 60, "ASSISTANT")).Append(Nl).Append(new string('é', 150)).Append(Nl).Append(Nl);
		var fileName = WriteRaw(sb.ToString());

		for (var window = 700; window < 760; window++)
			Assert.NotEmpty(_sut.ReadTailEntries(fileName, window));
	}

	// ── File stamp ───────────────────────────────────────────────────────────────

	[Fact]
	public void GetFileStamp_ReportsLengthAndLastWriteTime_AndChangesWhenAppended()
	{
		var fileName = _sut.CreateSessionFile();
		var empty = _sut.GetFileStamp(fileName);
		Assert.NotNull(empty);
		Assert.Equal(new FileInfo(_sut.GetFullPath(fileName)).Length, empty!.Length);

		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "hello");
		var after = _sut.GetFileStamp(fileName);

		Assert.Equal(new FileInfo(_sut.GetFullPath(fileName)).Length, after!.Length);
		Assert.NotEqual(empty, after);
	}

	[Fact]
	public void GetFileStamp_MissingFile_IsNull()
	{
		Assert.Null(_sut.GetFileStamp("missing.txt"));
	}

	// ── SessionFileRewritten ─────────────────────────────────────────────────────

	private List<string> CollectRewrites()
	{
		var names = new List<string>();
		_sut.SessionFileRewritten += (_, e) => names.Add(e.FileName);
		return names;
	}

	[Fact]
	public void RewriteSessionFile_RaisesSessionFileRewritten()
	{
		var fileName = _sut.CreateSessionFile();
		var names = CollectRewrites();

		_sut.RewriteSessionFile(fileName, Header(0, "USER") + Nl + "x" + Nl);

		Assert.Equal(new[] { fileName }, names);
	}

	[Fact]
	public void WriteSessionFile_RaisesSessionFileRewritten()
	{
		var fileName = _sut.CreateSessionFile();
		var names = CollectRewrites();

		_sut.WriteSessionFile(fileName, new List<SessionEntryModel>
		{
			new() { Timestamp = DateTimeOffset.UtcNow, Role = Constants.SessionFile.RoleUser, Content = "x" },
		});

		Assert.Equal(new[] { fileName }, names);
	}

	[Fact]
	public void RepairCorruptedCompactions_RaisesSessionFileRewritten_ForEachRepairedFile()
	{
		var corrupted = WriteRaw("raw text without any header");
		var healthy = WriteRaw(Header(0, "USER") + Nl + "fine" + Nl);
		var names = CollectRewrites();

		var repaired = _sut.RepairCorruptedCompactions();

		Assert.Equal(1, repaired);
		Assert.Equal(new[] { corrupted }, names);
		Assert.DoesNotContain(healthy, names);
	}

	[Fact]
	public void RepairCorruptedCompactions_LeavesCrlfAndWhitespaceOnlyFilesAlone()
	{
		var crlf = WriteRaw(Header(0, "USER") + "\r\n" + "fine" + "\r\n" + Header(1, "ASSISTANT") + "\r\n" + "ok" + "\r\n");
		var blank = WriteRaw("  \r\n \n");
		var names = CollectRewrites();

		var repaired = _sut.RepairCorruptedCompactions();

		Assert.Equal(0, repaired);
		Assert.DoesNotContain(crlf, names);
		Assert.DoesNotContain(blank, names);
	}

	[Fact]
	public void AppendMessage_DoesNotRaiseSessionFileRewritten()
	{
		var fileName = _sut.CreateSessionFile();
		var names = CollectRewrites();

		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "hello");
		_sut.AppendCompactionSeparator(fileName);

		Assert.Empty(names);
	}
}
