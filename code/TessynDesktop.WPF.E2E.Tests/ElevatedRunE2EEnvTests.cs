using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Tests.Client;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// The daemon side of the elevated-run E2E tests, without the app: a real client over the loopback host sees the capability, the card's action and the change events, and
/// the wrapper really runs a command. If this fails the WPF tests of TC.ER.56 to TC.ER.61 cannot work; it starts no window and takes no focus.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunE2EEnvTests : IDisposable
{
	private readonly ElevatedRunE2EEnv _env = new();

	public void Dispose() => _env.Dispose();

	[Fact]
	public async Task A_request_made_with_the_tool_reaches_a_client_as_an_item_with_the_action_and_a_change_event()
	{
		await using var client = new TessynClient(_env.Host.Endpoint, ClientTestHost.Token);
		var changes = new List<RemoteNotification>();
		client.NotificationReceived += (_, n) => { lock (changes) changes.Add(n); };
		await client.ConnectAsync();
		Assert.Contains(RemoteProtocol.ElevatedRuns.Capability, client.ServerInfo!.Capabilities);

		var id = await _env.RequestAsync("cmd /c echo hi & exit 3", "Check the exit code");

		var items = (await client.AttentionListAsync(_env.Lead.NodeId)).Items;
		var item = Assert.Single(items);
		Assert.Equal(RemoteProtocol.ElevatedRuns.ActionType, item.Action!.Type);
		Assert.Equal(id, item.Action.Run.Id);
		Assert.Equal("cmd /c echo hi & exit 3", item.Action.Run.Command);
		Assert.Equal(_env.WorkDir, item.Action.Run.WorkingDir);

		var launch = (await client.ElevatedApproveAsync(id)).Launch;
		Assert.Equal("runas", launch.Verb);
		Assert.Equal(_env.WorkDir, launch.WorkingDirectory);
		Assert.Equal(ElevatedRunStatus.Launching, (await client.ElevatedGetAsync(id)).Run.Status);

		// What the fake launcher of the app does: start the launch description unelevated, hidden.
		using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launch.FileName, launch.Arguments)
		{
			WorkingDirectory = launch.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
		})!;
		await client.ElevatedLaunchedAsync(id);

		var done = await _env.WaitForStatusAsync(id, TimeSpan.FromSeconds(90), ElevatedRunStatus.Completed);
		Assert.NotNull(done);
		Assert.Equal(3, done.ExitCode);

		var outcome = _env.OutcomeFor(_env.Lead.NodeId, id);
		Assert.NotNull(outcome);
		Assert.Contains($"[Elevated run {id} completed]", outcome);
		Assert.Contains("Exit code: 3", outcome);
		Assert.Contains("hi", outcome);

		lock (changes)
		{
			Assert.Contains(changes, n => n.TryGetElevatedChanged(out var p) && p.RequestId == id && p.Status == ElevatedRunStatus.Completed);
			Assert.Contains(changes, n => n.Method == RemoteProtocol.Events.AttentionChanged);
		}
	}
}
