using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>V6: the revision check of <c>draft.set</c> and the write are one step, serialised with the desktop composer's saves.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DraftServiceAtomicWriteTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "draft-atomic-" + Guid.NewGuid().ToString("N"));
	private readonly DraftService _sut;

	public DraftServiceAtomicWriteTests() => _sut = new DraftService(_dir);

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	[Fact]
	public void TryWriteDraft_WritesWhenTheCheckAccepts_AndRefusesWithoutWritingWhenItDoesNot()
	{
		_sut.SaveDraft("s.txt", "old");

		Assert.False(_sut.TryWriteDraft("s.txt", "new", current => current == "something else"));
		Assert.Equal("old", _sut.LoadDraft("s.txt"));

		Assert.True(_sut.TryWriteDraft("s.txt", "new", current => current == "old"));
		Assert.Equal("new", _sut.LoadDraft("s.txt"));
	}

	[Fact]
	public void TryWriteDraft_WithAnEmptyText_Deletes_AndTheCheckSeesNullForNoDraft()
	{
		string? seen = "unset";

		Assert.True(_sut.TryWriteDraft("none.txt", "x", current => { seen = current; return true; }));
		Assert.Null(seen);

		Assert.True(_sut.TryWriteDraft("none.txt", string.Empty, _ => true));
		Assert.Null(_sut.LoadDraft("none.txt"));
	}

	[Fact]
	public async Task ASaveOfTheDesktopComposer_CannotSlipInBetweenTheCheckAndTheWrite()
	{
		_sut.SaveDraft("s.txt", "base");
		var insideCheck = new ManualResetEventSlim();
		var composerSaved = new ManualResetEventSlim();
		var composer = Task.Run(() =>
		{
			insideCheck.Wait();
			_sut.SaveDraft("s.txt", "typed on the desktop"); // must wait for the compare-and-write to finish
			composerSaved.Set();
		});

		var written = _sut.TryWriteDraft("s.txt", "remote edit", current =>
		{
			insideCheck.Set();
			Assert.False(composerSaved.Wait(300), "the composer's save got in between the check and the write");
			return current == "base";
		});

		await composer.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.True(written);
		Assert.Equal("typed on the desktop", _sut.LoadDraft("s.txt")); // it came after, in order: not lost, not interleaved
	}

	[Fact]
	public void TheChangeEvent_IsRaisedOncePerEffectiveWrite_AndNotForARefusedOne()
	{
		var raised = 0;
		_sut.DraftChanged += (_, _) => raised++;
		_sut.SaveDraft("s.txt", "a");

		_sut.TryWriteDraft("s.txt", "b", current => current == "wrong");
		_sut.TryWriteDraft("s.txt", "b", current => current == "a");
		_sut.TryWriteDraft("missing.txt", string.Empty, _ => true); // deleting a missing draft changes nothing

		Assert.Equal(2, raised);
	}
}
