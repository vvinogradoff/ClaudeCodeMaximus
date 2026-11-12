using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionIndexUpdaterTests
{
	private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	private readonly InProcFakeFileService _fileService = new();
	private readonly InProcFakeRegistry _registry = new();
	private readonly InProcFakeIndexService _indexService = new();
	private readonly SessionIndexUpdater _updater;

	public SessionIndexUpdaterTests()
	{
		_updater = new SessionIndexUpdater(_fileService, _registry, _indexService, Debounce);
		_updater.Start();
	}

	private int CallCount()
	{
		lock (_indexService.IndexSessionCalls)
			return _indexService.IndexSessionCalls.Count;
	}

	private async Task WaitForCallsAsync(int expected)
	{
		var deadline = DateTime.UtcNow + Timeout;
		while (CallCount() < expected && DateTime.UtcNow < deadline)
			await Task.Delay(10);
	}

	/// <summary>Waits well past the debounce window to prove no (further) call arrives.</summary>
	private static Task SettleAsync() => Task.Delay(Debounce * 6);

	private SessionNodeModel AddSession(string fileName, string? externalId, string? claudeSessionId, string workingDirectory, string? originalProjectPath = null)
	{
		var model = new SessionNodeModel
		{
			NodeId = fileName,
			Name = fileName,
			FileName = fileName,
			ExternalId = externalId,
			ClaudeSessionId = claudeSessionId,
			WorkingDirectory = workingDirectory,
			OriginalProjectPath = originalProjectPath,
		};
		_registry.ModelsByFileName[fileName] = model;
		return model;
	}

	[Fact]
	public async Task TurnCompleted_IndexesSessionOnce_WithExternalIdAndWorkingDirectory()
	{
		AddSession("a.txt", "ext-a", null, "/work/a");

		_fileService.NotifyTurnCompleted("a.txt", false, null);
		await WaitForCallsAsync(1);
		await SettleAsync();

		var call = Assert.Single(_indexService.IndexSessionCalls);
		Assert.Equal("ext-a", call.ExternalId);
		Assert.Equal("/work/a", call.WorkingDirectory);
	}

	[Fact]
	public async Task TurnCompleted_PrefersOriginalProjectPath()
	{
		AddSession("a.txt", "ext-a", null, "/work/a", originalProjectPath: "/origin/a");

		_fileService.NotifyTurnCompleted("a.txt", false, null);
		await WaitForCallsAsync(1);

		Assert.Equal("/origin/a", Assert.Single(_indexService.IndexSessionCalls).WorkingDirectory);
	}

	[Fact]
	public async Task TurnCompleted_UsesClaudeSessionId_WhenExternalIdIsNull()
	{
		AddSession("a.txt", null, "claude-a", "/work/a");

		_fileService.NotifyTurnCompleted("a.txt", false, null);
		await WaitForCallsAsync(1);

		Assert.Equal("claude-a", Assert.Single(_indexService.IndexSessionCalls).ExternalId);
	}

	[Fact]
	public async Task TurnCompleted_UnknownFile_DoesNotIndex()
	{
		_fileService.NotifyTurnCompleted("unknown.txt", false, null);
		await SettleAsync();

		Assert.Equal(0, CallCount());
	}

	[Fact]
	public async Task TurnCompleted_SessionWithoutAnyId_DoesNotIndex()
	{
		AddSession("a.txt", null, null, "/work/a");

		_fileService.NotifyTurnCompleted("a.txt", false, null);
		await SettleAsync();

		Assert.Equal(0, CallCount());
	}

	[Fact]
	public async Task TurnCompleted_Burst_IndexesOnce()
	{
		AddSession("a.txt", "ext-a", null, "/work/a");

		// A debounce far longer than any scheduling stall on a loaded machine, so the five notifications always fall into
		// one window; the fixture's short-debounce updater is replaced for this test.
		_updater.Stop();
		var burstUpdater = new SessionIndexUpdater(_fileService, _registry, _indexService, TimeSpan.FromSeconds(1));
		burstUpdater.Start();
		try
		{
			for (var i = 0; i < 5; i++)
				_fileService.NotifyTurnCompleted("a.txt", false, null);
			await WaitForCallsAsync(1);
			await SettleAsync();

			Assert.Equal(1, CallCount());
		}
		finally
		{
			burstUpdater.Stop();
		}
	}

	[Fact]
	public async Task TurnCompleted_DifferentSessions_IndexedIndependently()
	{
		AddSession("a.txt", "ext-a", null, "/work/a");
		AddSession("b.txt", "ext-b", null, "/work/b");

		_fileService.NotifyTurnCompleted("a.txt", false, null);
		_fileService.NotifyTurnCompleted("b.txt", false, null);
		await WaitForCallsAsync(2);
		await SettleAsync();

		Assert.Equal(["ext-a", "ext-b"], _indexService.IndexSessionCalls.Select(c => c.ExternalId).OrderBy(x => x).ToArray());
	}

	[Fact]
	public async Task Stop_PreventsFurtherIndexing_AndCancelsPending()
	{
		AddSession("a.txt", "ext-a", null, "/work/a");

		_fileService.NotifyTurnCompleted("a.txt", false, null);
		_updater.Stop();
		_updater.Stop(); // idempotent
		_fileService.NotifyTurnCompleted("a.txt", false, null);
		await SettleAsync();

		Assert.Equal(0, CallCount());
	}

	[Fact]
	public async Task Start_CalledTwice_IndexesOnlyOnce()
	{
		AddSession("a.txt", "ext-a", null, "/work/a");
		_updater.Start();

		_fileService.NotifyTurnCompleted("a.txt", false, null);
		await WaitForCallsAsync(1);
		await SettleAsync();

		Assert.Equal(1, CallCount());
	}

	[Fact]
	public async Task IndexSessionThrowing_DoesNotEscapeTheUpdater()
	{
		AddSession("a.txt", "ext-a", null, "/work/a");
		_indexService.IndexSessionException = new InvalidOperationException("boom");

		_fileService.NotifyTurnCompleted("a.txt", false, null);
		await WaitForCallsAsync(1);
		await SettleAsync();

		Assert.Equal(1, CallCount()); // reached the index service, failure was swallowed
	}
}
