using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.FV.01 — the Attention Ledger sidecar file store (FR.23.3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionLedgerStoreTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"attention-store-tests-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}

	private static AttentionItemModel FullItem(int number, AttentionKind kind) => new()
	{
		Id                    = $"A{number}",
		Number                = number,
		Kind                  = kind,
		Title                 = $"Title {number}",
		Body                  = $"Body {number}\nsecond line",
		Options               = ["yes", "no"],
		Recommendation        = "yes",
		Key                   = $"key-{number}",
		Status                = AttentionStatus.Answered,
		Source                = AttentionSource.NotifyUser,
		Answer                = "the answer",
		ResolutionNote        = "the note",
		IsUserCloseUnreported = true,
		CreatedUtc            = new DateTimeOffset(2026, 11, 1, 10, 0, number, TimeSpan.Zero),
		UpdatedUtc            = new DateTimeOffset(2026, 11, 2, 10, 0, number, TimeSpan.Zero),
		ClosedUtc             = new DateTimeOffset(2026, 11, 3, 10, 0, number, TimeSpan.Zero),
	};

	private static string Json(AttentionLedgerModel ledger) => JsonSerializer.Serialize(ledger);

	[Fact]
	public void RoundTrip_FreshStoreOnSameRoot_LoadsEqualLedger()   // TC.FV.01-1
	{
		var ledger = new AttentionLedgerModel
		{
			NodeId     = "node-1",
			NextNumber = 4,
			Items      = [FullItem(1, AttentionKind.Critical), FullItem(2, AttentionKind.Todo), FullItem(3, AttentionKind.Decided)],
		};
		new AttentionLedgerStore(_root).Save(ledger);

		var loaded = new AttentionLedgerStore(_root).Load("node-1");

		Assert.Equal(Json(ledger), Json(loaded));
		Assert.Equal(["yes", "no"], loaded.Items[0].Options);
		Assert.Equal(AttentionKind.Todo, loaded.Items[1].Kind);
	}

	[Fact]
	public void Load_UnknownNode_ReturnsEmptyLedgerAndCreatesNothing()   // TC.FV.01-2
	{
		var ledger = new AttentionLedgerStore(_root).Load("ghost");

		Assert.Equal("ghost", ledger.NodeId);
		Assert.Equal(1, ledger.NextNumber);
		Assert.Empty(ledger.Items);
		Assert.False(Directory.Exists(_root) && Directory.GetFiles(_root).Length > 0);
	}

	[Fact]
	public void Load_CorruptFile_MovesItAsideAndStartsEmpty()   // TC.FV.01-3
	{
		Directory.CreateDirectory(_root);
		var path = Path.Combine(_root, "node-1.json");
		File.WriteAllText(path, "{ not json");
		var store = new AttentionLedgerStore(_root);

		var ledger = store.Load("node-1");

		Assert.Empty(ledger.Items);
		Assert.Equal(1, ledger.NextNumber);
		Assert.False(File.Exists(path));
		var bad = Assert.Single(Directory.GetFiles(_root, "node-1.json.bad-*"));
		Assert.Matches(@"node-1\.json\.bad-\d{14}$", bad);
		Assert.Equal("{ not json", File.ReadAllText(bad));

		// The next save writes a fresh ledger and leaves the .bad-* file alone.
		ledger.Items.Add(FullItem(1, AttentionKind.Ask));
		ledger.NextNumber = 2;
		store.Save(ledger);

		Assert.True(File.Exists(path));
		Assert.Equal("{ not json", File.ReadAllText(bad));
		Assert.Single(store.Load("node-1").Items);
	}

	[Fact]
	public void Save_LeavesOnlyTheLedgerFile()   // TC.FV.01-4
	{
		new AttentionLedgerStore(_root).Save(new AttentionLedgerModel { NodeId = "node-1" });

		Assert.Equal(["node-1.json"], Directory.GetFiles(_root).Select(Path.GetFileName).ToArray());
	}

	[Fact]
	public void Save_WhenTempStepFails_PreviousFileStillLoads()   // TC.FV.01-4
	{
		var store = new AttentionLedgerStore(_root);
		var first = new AttentionLedgerModel { NodeId = "node-1", NextNumber = 2, Items = [FullItem(1, AttentionKind.Ask)] };
		store.Save(first);

		// A directory where the temp file must go makes the write step fail.
		Directory.CreateDirectory(Path.Combine(_root, "node-1.json.tmp"));
		var second = new AttentionLedgerModel { NodeId = "node-1", NextNumber = 9 };

		Assert.ThrowsAny<Exception>(() => store.Save(second));
		Assert.Equal(2, new AttentionLedgerStore(_root).Load("node-1").NextNumber);
		Assert.Single(new AttentionLedgerStore(_root).Load("node-1").Items);
	}

	[Fact]
	public void Save_CreatesRootAndKeepsNodesApart()   // TC.FV.01-5
	{
		var store = new AttentionLedgerStore(_root);
		Assert.False(Directory.Exists(_root));

		store.Save(new AttentionLedgerModel { NodeId = "a", NextNumber = 2, Items = [FullItem(1, AttentionKind.Ask)] });
		store.Save(new AttentionLedgerModel { NodeId = "b", NextNumber = 1 });

		Assert.True(File.Exists(Path.Combine(_root, "a.json")));
		Assert.True(File.Exists(Path.Combine(_root, "b.json")));
		Assert.Single(store.Load("a").Items);
		Assert.Empty(store.Load("b").Items);
	}

	[Fact]
	public async Task ConcurrentPostsThroughTheService_LeaveValidFileWithDistinctItems()   // TC.FV.01-6
	{
		using var env = new AttentionTestEnv();
		var node = AttentionTestEnv.Node();

		await Task.WhenAll(Enumerable.Range(1, 20).Select(n =>
			Task.Run(() => env.Service.Post(node, [AttentionTestEnv.Req($"Item {n}")]))));

		var loaded = new AttentionLedgerStore(env.Root).Load(node.NodeId);
		Assert.Equal(20, loaded.Items.Count);
		Assert.Equal(20, loaded.Items.Select(i => i.Id).Distinct().Count());
		Assert.Equal(21, loaded.NextNumber);
	}
}
