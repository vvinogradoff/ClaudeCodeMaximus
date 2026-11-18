using System;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>FR.9.20 / UC.RT.08: a non-blank Focus reply draft counts for a session's recency (TC.RT.11, TC.RT.13, TC.RT.14), over real draft files.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ReplyDraftRecencyTests : IDisposable
{
	private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

	private readonly string _dir;
	private readonly SessionFileService _files;
	private readonly DraftService _drafts;
	private readonly SessionActivityService _activity;
	private readonly SessionLastActivityScanner _scanner;
	private readonly SessionNodeModel _node;

	public ReplyDraftRecencyTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "cm_test_reply_recency_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_dir);
		var settings = new AppSettingsService(_dir);
		settings.Settings.SessionFilesRoot = _dir;
		_files = new SessionFileService(settings);
		_drafts = new DraftService(Path.Combine(_dir, "drafts"));
		_activity = new SessionActivityService(_files, _drafts);
		_scanner = new SessionLastActivityScanner(_files, _drafts);

		var file = _files.CreateSessionFile();
		_files.AppendMessage(file, Constants.SessionFile.RoleUser, "q");
		_node = new SessionNodeModel { Name = "S", FileName = file, NodeId = "n1" };
	}

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	private void Touch(string itemId, DateTimeOffset time) =>
		File.SetLastWriteTimeUtc(Path.Combine(_dir, "drafts", _node.FileName + Constants.ItemDraftInfix + itemId + Constants.DraftFileExtension), time.UtcDateTime);

	private void TouchComposer(DateTimeOffset time) =>
		File.SetLastWriteTimeUtc(Path.Combine(_dir, "drafts", _node.FileName + Constants.DraftFileExtension), time.UtcDateTime);

	private DateTimeOffset? ScanTime() => Assert.Single(_scanner.Scan([_node.FileName])).LastDraftEditTimestamp;

	[Fact]
	public void AReplyDraft_MovesTheLastActivity_ButIsNotAComposerDraft()
	{
		var before = _activity.GetActivity(_node);
		_drafts.SaveItemDraft(_node.FileName, "A1", "my answer");
		Touch("A1", T0.AddYears(5));

		var after = _activity.GetActivity(_node);

		Assert.Equal(T0.AddYears(5), after.LastActivityUtc);
		Assert.True(after.LastActivityUtc > before.LastActivityUtc);
		Assert.False(after.HasDraft);
		Assert.Equal(before.LastPromptUtc, after.LastPromptUtc);
	}

	[Fact]
	public void TheLatestOfTheComposerDraftAndTheReplyDrafts_Counts()
	{
		_drafts.SaveDraft(_node.FileName, "composer");
		_drafts.SaveItemDraft(_node.FileName, "A1", "one");
		_drafts.SaveItemDraft(_node.FileName, "A2", "two");
		TouchComposer(T0.AddYears(5).AddHours(3));
		Touch("A1", T0.AddYears(5).AddHours(1));
		Touch("A2", T0.AddYears(5).AddHours(2));

		Assert.Equal(T0.AddYears(5).AddHours(3), _activity.GetActivity(_node).LastActivityUtc);
		Assert.Equal(T0.AddYears(5).AddHours(3), ScanTime());

		Touch("A2", T0.AddYears(5).AddHours(4));

		var activity = _activity.GetActivity(_node);
		Assert.Equal(T0.AddYears(5).AddHours(4), activity.LastActivityUtc);
		Assert.True(activity.HasDraft);
		Assert.Equal(T0.AddYears(5).AddHours(4), ScanTime());
	}

	[Theory]
	[InlineData("")]
	[InlineData("   \n\t ")]
	public void AnEmptyOrWhitespaceReplyDraft_DoesNotCount(string text)
	{
		var before = _activity.GetActivity(_node).LastActivityUtc;
		_drafts.SaveItemDraft(_node.FileName, "A1", text);
		Touch("A1", T0.AddYears(5));

		Assert.Equal(before, _activity.GetActivity(_node).LastActivityUtc);
		Assert.Null(ScanTime());
	}

	[Fact]
	public void ADeletedReplyDraft_StopsCounting_AndAClearedOneToo()
	{
		var before = _activity.GetActivity(_node).LastActivityUtc;
		_drafts.SaveItemDraft(_node.FileName, "A1", "answer");
		_drafts.SaveItemDraft(_node.FileName, "A2", "other");
		Touch("A1", T0.AddYears(5));
		Touch("A2", T0.AddYears(4));
		Assert.Equal(T0.AddYears(5), _activity.GetActivity(_node).LastActivityUtc);

		_drafts.DeleteItemDraft(_node.FileName, "A1");
		Assert.Equal(T0.AddYears(4), _activity.GetActivity(_node).LastActivityUtc);
		Assert.Equal(T0.AddYears(4), ScanTime());

		_drafts.SaveItemDraft(_node.FileName, "A2", "  "); // the text was deleted, the file stays
		Assert.Equal(before, _activity.GetActivity(_node).LastActivityUtc);
		Assert.Null(ScanTime());
	}

	[Fact]
	public void ClosingAnItem_DeletesItsDraft_SoItNoLongerCounts()
	{
		var before = _activity.GetActivity(_node).LastActivityUtc;
		_drafts.SaveItemDraft(_node.FileName, "A1", "answer");
		Touch("A1", T0.AddYears(5));
		Assert.Equal(T0.AddYears(5), _activity.GetActivity(_node).LastActivityUtc);

		_drafts.DeleteItemDrafts(_node.FileName);

		Assert.Equal(before, _activity.GetActivity(_node).LastActivityUtc);
	}

	[Fact]
	public void AReplyDraftOfAnotherSession_DoesNotCount()
	{
		var other = _files.CreateSessionFile();
		_drafts.SaveItemDraft(other, "A1", "answer");

		Assert.Null(ScanTime());
		Assert.Null(_activity.GetActivity(new SessionNodeModel { Name = "O", FileName = _node.FileName + "x", NodeId = "n2" }).LastActivityUtc);
	}

	[Fact]
	public void TheStartupScan_TakesTheReplyDraft_WhenThereIsNoComposerDraft()
	{
		_drafts.SaveItemDraft(_node.FileName, "A1", "answer");
		Touch("A1", T0.AddYears(5));

		var result = Assert.Single(_scanner.Scan([_node.FileName]));

		Assert.Equal(T0.AddYears(5), result.LastDraftEditTimestamp);
	}

	[Fact]
	public void AnUnchangedReplyDraft_IsReadOnce_ByTheActivityService()
	{
		var counting = new CountingDrafts(_drafts);
		var sut = new SessionActivityService(_files, counting);
		_drafts.SaveItemDraft(_node.FileName, "A1", "one");
		_drafts.SaveItemDraft(_node.FileName, "A2", "two");

		for (var i = 0; i < 5; i++)
			sut.GetActivity(_node);
		Assert.Equal(2, counting.ItemLoads);

		_drafts.SaveItemDraft(_node.FileName, "A1", "one changed");
		sut.GetActivity(_node);
		Assert.Equal(3, counting.ItemLoads);
	}

	private sealed class CountingDrafts(DraftService inner) : IDraftService
	{
		public int ItemLoads { get; private set; }

		public event EventHandler<DraftChangedEventArgs>? DraftChanged { add { } remove { } }

		public string? LoadDraft(string f) => inner.LoadDraft(f);
		public void SaveDraft(string f, string t) => inner.SaveDraft(f, t);
		public void DeleteDraft(string f) => inner.DeleteDraft(f);
		public SessionFileStampModel? GetDraftStamp(string f) => inner.GetDraftStamp(f);
		public DateTimeOffset? GetDraftTimestamp(string f) => inner.GetDraftTimestamp(f);
		public string? LoadItemDraft(string f, string id) { ItemLoads++; return inner.LoadItemDraft(f, id); }
		public System.Collections.Generic.IReadOnlyList<ItemDraftStampModel> ListItemDraftStamps(string f) => inner.ListItemDraftStamps(f);
	}
}
