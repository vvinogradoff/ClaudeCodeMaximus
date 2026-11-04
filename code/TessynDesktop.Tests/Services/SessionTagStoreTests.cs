using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.TG.05 — <see cref="SessionTagStore"/> (FR.24.5).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagStoreTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"tag-store-tests-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}

	private static SessionTagModel Tag(int n) => new()
	{
		Kind     = SessionTagKind.GitHubPullRequest,
		Key      = $"o/r#{n}",
		Label    = $"r PR #{n}",
		Url      = $"https://github.com/o/r/pull/{n}",
		Source   = SessionTagSource.Session,
		AddedUtc = new DateTimeOffset(2026, 11, 4, 10, 0, n, TimeSpan.Zero),
	};

	private static string Json(object o) => JsonSerializer.Serialize(o);

	[Fact]
	public void RoundTrip_FreshStoreLoadsEqualTags()   // TC.TG.05-1
	{
		var tags = new[] { Tag(1), Tag(2) };
		new SessionTagStore(_root).SaveTags("node-1", tags);

		var loaded = new SessionTagStore(_root).LoadTags("node-1");

		Assert.Equal(Json(tags), Json(loaded));
	}

	[Fact]
	public void Load_MissingFile_ReturnsNoTagsAndCreatesNothing()   // TC.TG.05-2
	{
		Assert.Empty(new SessionTagStore(_root).LoadTags("ghost"));
		Assert.Empty(new SessionTagStore(_root).LoadWorkspaces());
		Assert.False(Directory.Exists(_root) && Directory.GetFiles(_root).Length > 0);
	}

	[Fact]
	public void Load_CorruptFile_MovesItAsideAndStartsEmpty()   // TC.TG.05-3
	{
		Directory.CreateDirectory(_root);
		var path = Path.Combine(_root, "node-1.json");
		File.WriteAllText(path, "{ not json");
		var store = new SessionTagStore(_root);

		Assert.Empty(store.LoadTags("node-1"));

		Assert.False(File.Exists(path));
		var bad = Assert.Single(Directory.GetFiles(_root, "node-1.json.bad-*"));
		Assert.Matches(@"node-1\.json\.bad-\d{14}$", bad);
		Assert.Equal("{ not json", File.ReadAllText(bad));

		store.SaveTags("node-1", [Tag(1)]);
		Assert.True(File.Exists(path));
		Assert.Equal("{ not json", File.ReadAllText(bad));
	}

	[Fact]
	public void Save_LeavesOnlyTheTagFile()   // TC.TG.05-4
	{
		new SessionTagStore(_root).SaveTags("node-1", [Tag(1)]);

		Assert.Equal("node-1.json", Path.GetFileName(Assert.Single(Directory.GetFiles(_root))));
	}

	[Fact]
	public void Save_WhenTheTempStepFails_PreviousFileStillLoads()   // TC.TG.05-4
	{
		var store = new SessionTagStore(_root);
		store.SaveTags("node-1", [Tag(1)]);
		Directory.CreateDirectory(Path.Combine(_root, "node-1.json.tmp"));

		Assert.ThrowsAny<Exception>(() => store.SaveTags("node-1", [Tag(1), Tag(2)]));

		Assert.Single(new SessionTagStore(_root).LoadTags("node-1"));
	}

	[Fact]
	public void Save_CreatesRootAndKeepsNodesApart()   // TC.TG.05-5
	{
		var store = new SessionTagStore(_root);
		Assert.False(Directory.Exists(_root));

		store.SaveTags("a", [Tag(1)]);
		store.SaveTags("b", [Tag(2), Tag(3)]);

		Assert.Single(store.LoadTags("a"));
		Assert.Equal(2, store.LoadTags("b").Count);
		Assert.True(File.Exists(Path.Combine(_root, "a.json")) && File.Exists(Path.Combine(_root, "b.json")));
	}

	[Fact]
	public void WorkspaceMap_RoundTripsAndCorruptFileIsMovedAside()   // TC.TG.04-5 (store side)
	{
		var store = new SessionTagStore(_root);
		store.SaveWorkspaces(new System.Collections.Generic.Dictionary<string, string> { ["HSE"] = "hardstop" });
		Assert.Equal("hardstop", new SessionTagStore(_root).LoadWorkspaces()["HSE"]);

		File.WriteAllText(Path.Combine(_root, "linear-workspaces.json"), "{ not json");
		Assert.Empty(store.LoadWorkspaces());
		Assert.Single(Directory.GetFiles(_root, "linear-workspaces.json.bad-*"));
	}
}
