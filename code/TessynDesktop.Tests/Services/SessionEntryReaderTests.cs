using System;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.Tests.Services;

/// <summary>session.entries / session.entry paging, truncation, caching and epochs (contract sections 3.10 and 3.11).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionEntryReaderTests : IDisposable
{
	private const string File = "a.txt";

	private readonly StubFileService _files = new();
	private readonly SessionEpochTracker _epochs;
	private readonly SessionEntryReader _sut;

	public SessionEntryReaderTests()
	{
		_epochs = new SessionEpochTracker(_files);
		_sut    = new SessionEntryReader(_files, _epochs);
	}

	public void Dispose() => _epochs.Dispose();

	private void Fill(int count)
	{
		_files.Set(File, Enumerable.Range(0, count).Select(i => User($"m{i}", i)));
	}

	private int[] Indexes(int? before, int? after, int limit)
	{
		var page = _sut.GetPage("n", File, before, after, limit);
		return page.Entries.Select(e => e.Index).ToArray();
	}

	[Fact]
	public void EmptyFile_YieldsAnEmptyPageForEveryMode()
	{
		_files.Set(File, []);

		Assert.Empty(Indexes(null, null, 50));
		Assert.Empty(Indexes(5, null, 50));
		Assert.Empty(Indexes(null, 3, 50));
		Assert.Equal(0, _sut.GetPage("n", File, null, null, 50).Total);
	}

	[Fact]
	public void MissingFile_YieldsAnEmptyPage()
	{
		var page = _sut.GetPage("n", "nope.txt", null, null, 50);

		Assert.Empty(page.Entries);
		Assert.Equal(0, page.Total);
	}

	[Fact]
	public void NoIndex_ReturnsTheLastEntriesAscending()
	{
		Fill(10);

		Assert.Equal([7, 8, 9], Indexes(null, null, 3));
	}

	[Fact]
	public void NoIndex_WithFewerEntriesThanTheLimit_ReturnsAll()
	{
		Fill(4);

		Assert.Equal([0, 1, 2, 3], Indexes(null, null, 50));
	}

	[Fact]
	public void AfterIndex_ReturnsLaterEntriesUpToTheLimit()
	{
		Fill(10);

		Assert.Equal([4, 5, 6], Indexes(null, 3, 3));
		Assert.Equal([9], Indexes(null, 8, 5));
	}

	[Fact]
	public void AfterIndex_AtOrBeyondTheEnd_IsEmpty()
	{
		Fill(10);

		Assert.Empty(Indexes(null, 9, 5));
		Assert.Empty(Indexes(null, 100, 5));
	}

	[Fact]
	public void AfterIndex_Negative_StartsAtTheBeginning()
	{
		Fill(4);

		Assert.Equal([0, 1], Indexes(null, -5, 2));
	}

	[Fact]
	public void BeforeIndex_ReturnsTheEntriesJustBeforeIt()
	{
		Fill(10);

		Assert.Equal([2, 3, 4], Indexes(5, null, 3));
	}

	[Fact]
	public void BeforeIndex_NearTheStart_ReturnsWhatIsThere()
	{
		Fill(10);

		Assert.Equal([0, 1], Indexes(2, null, 5));
		Assert.Empty(Indexes(0, null, 5));
	}

	[Fact]
	public void BeforeIndex_BeyondTheEnd_IsClampedToTheLastEntries()
	{
		Fill(10);

		Assert.Equal([8, 9], Indexes(500, null, 2));
	}

	[Fact]
	public void Total_CountsEveryParsedEntry_EmptyOnesIncluded()
	{
		_files.Set(File, [User("a"), Assistant("   "), Entry(Constants.SessionFile.RoleCompaction, string.Empty), User("b")]);

		var page = _sut.GetPage("n", File, null, null, 50);

		Assert.Equal(4, page.Total);
		Assert.Equal([0, 1, 2, 3], page.Entries.Select(e => e.Index));
		Assert.Equal("compaction", page.Entries[2].Role);
	}

	[Fact]
	public void Entry_MapsRoleAndHeaderAttributes()
	{
		_files.Set(File, [User("q", profile: "Work"), Assistant("a", model: "claude-opus-5-5", effort: "high")]);

		var page = _sut.GetPage("n", File, null, null, 50);

		Assert.Equal("user", page.Entries[0].Role);
		Assert.Equal("Work", page.Entries[0].ProfileName);
		Assert.Equal("assistant", page.Entries[1].Role);
		Assert.Equal("claude-opus-5-5", page.Entries[1].ModelId);
		Assert.Equal("high", page.Entries[1].Effort);
		Assert.Equal(T0, page.Entries[0].Timestamp);
	}

	[Fact]
	public void LongContent_IsCappedAndFlagged_ExactlyAtTheCapIsNot()
	{
		var cap = Constants.Remote.Limits.EntryContentMaxChars;
		_files.Set(File, [User(new string('x', cap + 10)), User(new string('y', cap))]);

		var page = _sut.GetPage("n", File, null, null, 50);

		Assert.Equal(cap, page.Entries[0].Content.Length);
		Assert.True(page.Entries[0].Truncated);
		Assert.Equal(cap + 10, page.Entries[0].FullLength);
		Assert.Equal(cap, page.Entries[1].Content.Length);
		Assert.Null(page.Entries[1].Truncated);
		Assert.Null(page.Entries[1].FullLength);
	}

	[Fact]
	public void GetEntry_ReturnsTheFullContent()
	{
		var cap = Constants.Remote.Limits.EntryContentMaxChars;
		_files.Set(File, [User("short"), User(new string('x', cap * 2))]);

		var entry = _sut.GetEntry(File, 1, epoch: 0);

		Assert.Equal(cap * 2, entry.Content.Length);
		Assert.Null(entry.Truncated);
		Assert.Equal(1, entry.Index);
	}

	[Fact]
	public void GetEntry_WithAStaleEpoch_Throws()
	{
		_files.Set(File, [User("a")]);
		_files.RaiseRewritten(File);

		var error = Assert.Throws<TdcpException>(() => _sut.GetEntry(File, 0, epoch: 0));

		Assert.Equal("Session history changed", error.Message);
		Assert.Equal("a", _sut.GetEntry(File, 0, epoch: 1).Content);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(1)]
	public void GetEntry_OutOfRange_Throws(int index)
	{
		_files.Set(File, [User("a")]);

		Assert.Throws<TdcpException>(() => _sut.GetEntry(File, index, epoch: 0));
	}

	[Fact]
	public void Epoch_IsReportedWithThePage_AndCountsRewrites()
	{
		Fill(2);
		Assert.Equal(0, _sut.GetPage("n", File, null, null, 5).Epoch);

		_files.RaiseRewritten(File);
		_files.RaiseRewritten(File);

		Assert.Equal(2, _sut.GetPage("n", File, null, null, 5).Epoch);
	}

	[Fact]
	public void Parsing_IsCached_WhileTheStampIsUnchanged()
	{
		Fill(3);

		_sut.GetPage("n", File, null, null, 5);
		_sut.GetPage("n", File, null, 0, 5);
		_sut.GetAll(File);

		Assert.Equal(1, _files.ReadCalls);
	}

	[Fact]
	public void Cache_IsInvalidatedByANewLength_OrANewWriteTime()
	{
		_files.Set(File, [User("a")], length: 10);
		Assert.Single(_sut.GetAll(File));

		_files.Set(File, [User("a"), User("b")], length: 20);
		Assert.Equal(2, _sut.GetAll(File).Count);

		_files.Set(File, [User("a"), User("b"), User("c")], length: 20, lastWrite: T0.AddHours(1));
		Assert.Equal(3, _sut.GetAll(File).Count);
		Assert.Equal(3, _files.ReadCalls);
	}

	[Fact]
	public void Cache_IsBounded_ButStaysCorrect()
	{
		var files = Enumerable.Range(0, Constants.Remote.EntryCache.MaxFiles + 4).Select(i => $"f{i}.txt").ToList();
		foreach (var file in files)
			_files.Set(file, [User(file)]);

		foreach (var file in files)
			_sut.GetAll(file);

		var readsBefore = _files.ReadCalls;
		Assert.Equal("f0.txt", _sut.GetAll("f0.txt")[0].Content);   // evicted: parsed again
		Assert.Equal(readsBefore + 1, _files.ReadCalls);
		_sut.GetAll(files[^1]);                                       // the newest is still cached
		Assert.Equal(readsBefore + 1, _files.ReadCalls);
	}
}
