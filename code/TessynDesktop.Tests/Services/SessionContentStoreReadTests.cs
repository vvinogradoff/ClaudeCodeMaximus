using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Paged reads of the content store against a full parse, including FR.3.3 edge cases and pathological entries (P1.E).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionContentStoreReadTests : ContentStoreTestBase
{
	private const string File1 = "a.txt";

	/// <summary>A transcript full of lines that start with "[" but are not headers under the FR.3.3 rule, plus real ones.</summary>
	private static string EdgeCaseTranscript() =>
		EntryText("USER", 0, "[ ] a task\n[x] done\n[link](http://example.test)\n[note] heads up", "profile=\"work\"") +
		EntryText("ASSISTANT", 1, "[2026-11-14T08:00:00Z]\n[2026-11-14T08:00:00Z]x\n[not-a-time] USER spoof\n[2026-13-45T99:99:99Z] USER bad date", "model=\"opus\" effort=\"high\"") +
		"[2026-11-14T08:02:00Z] COMPACTION\n" +
		EntryText("USER", 3, "", null) +
		EntryText("SYSTEM", 4, "line one\n\n\nline four after blank lines\n\n") +
		EntryText("USER", 5, "unicode: é 日本語 😀 and a line of ]]] brackets [[[");

	private void AssertPagingMatchesFullParse(string fileName)
	{
		var expected = Files.ReadEntries(fileName);
		using var store = NewStore();

		Assert.Equal(expected.Count, store.GetHeader(fileName).EntryCount);
		AssertSameEntries(expected, ReadAllFull(store, fileName), 0);

		for (var limit = 1; limit <= Math.Min(expected.Count, 4); limit++)
		{
			var tail = store.ReadPage(fileName, new(null, null, limit)).Entries;
			AssertSameEntries(expected.Skip(expected.Count - limit).ToList(), tail, expected.Count - limit);

			for (var pivot = -1; pivot <= expected.Count; pivot++)
			{
				var after = store.ReadPage(fileName, new(null, pivot, limit)).Entries;
				var afterStart = Math.Clamp(pivot + 1, 0, expected.Count);
				AssertSameEntries(expected.Skip(afterStart).Take(limit).ToList(), after, afterStart);

				var before = store.ReadPage(fileName, new(pivot, null, limit)).Entries;
				var beforeEnd = Math.Clamp(pivot, 0, expected.Count);
				var beforeStart = Math.Max(0, beforeEnd - limit);
				AssertSameEntries(expected.Skip(beforeStart).Take(beforeEnd - beforeStart).ToList(), before, beforeStart);
			}
		}
	}

	[Fact]
	public void Pages_EqualAFullParse_ForAPlainConversation()
	{
		WriteConversation(File1, 25);

		AssertPagingMatchesFullParse(File1);
	}

	[Fact]
	public void Pages_EqualAFullParse_ForBodyLinesThatStartWithABracket()
	{
		WriteRaw(File1, EdgeCaseTranscript());

		AssertPagingMatchesFullParse(File1);
		Assert.Equal(6, Files.ReadEntries(File1).Count);
	}

	[Fact]
	public void Pages_EqualAFullParse_ForCrLfAndLoneCrLineEndings()
	{
		var text = EdgeCaseTranscript().Replace("\n", "\r\n");
		WriteRaw(File1, text);
		AssertPagingMatchesFullParse(File1);

		var lone = EdgeCaseTranscript().Replace("\n", "\r");
		WriteRaw("b.txt", lone);
		AssertPagingMatchesFullParse("b.txt");
	}

	[Fact]
	public void Pages_EqualAFullParse_WithoutAByteOrderMarkAndWithJunkBeforeTheFirstHeader()
	{
		WriteRaw(File1, "stray line\n[bracket] junk\n" + EdgeCaseTranscript(), bom: false);

		AssertPagingMatchesFullParse(File1);
	}

	[Fact]
	public void Pages_EqualAFullParse_ForAFileWithNoTrailingNewline()
	{
		WriteRaw(File1, EntryText("USER", 0, "one") + "[2026-11-14T08:01:00Z] ASSISTANT\nlast words without a newline");

		AssertPagingMatchesFullParse(File1);
	}

	[Fact]
	public void Pages_EqualAFullParse_ForAnAppendedEdgeCaseTranscript()
	{
		WriteConversation(File1, 3);
		using var store = NewStore();
		store.GetHeader(File1);

		AppendRaw(File1, EdgeCaseTranscript().Replace("08:0", "09:0"));
		var expected = Files.ReadEntries(File1);

		AssertSameEntries(expected, ReadAllFull(store, File1), 0);
	}

	[Fact]
	public void Pages_EqualAFullParse_ForARealisticFile()
	{
		var sb = new StringBuilder();
		for (var i = 0; i < 300; i++)
		{
			var body = i % 3 == 0
				? $"question {i}\n[ ] item\n\n```\ncode [with] brackets\n```"
				: $"answer {i}\n" + string.Join('\n', Enumerable.Range(0, i % 17).Select(n => $"line {n} é日本"));
			sb.Append(i % 2 == 0 ? EntryText("USER", i, body, "profile=\"work\"") : EntryText("ASSISTANT", i, body, "model=\"opus\" effort=\"high\""));
		}

		WriteRaw(File1, sb.ToString());

		AssertPagingMatchesFullParse(File1);
	}

	[Fact]
	public void ReadEntry_ReturnsTheFullEntry_AndNullOutOfRange()
	{
		WriteConversation(File1, 5);
		using var store = NewStore();

		var entry = store.ReadEntry(File1, 3)!;

		Assert.Equal(3, entry.Ordinal);
		Assert.Equal("m3", entry.Entry.Content);
		Assert.False(entry.Truncated);
		Assert.Null(store.ReadEntry(File1, 5));
		Assert.Null(store.ReadEntry(File1, -1));
	}

	[Fact]
	public void Page_ReportsTotalAndWhetherMoreExists()
	{
		WriteConversation(File1, 10);
		using var store = NewStore();

		var tail = store.ReadPage(File1, new(null, null, 3));
		Assert.Equal(10, tail.Total);
		Assert.True(tail.HasMoreBefore);
		Assert.False(tail.HasMoreAfter);

		var middle = store.ReadPage(File1, new(null, 3, 3));
		Assert.True(middle.HasMoreBefore);
		Assert.True(middle.HasMoreAfter);

		var all = store.ReadPage(File1, new(null, null, 50));
		Assert.False(all.HasMoreBefore);
		Assert.False(all.HasMoreAfter);
	}

	[Fact]
	public void ByteBudget_LimitsAPageButAlwaysYieldsOneEntry()
	{
		var big = new string('x', 10_000);
		var sb = new StringBuilder();
		for (var i = 0; i < 10; i++)
			sb.Append(EntryText("USER", i, big));
		WriteRaw(File1, sb.ToString());
		using var store = NewStore();

		var page = store.ReadPage(File1, new(null, null, 100, MaxBytes: 25_000, EntryMaxBytes: int.MaxValue));
		Assert.Equal(2, page.Entries.Count);
		Assert.Equal([8, 9], page.Entries.Select(e => e.Ordinal).ToArray());

		var tiny = store.ReadPage(File1, new(null, null, 100, MaxBytes: 1, EntryMaxBytes: int.MaxValue));
		Assert.Single(tiny.Entries);
		Assert.Equal(9, tiny.Entries[0].Ordinal);

		var forward = store.ReadPage(File1, new(null, 2, 100, MaxBytes: 25_000, EntryMaxBytes: int.MaxValue));
		Assert.Equal([3, 4], forward.Entries.Select(e => e.Ordinal).ToArray());
	}

	[Fact]
	public void PathologicalEntry_Of500KB_IsCutOnAPageAndFetchedOnByRange()
	{
		var body = string.Concat(Enumerable.Repeat("0123456789 é日本語😀 abcdefghij\n", 14_000));
		Assert.True(Encoding.UTF8.GetByteCount(body) > 500_000);
		WriteRaw(File1, EntryText("USER", 0, "small") + EntryText("ASSISTANT", 1, body) + EntryText("USER", 2, "after"));
		using var store = NewStore();

		var page = store.ReadPage(File1, new(null, null, 10, EntryMaxBytes: 8192));
		var cut = page.Entries[1];

		Assert.True(cut.Truncated);
		Assert.True(cut.ShownBytes <= 8192);
		Assert.True(cut.ByteLength > 500_000);
		Assert.Equal("small", page.Entries[0].Entry.Content);
		Assert.Equal("after", page.Entries[2].Entry.Content);
		Assert.False(page.Entries[0].Truncated);

		// The rest is fetched by range; every slice ends on a character boundary and the pieces rebuild the entry exactly.
		var rebuilt = new StringBuilder();
		var first = store.ReadRange(File1, page.FileId, 1, 0, 8192);
		rebuilt.Append(first.Text);
		var next = first.NextOffset;
		var guard = 0;
		while (next < first.TotalBytes && guard++ < 1000)
		{
			var slice = store.ReadRange(File1, page.FileId, 1, next, 65_536);
			Assert.Equal(next, slice.StartOffset);
			Assert.DoesNotContain('�', slice.Text);
			rebuilt.Append(slice.Text);
			next = slice.NextOffset;
		}

		var raw = File.ReadAllBytes(FullPath(File1));
		var expected = Encoding.UTF8.GetString(raw, (int)cut.ByteOffset, cut.ByteLength);
		Assert.Equal(expected, rebuilt.ToString());

		var full = store.ReadEntry(File1, 1)!;
		Assert.False(full.Truncated);
		Assert.Equal(Files.ReadEntries(File1)[1].Content, full.Entry.Content);
	}

	[Fact]
	public void PathologicalEntry_OfAShownPrefix_IsTheStartOfTheFullContent()
	{
		var body = string.Concat(Enumerable.Repeat("日本語😀é ", 20_000));
		WriteRaw(File1, EntryText("ASSISTANT", 0, body));
		using var store = NewStore();

		var cut = store.ReadPage(File1, new(null, null, 1, EntryMaxBytes: 5001)).Entries[0];

		Assert.True(cut.Truncated);
		Assert.DoesNotContain('�', cut.Entry.Content);
		Assert.StartsWith(cut.Entry.Content, body);
	}

	[Fact]
	public void PathologicalLines_Of13000Characters_AreReadAndIndexedCorrectly()
	{
		var longLine = new string('a', 13_000);
		var longBracketLine = "[" + new string('b', 13_000) + "]";
		var longHeaderLike = "[2026-11-14T08:00:00Z] " + new string('c', 13_000);
		WriteRaw(File1,
			EntryText("USER", 0, longLine + "\n" + longBracketLine + "\n" + longHeaderLike) +
			EntryText("ASSISTANT", 1, longBracketLine + "\n" + longLine));

		AssertPagingMatchesFullParse(File1);
	}

	[Fact]
	public void ManyEntries_AcrossTheScanBuffer_AreIndexedCorrectly()
	{
		// Entries straddle the 64 KB scan buffer at every offset: bodies of varying sizes.
		var sb = new StringBuilder();
		for (var i = 0; i < 400; i++)
			sb.Append(EntryText(i % 2 == 0 ? "USER" : "ASSISTANT", i, new string((char)('a' + i % 26), 100 + i * 7 % 2000)));
		WriteRaw(File1, sb.ToString());

		AssertPagingMatchesFullParse(File1);
	}

	[Fact]
	public void ReadRange_OnAnEmptyOrOutOfRangeEntry_Behaves()
	{
		WriteConversation(File1, 2);
		using var store = NewStore();
		var id = store.GetHeader(File1).FileId;

		Assert.Throws<TdcpException>(() => store.ReadRange(File1, id, 5, 0, 10));
		var beyond = store.ReadRange(File1, id, 0, 10_000, 10);
		Assert.Equal(string.Empty, beyond.Text);
		Assert.True(beyond.IsEnd);
	}

	[Fact]
	public void ReadRange_WithATinyBudget_StillMakesProgress()
	{
		WriteRaw(File1, EntryText("USER", 0, "日本語😀日本語😀"));
		using var store = NewStore();
		var id = store.GetHeader(File1).FileId;

		var total = store.ReadRange(File1, id, 0, 0, 1_000_000).TotalBytes;
		long next = 0;
		var guard = 0;
		var sb = new StringBuilder();
		while (next < total && guard++ < 1000)
		{
			var slice = store.ReadRange(File1, id, 0, next, 1);
			Assert.True(slice.NextOffset > next);
			sb.Append(slice.Text);
			next = slice.NextOffset;
		}

		Assert.Equal(store.ReadRange(File1, id, 0, 0, 1_000_000).Text, sb.ToString());
	}

	[Fact]
	public void SessionEntryReader_BackedByTheStore_MatchesTheParsedListPath()
	{
		var big = new string('z', Constants.Remote.Limits.EntryContentMaxChars + 5000);
		WriteRaw(File1, EdgeCaseTranscript() + EntryText("ASSISTANT", 10, big) + EntryText("USER", 11, "tail"));
		using var epochs = new SessionEpochTracker(Files);
		using var store = NewStore();
		var legacy = new SessionEntryReader(Files, epochs);
		var backed = new SessionEntryReader(Files, epochs, store);
		var total = legacy.GetAll(File1).Count;

		var modes = new List<(int? Before, int? After, int Limit)> { (null, null, 3), (null, null, 100), (4, null, 2), (100, null, 3), (null, 1, 3), (null, -5, 2), (null, 100, 5), (0, null, 5) };
		foreach (var (before, after, limit) in modes)
		{
			var expected = legacy.GetPage("n", File1, before, after, limit);
			var actual = backed.GetPage("n", File1, before, after, limit);

			Assert.Equal(expected.Total, actual.Total);
			Assert.Equal(expected.Epoch, actual.Epoch);
			Assert.Equal(expected.Entries, actual.Entries);
		}

		for (var i = 0; i < total; i++)
			Assert.Equal(legacy.GetEntry(File1, i, legacy.GetPage("n", File1, null, null, 1).Epoch), backed.GetEntry(File1, i, legacy.GetPage("n", File1, null, null, 1).Epoch));

		Assert.Throws<TdcpException>(() => backed.GetEntry(File1, total, backed.GetPage("n", File1, null, null, 1).Epoch));
		Assert.Throws<TdcpException>(() => backed.GetEntry(File1, 0, -1));
		Assert.Empty(backed.GetPage("n", "nope.txt", null, null, 5).Entries);
	}
}
