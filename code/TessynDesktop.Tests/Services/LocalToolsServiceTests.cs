using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The J1 local tool profile service (FR.12.21) and the git-origin cache behind the move rule.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LocalToolsServiceTests : IDisposable
{
	private readonly TreeMutationTestEnv _env = new();
	private readonly LocalToolsService _service;
	private readonly DirectoryNodeModel _dir;

	public LocalToolsServiceTests()
	{
		_service = new LocalToolsService(_env.Registry, _env.Settings);
		_dir     = _env.AddDirectory("proj");
	}

	public void Dispose() => _env.Dispose();

	[Fact]
	public void Get_WithoutAStoredProfile_AnswersTheDefaultLeanProfile_AndTheCatalogs()
	{
		_env.Settings.Settings.ExternalMcpServers.Add(new ExternalMcpServerModel { Id = "mcp-1", DisplayName = "Linear" });

		var profile = _service.Get(_dir.NodeId);

		Assert.True(profile.Enabled);
		Assert.Equal(Constants.LocalTools.DefaultNativeTools, profile.NativeTools);
		Assert.False(profile.IncludeCmxMcp);
		Assert.Empty(profile.IncludedMcpServerIds);
		Assert.Equal(Constants.LocalTools.Catalog, profile.AvailableNativeTools);
		Assert.Equal("Linear", Assert.Single(profile.AvailableMcpServers).DisplayName);
		Assert.Null(_dir.LocalToolProfile);
	}

	[Fact]
	public void Set_StoresTheProfile_AnswersItBack_AndRaisesATreeChange()
	{
		_env.Settings.Settings.ExternalMcpServers.Add(new ExternalMcpServerModel { Id = "mcp-1", DisplayName = "Linear" });
		var before = _env.TreeChanges;

		var result = _service.Set(_dir.NodeId, enabled: false, ["Read", "Bash", "Read"], includeCmxMcp: true, ["mcp-1", "gone"]);

		var stored = _dir.LocalToolProfile!;
		Assert.False(stored.Enabled);
		Assert.Equal(["Read", "Bash"], stored.NativeTools);
		Assert.True(stored.IncludeCmxMcp);
		Assert.Equal(["mcp-1"], stored.IncludedExternalMcpServerIds);
		Assert.Equal((false, true), (result.Enabled, result.IncludeCmxMcp));
		Assert.Equal(["Read", "Bash"], result.NativeTools);
		Assert.Equal(1, _env.TreeChanges - before);
		var again = _service.Get(_dir.NodeId);
		Assert.Equal((result.Enabled, result.IncludeCmxMcp), (again.Enabled, again.IncludeCmxMcp));
		Assert.Equal(result.NativeTools, again.NativeTools);
		Assert.Equal(result.IncludedMcpServerIds, again.IncludedMcpServerIds);
	}

	[Fact]
	public void Set_RefusesAnUnknownNativeTool_AndStoresNothing()
	{
		var error = Assert.Throws<TdcpInvalidParamsException>(() => _service.Set(_dir.NodeId, true, ["Read", "Rm"], false, []));

		Assert.Contains("Rm", error.Message);
		Assert.Null(_dir.LocalToolProfile);
	}

	[Fact]
	public void GetAndSet_AnUnknownDirectory_AreRefused()
	{
		Assert.Equal(Constants.TreeMutation.DirectoryNotFound, Assert.Throws<TdcpException>(() => _service.Get("nope")).Message);
		Assert.Equal(Constants.TreeMutation.DirectoryNotFound, Assert.Throws<TdcpException>(() => _service.Set("nope", true, [], false, [])).Message);
	}

	[Fact]
	public void Get_HidesAStoredServerIdThatIsNoLongerRegistered()
	{
		_dir.LocalToolProfile = new LocalToolProfileModel { IncludedExternalMcpServerIds = ["gone"] };

		Assert.Empty(_service.Get(_dir.NodeId).IncludedMcpServerIds);
	}

	// ── DirectoryGitOrigins ──────────────────────────────────────────────────

	[Fact]
	public async Task DirectoryGitOrigins_ResolvesOnceThenServesTheCache_AndARefreshReplacesIt()
	{
		var service = new CountingGitOriginService { Origin = "first" };
		var cache   = new DirectoryGitOrigins(service);

		Assert.Equal("first", cache.GetOrigin(@"C:\a"));
		Assert.Equal("first", cache.GetOrigin(@"c:\A"));
		Assert.Equal(1, service.Reads);

		service.Origin = "second";
		var completed  = 0;
		var task       = cache.StartRefresh([@"C:\a"], () => completed++);
		await task!.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Equal(1, completed);
		Assert.Equal("second", cache.GetOrigin(@"C:\a"));
	}

	[Fact]
	public async Task DirectoryGitOrigins_AStartWhileARefreshRuns_IsANoOp_AndAnUnreadablePathHasNoOrigin()
	{
		var gate    = new ManualResetEventSlim();
		var service = new CountingGitOriginService { Origin = "o", Block = gate };
		var cache   = new DirectoryGitOrigins(service);

		var running = cache.StartRefresh([@"C:\a"], () => { });
		var second  = cache.StartRefresh([@"C:\a"], () => { });
		gate.Set();
		await running!.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Null(second);
		service.Throw = true;
		Assert.Null(cache.GetOrigin(@"C:\unreadable"));
	}

	private sealed class CountingGitOriginService : IGitOriginService
	{
		public string? Origin { get; set; }
		public int Reads { get; private set; }
		public bool Throw { get; set; }
		public ManualResetEventSlim? Block { get; set; }

		public string? GetOriginUrl(string directoryPath)
		{
			Reads++;
			Block?.Wait(TimeSpan.FromSeconds(10));
			return Throw ? throw new System.IO.IOException("unreadable") : Origin;
		}
	}
}
