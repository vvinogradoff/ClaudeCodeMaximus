using System.Diagnostics;
using TessynDesktop.Client;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// <see cref="DaemonDiscovery"/> reading a <c>daemon.json</c> written by the real <see cref="DaemonDiscoveryFile"/> and a token
/// written by the real <see cref="TdcpTokenStore"/>, all in a temp directory (never the real %AppData%).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonDiscoveryTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "cm_test_discovery_" + Path.GetRandomFileName());
	private readonly string _daemonFile;
	private readonly string _tokenFile;

	public DaemonDiscoveryTests()
	{
		Directory.CreateDirectory(_dir);
		_daemonFile = Path.Combine(_dir, "daemon.json");
		_tokenFile  = Path.Combine(_dir, "tdcp-token.dat");
	}

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	[Fact]
	public async Task Finds_the_daemon_written_by_the_real_discovery_file_and_decrypts_the_token_of_the_real_store()
	{
		var run = new DaemonRunInfo();
		var tokens = new TdcpTokenStore(_tokenFile);
		tokens.Save("secret-token-1");
		new DaemonDiscoveryFile(run, tokens, _daemonFile).Write(51234);

		var result = await new DaemonDiscovery(_daemonFile).DiscoverAsync();

		Assert.Equal(DaemonDiscoveryStatus.Found, result.Status);
		var daemon = result.Daemon!;
		Assert.Equal(new Uri("http://127.0.0.1:51234/tdcp"), daemon.Endpoint);
		Assert.Equal("secret-token-1", daemon.Token);
		Assert.Equal(run.RunId, daemon.RunId);
		Assert.Equal(run.ProcessId, daemon.Pid);
	}

	[Fact]
	public async Task A_discovered_daemon_can_be_connected_to()
	{
		using var host = new ClientTestHost();
		var tokens = new TdcpTokenStore(_tokenFile);
		tokens.Save(ClientTestHost.Token);
		new DaemonDiscoveryFile(host.RunInfo, tokens, _daemonFile).Write(host.Port);

		var result = await new DaemonDiscovery(_daemonFile).DiscoverAsync();
		await using var client = new TessynClient(result.Daemon!);
		await client.ConnectAsync();

		Assert.Equal(TessynClientState.Connected, client.State);
		Assert.Equal(result.Daemon!.RunId, client.ServerInfo!.RunId);
	}

	[Fact]
	public async Task No_file_is_NoDaemonFile()
	{
		var result = await new DaemonDiscovery(_daemonFile).DiscoverAsync();

		Assert.Equal(DaemonDiscoveryStatus.NoDaemonFile, result.Status);
		Assert.Null(result.Daemon);
	}

	[Theory]
	[InlineData("not json at all")]
	[InlineData("{}")]
	[InlineData("""{"endpoint":"not a url","pid":1,"protocolVersion":1,"runId":"r","tokenFile":"t"}""")]
	public async Task An_unusable_file_is_InvalidFile(string content)
	{
		await File.WriteAllTextAsync(_daemonFile, content);

		var result = await new DaemonDiscovery(_daemonFile).DiscoverAsync();

		Assert.Equal(DaemonDiscoveryStatus.InvalidFile, result.Status);
	}

	[Fact]
	public async Task A_file_left_behind_by_a_dead_process_is_StaleFile()
	{
		var tokens = new TdcpTokenStore(_tokenFile);
		tokens.Save("t");
		var deadPid = await StartAndWaitForExitAsync();
		await File.WriteAllTextAsync(_daemonFile,
			$$"""{"endpoint":"http://127.0.0.1:5000/tdcp","pid":{{deadPid}},"protocolVersion":1,"runId":"r","tokenFile":{{System.Text.Json.JsonSerializer.Serialize(_tokenFile)}}}""");

		var result = await new DaemonDiscovery(_daemonFile).DiscoverAsync();

		Assert.Equal(DaemonDiscoveryStatus.StaleFile, result.Status);
	}

	[Fact]
	public async Task A_missing_or_corrupt_token_file_is_TokenUnavailable()
	{
		var run = new DaemonRunInfo();
		var tokens = new TdcpTokenStore(_tokenFile); // nothing saved yet
		new DaemonDiscoveryFile(run, tokens, _daemonFile).Write(5000);

		Assert.Equal(DaemonDiscoveryStatus.TokenUnavailable, (await new DaemonDiscovery(_daemonFile).DiscoverAsync()).Status);

		await File.WriteAllBytesAsync(_tokenFile, [1, 2, 3, 4]);

		Assert.Equal(DaemonDiscoveryStatus.TokenUnavailable, (await new DaemonDiscovery(_daemonFile).DiscoverAsync()).Status);
	}

	private static async Task<int> StartAndWaitForExitAsync()
	{
		using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
		var pid = process.Id;
		await process.WaitForExitAsync();
		return pid;
	}
}
