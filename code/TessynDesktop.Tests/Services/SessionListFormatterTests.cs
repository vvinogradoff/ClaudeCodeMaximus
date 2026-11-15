using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Text rendering of <c>list_sessions</c> pages (TC.15.4, FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListFormatterTests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

	private readonly SessionListFormatter _formatter = new();

	private static SessionListSectionModel DirSection(string label = "Tessyn", string path = @"C:\\Projects\Tessyn", int top = 5, int workers = 2) =>
		new()
		{
			Kind           = SessionListSectionKind.Directory,
			DirectoryLabel = label,
			DirectoryPath  = path,
			TopLevelCount  = top,
			WorkerCount    = workers,
		};

	private static SessionListRowModel Row(string id = "aaaaaaaa", string name = "Session", DateTimeOffset? last = null, int section = 0, int indent = 0) =>
		new()
		{
			NodeId          = id + "-full",
			ShortId         = id,
			Name            = name,
			LastActivityUtc = last ?? Now.AddHours(-1),
			IsResumable     = true,
			IndentLevel     = indent,
			SectionIndex    = section,
		};

	private static SessionListPageModel Page(
		IReadOnlyList<SessionListRowModel> rows,
		IReadOnlyList<SessionListSectionModel>? sections = null,
		SessionListCallerModel? caller = null,
		SessionListScope scope = SessionListScope.Project,
		int? total = null,
		int offset = 0,
		string? workersHint = null) =>
		new()
		{
			Scope             = scope,
			Caller            = caller,
			Sections          = sections ?? new[] { DirSection() },
			Rows              = rows,
			TotalRows         = total ?? rows.Count,
			Offset            = offset,
			Limit             = Constants.Agent.ListSessionsDefaultLimit,
			WorkersHintShortId = workersHint,
		};

	private string FormatLines(SessionListPageModel page, out string[] lines)
	{
		var text = _formatter.Format(page, Now);
		lines = text.Split('\n');
		return text;
	}

	[Fact]
	public void YouLine_TopLevelForm()
	{
		var caller = new SessionListCallerModel { ShortId = "abcd1234", Name = "Boss", DirectoryLabel = "Tessyn", OrchestrationDepth = 0 };

		FormatLines(Page(new[] { Row() }, caller: caller), out var lines);

		Assert.Equal("You: abcd1234 \"Boss\" - top-level session in Tessyn", lines[0]);
	}

	[Fact]
	public void YouLine_WorkerForm()
	{
		var caller = new SessionListCallerModel
		{
			ShortId             = "abcd1234",
			Name                = "Helper",
			DirectoryLabel      = "Tessyn",
			OrchestrationDepth  = 2,
			SupervisorShortId   = "feedbeef",
			SupervisorName      = "Boss",
		};

		FormatLines(Page(new[] { Row() }, caller: caller), out var lines);

		Assert.Equal("You: abcd1234 \"Helper\" - worker (depth 2) of feedbeef \"Boss\" in Tessyn", lines[0]);
	}

	[Fact]
	public void YouLine_SupervisorGone_OfPartDropped()
	{
		var caller = new SessionListCallerModel { ShortId = "abcd1234", Name = "Helper", DirectoryLabel = "Tessyn", OrchestrationDepth = 1 };

		FormatLines(Page(new[] { Row() }, caller: caller), out var lines);

		Assert.Equal("You: abcd1234 \"Helper\" - worker (depth 1) in Tessyn", lines[0]);
	}

	[Fact]
	public void SectionHeader_DirectoryForm()
	{
		var section = DirSection("Tessyn", @"C:\\Projects\Tessyn\", top: 7, workers: 3);

		FormatLines(Page(new[] { Row() }, new[] { section }), out var lines);

		Assert.Equal(@"Tessyn (C:\\Projects\Tessyn) - 7 top-level sessions, 3 workers", lines[0]);
	}

	[Fact]
	public void SectionHeader_WorkersForm()
	{
		var section = new SessionListSectionModel
		{
			Kind           = SessionListSectionKind.Workers,
			DirectoryLabel = "Tessyn",
			DirectoryPath  = @"C:\\Projects\Tessyn",
			RootShortId    = "feedbeef",
			RootName       = "Boss",
			DirectCount    = 4,
			SubtreeCount   = 9,
		};

		FormatLines(Page(new[] { Row() }, new[] { section }, scope: SessionListScope.Subtree), out var lines);

		Assert.Equal("Workers of feedbeef \"Boss\" - 4 direct, 9 in subtree", lines[0]);
	}

	[Fact]
	public void SectionHeader_PrintedWhenSectionChanges()
	{
		var sections = new[] { DirSection("One", @"C:\one"), DirSection("Two", @"C:\two") };
		var rows = new[]
		{
			Row("aaaaaaaa", section: 0),
			Row("bbbbbbbb", section: 0),
			Row("cccccccc", section: 1),
		};

		FormatLines(Page(rows, sections), out var lines);

		Assert.StartsWith("One (", lines[0]);
		Assert.StartsWith("aaaaaaaa", lines[1]);
		Assert.StartsWith("bbbbbbbb", lines[2]);
		Assert.StartsWith("Two (", lines[3]);
		Assert.StartsWith("cccccccc", lines[4]);
		Assert.Equal(2, lines.Count(l => l.Contains(" top-level sessions, ")));
	}

	[Fact]
	public void Row_FlagsInSpecifiedOrder_OnlyWhenApplicable()
	{
		var plain = Row();
		var full = plain with
		{
			IsRunning          = true,
			IsFavorite         = true,
			IsResumable        = false,
			WorkerCount        = 3,
			RunningWorkerCount = 2,
			WorkerOfShortId    = "feedbeef",
			OtherDirectoryLabel = "Other",
			IsCaller           = true,
		};
		var workerOfYou = plain with { IsWorkerOfCaller = true, WorkerCount = 1 };

		FormatLines(Page(new[] { plain, full, workerOfYou }), out var lines);

		Assert.Equal("aaaaaaaa  Session  1h  profile=default", lines[1]);
		Assert.Equal("aaaaaaaa  Session  1h  profile=default  running  fav  no-resume  3 workers (2 running)  worker of feedbeef  [Other]  (you)", lines[2]);
		Assert.Equal("aaaaaaaa  Session  1h  profile=default  1 worker  worker of you", lines[3]);
	}

	[Fact]
	public void Row_IndentTwoSpacesPerLevel()
	{
		var rows = new[] { Row("aaaaaaaa", indent: 0), Row("bbbbbbbb", indent: 1), Row("cccccccc", indent: 3) };

		FormatLines(Page(rows), out var lines);

		Assert.StartsWith("aaaaaaaa  ", lines[1]);
		Assert.StartsWith("  bbbbbbbb  ", lines[2]);
		Assert.StartsWith("      cccccccc  ", lines[3]);
	}

	[Fact]
	public void Row_NameCutTo60CharactersEndingInDots()
	{
		var exact = new string('x', Constants.Agent.ListSessionsNameMaxLength);
		var longName = new string('y', 100);

		FormatLines(Page(new[] { Row(name: exact), Row(name: longName) }), out var lines);

		Assert.Equal($"aaaaaaaa  {exact}  1h  profile=default", lines[1]);
		Assert.Equal($"aaaaaaaa  {new string('y', Constants.Agent.ListSessionsNameMaxLength - 3)}...  1h  profile=default", lines[2]);
		Assert.Equal(Constants.Agent.ListSessionsNameMaxLength, lines[2].Split("  ")[1].Length);

		// An emoji (surrogate pair) straddling the cut position is dropped whole, never split.
		var cutAt = Constants.Agent.ListSessionsNameMaxLength - 3;
		var straddling = new string('z', cutAt - 1) + "😀" + new string('z', 20);
		FormatLines(Page(new[] { Row(name: straddling) }), out lines);
		var cutName = lines[1].Split("  ")[1];
		Assert.Equal(new string('z', cutAt - 1) + "...", cutName);
		Assert.DoesNotContain(cutName, char.IsSurrogate);
	}

	[Theory]
	[InlineData(0, "now")]
	[InlineData(59, "now")]
	[InlineData(60, "1m")]
	[InlineData(3599, "59m")]
	[InlineData(3600, "1h")]
	[InlineData(86399, "23h")]
	[InlineData(86400, "1d")]
	[InlineData(13 * 86400 + 86399, "13d")]
	[InlineData(14 * 86400, "2w")]
	[InlineData(55 * 86400, "7w")]
	[InlineData(56 * 86400, "1mo")]
	[InlineData(364 * 86400, "12mo")]
	[InlineData(365 * 86400, "1y")]
	[InlineData(800 * 86400, "2y")]
	[InlineData(-300, "now")]
	public void Age_Buckets(long secondsAgo, string expected)
	{
		var row = Row(last: Now.AddSeconds(-secondsAgo));

		FormatLines(Page(new[] { row }), out var lines);

		Assert.Equal($"aaaaaaaa  Session  {expected}  profile=default", lines[1]);
	}

	[Fact]
	public void Age_Buckets_MissingFile_Dash()
	{
		var row = Row() with { LastActivityUtc = null };

		FormatLines(Page(new[] { row }), out var lines);

		Assert.Equal("aaaaaaaa  Session  -  profile=default", lines[1]);
	}

	[Fact]
	public void Footer_RowsRangeAndNextPage()
	{
		var rows = new[] { Row("aaaaaaaa"), Row("bbbbbbbb") };

		FormatLines(Page(rows, total: 10, offset: 4), out var lines);
		Assert.Equal("Rows 5-6 of 10. Next page: offset=6.", lines[^2]);

		FormatLines(Page(rows, total: 6, offset: 4), out lines);
		Assert.Equal("Rows 5-6 of 6.", lines[^2]);
	}

	[Fact]
	public void Footer_HintsOnlyWhenTheyApply()
	{
		var rows = new[] { Row() };

		FormatLines(Page(rows, scope: SessionListScope.Project, workersHint: "feedbeef"), out var lines);
		Assert.Equal("Other projects: scope=all. Workers: scope=children nodeId=feedbeef. Narrow: query=, running=, activeWithinHours=.", lines[^1]);

		FormatLines(Page(rows, scope: SessionListScope.All), out lines);
		Assert.Equal("Narrow: query=, running=, activeWithinHours=.", lines[^1]);

		FormatLines(Page(rows, scope: SessionListScope.Children, workersHint: "feedbeef"), out lines);
		Assert.Equal("Workers: scope=children nodeId=feedbeef. Narrow: query=, running=, activeWithinHours=.", lines[^1]);
	}

	[Fact]
	public void Footer_OffsetPastEnd_NoRowsAtOffset()
	{
		var page = Page(Array.Empty<SessionListRowModel>(), total: 5, offset: 10);

		FormatLines(page, out var lines);

		Assert.Equal(3, lines.Length);
		Assert.StartsWith("Tessyn (", lines[0]);
		Assert.Equal("No rows at offset=10; 5 in total.", lines[1]);
		Assert.Equal("Other projects: scope=all. Narrow: query=, running=, activeWithinHours=.", lines[2]);
	}

	[Fact]
	public void YouLineAndHeaders_NamesCutTo60Characters()
	{
		var longName = new string('n', 100);
		var cut      = new string('n', Constants.Agent.ListSessionsNameMaxLength - 3) + "...";
		var caller = new SessionListCallerModel
		{
			ShortId            = "abcd1234",
			Name               = longName,
			DirectoryLabel     = "Tessyn",
			OrchestrationDepth = 1,
			SupervisorShortId  = "feedbeef",
			SupervisorName     = longName,
		};
		var workers = new SessionListSectionModel
		{
			Kind           = SessionListSectionKind.Workers,
			DirectoryLabel = "Tessyn",
			DirectoryPath  = @"C:\Projects\Tessyn",
			RootShortId    = "feedbeef",
			RootName       = longName,
			DirectCount    = 1,
			SubtreeCount   = 1,
		};

		FormatLines(Page(new[] { Row() }, new[] { workers }, caller, SessionListScope.Children), out var lines);

		Assert.Equal($"You: abcd1234 \"{cut}\" - worker (depth 1) of feedbeef \"{cut}\" in Tessyn", lines[0]);
		Assert.Equal($"Workers of feedbeef \"{cut}\" - 1 direct, 1 in subtree", lines[1]);
	}

	[Theory]
	[InlineData(@"C:\", @"C:\")]
	[InlineData(@"C:\Projects\Tessyn\", @"C:\Projects\Tessyn")]
	[InlineData(@"C:\Projects\Tessyn", @"C:\Projects\Tessyn")]
	public void SectionHeader_DriveRootPathKept(string path, string printed)
	{
		FormatLines(Page(new[] { Row() }, new[] { DirSection("Root", path) }), out var lines);

		Assert.Equal($"Root ({printed}) - 5 top-level sessions, 2 workers", lines[0]);
	}

	[Fact]
	public void Footer_NoRows_NoSessionsMatch()
	{
		var caller = new SessionListCallerModel { ShortId = "abcd1234", Name = "Boss", DirectoryLabel = "Tessyn" };

		var text = FormatLines(Page(Array.Empty<SessionListRowModel>(), caller: caller, total: 0), out var lines);

		Assert.Equal(4, lines.Length);
		Assert.StartsWith("You: ", lines[0]);
		Assert.StartsWith("Tessyn (", lines[1]);
		Assert.Equal("No sessions match.", lines[2]);
		Assert.Equal("Other projects: scope=all. Narrow: query=, running=, activeWithinHours=.", lines[3]);
		Assert.DoesNotContain("Rows ", text);
		Assert.False(text.EndsWith('\n'));
	}

	[Fact]
	public void Output_AsciiNames_WholeOutputIsAscii()
	{
		var caller = new SessionListCallerModel { ShortId = "abcd1234", Name = "Boss", DirectoryLabel = "Tessyn", OrchestrationDepth = 0 };
		var row = Row(name: "plain name") with { OtherDirectoryLabel = "Other", IsCaller = true };

		var text = FormatLines(Page(new[] { row }, caller: caller, workersHint: "feedbeef"), out _);

		Assert.All(text, c => Assert.True(c == '\n' || (c >= ' ' && c <= '~'), $"non-ASCII char U+{(int)c:X4}"));
	}

	[Fact]
	public void Names_NonAsciiPrintedAsIs_ControlCharactersBecomeSpaces()
	{
		var caller = new SessionListCallerModel { ShortId = "abcd1234", Name = "Böss", DirectoryLabel = "Téssyn", OrchestrationDepth = 0 };
		var section = DirSection("Téssyn", "C:\\Pröjects");
		var rows = new[]
		{
			Row("aaaaaaaa", name: "PH v4 — post"),
			Row("bbbbbbbb", name: "line\nbreak\r\ttab\u2028x\u2029y") with { OtherDirectoryLabel = "Über" },
			Row("cccccccc", name: "журнал"),
		};

		var text = FormatLines(Page(rows, new[] { section }, caller), out var lines);

		Assert.Equal("You: abcd1234 \"Böss\" - top-level session in Téssyn", lines[0]);
		Assert.Equal("Téssyn (C:\\Pröjects) - 5 top-level sessions, 2 workers", lines[1]);
		Assert.Equal("aaaaaaaa  PH v4 — post  1h  profile=default", lines[2]);
		Assert.Equal("bbbbbbbb  line break  tab x y  1h  profile=default  [Über]", lines[3]);
		Assert.Equal("cccccccc  журнал  1h  profile=default", lines[4]);
		Assert.DoesNotContain('\r', text);
	}
}
