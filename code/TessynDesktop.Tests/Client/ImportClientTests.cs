using System.IO;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// The package J4 wrappers of <see cref="TessynClient"/> (global search, import, schedules, the attention list of all sessions, the daemon health)
/// and the typed <c>schedule.changed</c> notification, against a real loopback daemon.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportClientTests : IDisposable
{
	private static readonly DateTimeOffset FireAt = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

	private readonly ImportWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private TessynClient NewClient() =>
		new(new Uri(_env.Host.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token);

	[Fact]
	public async Task Hello_ReportsTheFiveCapabilities()
	{
		await using var client = NewClient();

		await client.ConnectAsync();

		foreach (var capability in new[] { RemoteProtocol.Search.Capability, RemoteProtocol.Import.Capability, RemoteProtocol.Schedules.Capability, RemoteProtocol.AttentionAll.Capability, RemoteProtocol.Health.Capability })
			Assert.Contains(capability, client.ServerInfo!.Capabilities);
	}

	[Fact]
	public async Task SearchGlobalAsync_SendsTheQueryAndPaging_AndReadsTheHits()
	{
		_env.Import.Index.SearchResult = [.. Enumerable.Range(0, 5).Select(i => new SessionSearchResultModel { ExternalId = "s" + i, ProjectSlug = "p", Content = "text " + i, Role = "user", Rank = -i })];
		await using var client = NewClient();
		await client.ConnectAsync();

		var page = await client.SearchGlobalAsync("needle", projectSlug: "p", limit: 2, offset: 1);

		Assert.Equal(["s1", "s2"], page.Hits.Select(h => h.ExternalId));
		Assert.Equal(5, page.Total);
		Assert.Equal(("needle", "p"), (_env.Import.Index.LastSearchQuery, _env.Import.Index.LastSearchProjectSlug));
	}

	[Fact]
	public async Task SearchReindexAsync_StartsAScan()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var result = await client.SearchReindexAsync();

		Assert.True(result.Started);
	}

	[Fact]
	public async Task ImportListAsync_ListsADirectorysSessions_AndFiltersThem()
	{
		_env.AddClaudeSession("claude-a", "needle one");
		_env.AddClaudeSession("claude-b", "other");
		await using var client = NewClient();
		await client.ConnectAsync();

		var all = await client.ImportListAsync(_env.Directory.NodeId);
		var filtered = await client.ImportListAsync(_env.Directory.NodeId, query: "needle");

		Assert.Equal(2, all.Total);
		Assert.Equal(["claude-a"], filtered.Items.Select(i => i.ExternalId));
	}

	[Fact]
	public async Task ImportListAsync_Global_NeedsAQuery_AndAnswersFromTheIndex()
	{
		_env.AddClaudeSession("claude-a", "First prompt", slug: "C--Other");
		_env.Import.Index.SearchResult = [new SessionSearchResultModel { ExternalId = "claude-a", ProjectSlug = "C--Other", Content = "x", Role = "user" }];
		await using var client = NewClient();
		await client.ConnectAsync();

		var hit = await client.ImportListAsync(global: true, query: "First");
		var refused = await Assert.ThrowsAsync<TessynRpcException>(() => client.ImportListAsync(global: true));

		Assert.Equal("C--Other", Assert.Single(hit.Items).ProjectSlug);
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, refused.Code);
	}

	[Fact]
	public async Task ImportTitlesAsync_ReadsTheCachedTitles()
	{
		_env.Import.Importer.CacheTitle("claude-a", "Known");
		await using var client = NewClient();
		await client.ConnectAsync();

		var result = await client.ImportTitlesAsync(["claude-a"]);

		Assert.Equal([new ImportTitleDto("claude-a", "Known")], result.Titles);
		Assert.Equal(0, result.Pending);
	}

	[Fact]
	public async Task ImportRunAsync_ImportsAndReportsTheFailures()
	{
		_env.AddClaudeSession("claude-a", "First prompt");
		await using var client = NewClient();
		await client.ConnectAsync();

		var result = await client.ImportRunAsync(_env.Directory.NodeId, [new ImportRequestDto("claude-a", "Mine"), new ImportRequestDto("ghost")]);

		var imported = Assert.Single(result.Imported);
		Assert.Equal("claude-a", imported.ExternalId);
		Assert.Equal(("ghost", "Session not found"), (Assert.Single(result.Failed).ExternalId, result.Failed[0].Reason));
		Assert.Equal(imported.NodeId, _env.Directory.Sessions.Single(s => s.Name == "Mine").NodeId);
	}

	[Fact]
	public async Task ImportRunAsync_AnUnknownTarget_IsATessynRpcException_WithoutAPath()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.ImportRunAsync("nope", []));

		Assert.DoesNotContain(_env.Root, error.Message);
	}

	[Fact]
	public async Task Schedules_AddListRemove_RoundTrip()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var at = await client.SchedulesAddAsync(_env.Session.NodeId, "wake", ScheduleKindDto.At, fireAtUtc: FireAt, note: "morning", missedFire: MissedFireDto.Skip);
		var cron = await client.SchedulesAddAsync(_env.Session.NodeId, "tick", ScheduleKindDto.Cron, cron: "0 9 * * *", maxFires: 3);
		var listed = await client.SchedulesListAsync(_env.Session.NodeId);
		var removed = await client.SchedulesRemoveAsync(at.Id);

		Assert.Equal((ScheduleKindDto.At, FireAt, "morning", MissedFireDto.Skip), (at.Kind, at.FireAtUtc, at.Note, at.MissedFire));
		Assert.Equal((ScheduleKindDto.Cron, "0 9 * * *", 3), (cron.Kind, cron.Cron, cron.MaxFires));
		Assert.Equal([at.Id, cron.Id], listed.Schedules.Select(s => s.Id));
		Assert.True(removed.Removed);
		Assert.Equal([cron.Id], (await client.SchedulesListAsync()).Schedules.Select(s => s.Id));
	}

	[Fact]
	public async Task SchedulesAddAsync_ThePicksTravelOverTheWire_AndComeBackInTheList_OnlyWhenPicked()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var picked = await client.SchedulesAddAsync(_env.Session.NodeId, "wake", ScheduleKindDto.At, fireAtUtc: FireAt, profileId: "work", modelId: "claude-opus-5-5", effort: "high");
		var plain = await client.SchedulesAddAsync(_env.Session.NodeId, "tick", ScheduleKindDto.At, fireAtUtc: FireAt);
		var listed = (await client.SchedulesListAsync(_env.Session.NodeId)).Schedules;

		Assert.Equal(("work", "claude-opus-5-5", "high"), (picked.ProfileId, picked.ModelId, picked.Effort));
		Assert.Equal((null, null, null), (plain.ProfileId, plain.ModelId, plain.Effort));
		Assert.Equal(("work", "claude-opus-5-5", "high"), listed.Where(s => s.Id == picked.Id).Select(s => (s.ProfileId, s.ModelId, s.Effort)).Single());
	}

	[Fact]
	public async Task SchedulesAddAsync_AnInvalidSchedule_IsATessynRpcException_NamingTheField()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.SchedulesAddAsync(_env.Session.NodeId, "p", ScheduleKindDto.Cron, cron: "not a cron"));

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, error.Code);
		Assert.Contains("cron", error.Message);
	}

	[Fact]
	public async Task AttentionListAllAsync_FiltersByStatusAndKind_AndPages()
	{
		var second = _env.Import.Registry.AddSession(_env.Directory, "Second");
		_env.Attention.Service.Post(_env.Session, [AttentionTestEnv.Req("ask", AttentionKind.Ask), AttentionTestEnv.Req("todo", AttentionKind.Todo)]);
		_env.Attention.Service.Post(second, [AttentionTestEnv.Req("critical", AttentionKind.Critical)]);
		await using var client = NewClient();
		await client.ConnectAsync();

		var all = await client.AttentionListAllAsync();
		var narrowed = await client.AttentionListAllAsync(AttentionStatus.Open, [AttentionKind.Todo, AttentionKind.Critical]);
		var firstPage = await client.AttentionListAllAsync(limit: 2);
		var secondPage = await client.AttentionListAllAsync(limit: 2, afterId: firstPage.NextAfterId);
		var closed = await client.AttentionListAllAsync(AttentionStatus.Dismissed);

		Assert.Equal(["critical", "ask", "todo"], all.Items.Select(i => i.Item.Title));
		Assert.Equal(["critical", "todo"], narrowed.Items.Select(i => i.Item.Title));
		Assert.Equal(["critical", "ask"], firstPage.Items.Select(i => i.Item.Title));
		Assert.Equal(["todo"], secondPage.Items.Select(i => i.Item.Title));
		Assert.Null(secondPage.NextAfterId);
		Assert.Empty(closed.Items);
	}

	[Fact]
	public async Task DaemonHealthAsync_ReadsTheVitalSigns()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var health = await client.DaemonHealthAsync();

		Assert.Equal(client.ServerInfo!.RunId, health.RunId);
		Assert.Equal("idle", health.Index.State);
		Assert.Equal(1234, health.LogBytes);
		Assert.True(health.WorkingSetMb > 0);
	}

	[Fact]
	public async Task TheTypedNotification_ReportsScheduleChanged()
	{
		await using var client = NewClient();
		var probe = new ClientProbe(client);
		await client.ConnectAsync();

		_env.Bus.Publish(RemoteProtocol.Schedules.ChangedEvent, new ScheduleChangedPayload(_env.Session.NodeId));
		RemoteNotification notification;
		do
		{
			notification = await probe.NextNotificationAsync();
		}
		while (!notification.TryGetScheduleChanged(out _));

		Assert.True(notification.TryGetScheduleChanged(out var payload));
		Assert.Equal(new ScheduleChangedPayload(_env.Session.NodeId), payload);
		Assert.False(new RemoteNotification("tree.changed", new System.Text.Json.Nodes.JsonObject()).TryGetScheduleChanged(out _));
	}
}
