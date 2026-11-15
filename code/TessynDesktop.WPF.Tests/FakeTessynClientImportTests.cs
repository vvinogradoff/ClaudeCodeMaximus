using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The package J4 members of <see cref="FakeTessynClient"/>: scriptable, recording, and usable through <see cref="ITessynClient"/> as the WPF packages will.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeTessynClientImportTests
{
	[Fact]
	public async Task EveryJ4Method_HasADefaultAnswer_AndIsRecorded()
	{
		var fake = new FakeTessynClient();
		ITessynClient client = fake;

		var search = await client.SearchGlobalAsync("needle");
		var reindex = await client.SearchReindexAsync();
		var list = await client.ImportListAsync("dir-1");
		var titles = await client.ImportTitlesAsync(["a"]);
		var run = await client.ImportRunAsync("dir-1", [new ImportRequestDto("a"), new ImportRequestDto("b", "Named")]);
		var schedules = await client.SchedulesListAsync("node-1");
		var added = await client.SchedulesAddAsync("node-1", "wake", ScheduleKindDto.Cron, cron: "* * * * *", maxFires: 2);
		var removed = await client.SchedulesRemoveAsync(added.Id);
		var attention = await client.AttentionListAllAsync(AttentionStatus.Open);
		var health = await client.DaemonHealthAsync();

		Assert.Empty(search.Hits);
		Assert.True(reindex.Started);
		Assert.Empty(list.Items);
		Assert.Empty(titles.Titles);
		Assert.Equal(["a", "b"], run.Imported.Select(i => i.ExternalId));
		Assert.Empty(schedules.Schedules);
		Assert.Equal(("node-1", ScheduleKindDto.Cron, "* * * * *", 2), (added.NodeId, added.Kind, added.Cron, added.MaxFires));
		Assert.True(removed.Removed);
		Assert.Empty(attention.Items);
		Assert.Equal("idle", health.Index.State);
		Assert.Equal(
			[
				"search.global:needle", "search.reindex:", "import.list:dir-1:False:", "import.titles:a", "import.run:dir-1:a,b", "schedules.list:node-1",
				"schedules.add:node-1:Cron", $"schedules.remove:{added.Id}", "attention.listAll:Open:", "daemon.health:",
			],
			fake.Calls);
	}

	[Fact]
	public async Task TheHandlers_ScriptTheAnswers_AndSeeTheArguments()
	{
		var fake = new FakeTessynClient
		{
			SearchGlobalHandler = (query, slug, limit, offset) => new SearchGlobalResult([new SearchHitDto("x", null, slug ?? "none", query, 1, null)], 1),
			ImportTitlesHandler = ids => new ImportTitlesResult([new ImportTitleDto(ids[0], "T")], 1),
			AttentionListAllHandler = (status, kinds, limit, after) => new AttentionListAllResult([], after),
		};
		ITessynClient client = fake;

		var hit = Assert.Single((await client.SearchGlobalAsync("q", "slug-1")).Hits);
		var titles = await client.ImportTitlesAsync(["id-1"]);
		var attention = await client.AttentionListAllAsync(afterId: "n/A1");

		Assert.Equal(("q", "slug-1"), (hit.Snippet, hit.ProjectSlug));
		Assert.Equal((new ImportTitleDto("id-1", "T"), 1), (titles.Titles[0], titles.Pending));
		Assert.Equal("n/A1", attention.NextAfterId);
	}

	[Fact]
	public async Task AScriptedFailure_Surfaces()
	{
		var fake = new FakeTessynClient
		{
			SchedulesAddHandler = (_, _, _, _, _, _, _, _) => throw new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "'cron' is not a valid cron expression"),
		};

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => ((ITessynClient)fake).SchedulesAddAsync("n", "p", ScheduleKindDto.Cron, cron: "x"));

		Assert.Contains("cron", error.Message);
	}
}
