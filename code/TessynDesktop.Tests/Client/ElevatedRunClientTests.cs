using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services.ElevatedRuns;

namespace TessynDesktop.Tests.Client;

/// <summary>The <c>elevated.*</c> methods of <see cref="TessynClient"/> against a real loopback daemon (tdcp-spec 10.A13, FR.32.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunClientTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();
	private readonly RemoteEventBus _bus = new();
	private readonly EventLog _log;
	private readonly TdcpTestHost _host;

	public ElevatedRunClientTests()
	{
		_log = new EventLog(_bus);
		var dispatcher = new TdcpDispatcher(
		[
			new ClientRpcModule(runInfo: new DaemonRunInfo(), eventLog: _log, elevatedRuns: _env.Service),
			new ElevatedRunRpcModule(_env.Service),
		]);
		_host = new TdcpTestHost(dispatcher, bus: _bus);
	}

	public void Dispose()
	{
		_host.Dispose();
		_log.Dispose();
		_bus.Dispose();
		_env.Dispose();
	}

	private TessynClient NewClient() => new(new Uri(_host.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token);

	[Fact]
	public async Task Connect_ExposesTheElevatedRunsCapability()
	{
		await using var client = NewClient();

		await client.ConnectAsync();

		Assert.Contains(RemoteProtocol.ElevatedRuns.Capability, client.ServerInfo!.Capabilities);
	}

	[Fact]
	public async Task ListAndGet_ReturnTheRunsOfTheDaemon()
	{
		var run = await _env.RequestAsync();
		_env.MakeRunning(run.Id, 91);
		File.WriteAllText(Path.Combine(_env.RunDir(run.Id), "output.log"), "a\r\nb\r\nc\r\n");
		await using var client = NewClient();
		await client.ConnectAsync();

		var list = await client.ElevatedListAsync();
		var get  = await client.ElevatedGetAsync(run.Id, tailLines: 2);

		var listed = Assert.Single(list.Runs);
		Assert.Equal((run.Id, ElevatedRunStatus.Running, run.Command), (listed.Id, listed.Status, listed.Command));
		Assert.Equal(_env.Alpha, listed.WorkingDir);
		Assert.Equal("b\nc", get.OutputTail);
		Assert.Empty((await client.ElevatedListAsync(_env.Other.NodeId)).Runs);
	}

	[Fact]
	public async Task Approve_ThenLaunched_ThenTheDaemonSeesTheStartedFile()
	{
		var run = await _env.RequestAsync();
		await using var client = NewClient();
		await client.ConnectAsync();

		var approved = await client.ElevatedApproveAsync(run.Id);
		await client.ElevatedLaunchedAsync(run.Id);

		Assert.Equal(("powershell.exe", "runas", _env.Alpha), (approved.Launch.FileName, approved.Launch.Verb, approved.Launch.WorkingDirectory));
		Assert.StartsWith("-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ", approved.Launch.Arguments);
		_env.WriteStarted(run.Id, 92);
		_env.Service.Poll();
		Assert.Equal(ElevatedRunStatus.Running, (await client.ElevatedGetAsync(run.Id)).Run.Status);
	}

	[Fact]
	public async Task ASecondApprove_IsARpcErrorWithTheUserFacingText()
	{
		var run = await _env.RequestAsync();
		await using var client = NewClient();
		await client.ConnectAsync();
		await client.ElevatedApproveAsync(run.Id);

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.ElevatedApproveAsync(run.Id));

		Assert.Equal(RemoteProtocol.ErrorCodes.DaemonError, error.Code);
		Assert.Equal("This request is no longer pending (launching).", error.Message);
	}

	[Fact]
	public async Task LaunchFailed_Decline_AndStop_ReachTheDaemon()
	{
		var failed   = await _env.RequestAsync(command: "echo 1");
		var declined = await _env.RequestAsync(command: "echo 2");
		var running  = await _env.RequestAsync(command: "echo 3");
		_env.MakeRunning(running.Id, 93);
		await using var client = NewClient();
		await client.ConnectAsync();
		await client.ElevatedApproveAsync(failed.Id);

		await client.ElevatedLaunchFailedAsync(failed.Id, declined: true);
		await client.ElevatedDeclineAsync(declined.Id, "later");
		await client.ElevatedStopAsync(running.Id);

		Assert.Equal(ElevatedRunStatus.DeclinedAtPrompt, _env.Service.Find(failed.Id)!.Status);
		Assert.Equal(("later", ElevatedRunStatus.Declined), (_env.Service.Find(declined.Id)!.Note, _env.Service.Find(declined.Id)!.Status));
		Assert.True(File.Exists(Path.Combine(_env.RunDir(running.Id), "stop")));
	}

	[Fact]
	public void TheChangedNotification_IsReadAsATypedPayload()
	{
		var payload = JsonSerializer.SerializeToNode(new ElevatedChangedPayload("abcd1234", "node-1", ElevatedRunStatus.DeclinedAtPrompt), TdcpJsonOptions.Default)!;

		Assert.True(new RemoteNotification("elevated.changed", payload).TryGetElevatedChanged(out var read));
		Assert.Equal(("abcd1234", "node-1", ElevatedRunStatus.DeclinedAtPrompt), (read.RequestId, read.NodeId, read.Status));
		Assert.Equal("declinedAtPrompt", payload["status"]!.GetValue<string>());
		Assert.False(new RemoteNotification("session.changed", payload).TryGetElevatedChanged(out _));
	}
}
