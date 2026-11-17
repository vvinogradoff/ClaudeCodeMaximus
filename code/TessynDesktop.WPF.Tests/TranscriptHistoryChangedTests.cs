using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// ISSUE.495: a session file rewritten under an open session (the duplicate cleanup) renumbers its entries. The loaded model must notice, otherwise a read
/// "after the last loaded index" returns nothing and the Architect's new inputs stay invisible until the session is reopened.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptHistoryChangedTests
{
	private static SessionEntryDto Entry(int index) => new() { Index = index, Role = "user", Content = "x", Timestamp = DateTimeOffset.UnixEpoch };

	private static TranscriptModel Model(int epoch, string? fileId, int loaded) =>
		new("n", epoch, loaded, Enumerable.Range(0, loaded).Select(Entry).ToList(), [], fileId);

	private static SessionEntriesResult Page(int epoch, int total, string? fileId) => new("n", epoch, total, [], fileId);

	[Fact]
	public void The_same_history_is_not_a_change_however_much_was_appended()
	{
		Assert.False(Model(0, "f1", 10).HistoryChanged(Page(0, 10, "f1")));
		Assert.False(Model(0, "f1", 10).HistoryChanged(Page(0, 14, "f1")));
		Assert.False(Model(0, null, 10).HistoryChanged(Page(0, 12, null)));
	}

	[Fact]
	public void Another_epoch_is_a_change()
	{
		Assert.True(Model(0, "f1", 10).HistoryChanged(Page(1, 10, "f1")));
	}

	[Fact]
	public void Another_file_generation_is_a_change_even_when_the_file_grew()
	{
		Assert.True(Model(0, "f1", 10).HistoryChanged(Page(0, 14, "f2")));
	}

	[Fact]
	public void A_file_with_fewer_entries_than_the_newest_loaded_index_is_a_change_without_any_ids()
	{
		Assert.True(Model(0, null, 10).HistoryChanged(Page(0, 8, null)));
		Assert.True(Model(0, null, 10).HistoryChanged(Page(0, 9, null)));   // entry 9 is loaded, the file holds indexes 0..8
	}
}
