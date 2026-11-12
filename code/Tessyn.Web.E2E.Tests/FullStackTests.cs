using System.Diagnostics;
using System.Net.Http.Json;
using Tessyn.Web.Auth;
using Tessyn.Web.E2E.Tests.Harness;
using TessynDesktop.Models;
using Xunit.Abstractions;

namespace Tessyn.Web.E2E.Tests;

/// <summary>
/// The real thing: Tessyn.Web as a child process on Kestrel (127.0.0.1:5398, Development, the production SPA from wwwroot),
/// the real RelayConnectorService dialling it over a socket, the seeded Core daemon with the fake claude, and Playwright
/// driving the SPA at 390x844. Enable with <c>TESSYN_FULLSTACK=1</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Trait("Category", "FullStack")]
public sealed class FullStackTests(ITestOutputHelper output)
{
	private const int Port = 5398;
	private const string BaseUrl = "http://127.0.0.1:5398";
	private const string Password = "fullstack correct horse battery";
	private const string Email = "vitaly@example.test";
	private const string LinkToken = "fullstack-link-token-0123456789-abcdefghij";

	private static string RepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "code", "TessynDesktop.sln")))
			dir = dir.Parent;

		return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
	}

	[FullStackFact]
	public async Task Spa_Against_Real_Server_Connector_And_Core()
	{
		var web = Path.Combine(RepoRoot(), "code", "Tessyn.Web");
		var clientApp = Path.Combine(web, "ClientApp");
		var dll = Path.Combine(web, "bin", "Debug", "net10.0", "Tessyn.Web.dll");
		Assert.True(File.Exists(dll), $"Build Tessyn.Web first: {dll}");

		if (!File.Exists(Path.Combine(web, "wwwroot", "index.html")))
			Assert.Equal(0, await RunAsync("cmd.exe", ["/c", "npm", "run", "build"], clientApp, new Dictionary<string, string>()));

		var temp = Path.Combine(Path.GetTempPath(), "tessyn-fullstack-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(temp);
		using var daemon = await DaemonHarness.CreateAsync();
		daemon.Process.Delay = TimeSpan.FromMilliseconds(300);
		daemon.Process.Reply = prompt => prompt.Contains("compact the conversation", StringComparison.OrdinalIgnoreCase)
			? "[2026-11-12T10:00:00Z] USER\nSummary of the conversation so far.\n\n[2026-11-12T10:00:05Z] ASSISTANT\nNoted."
			: "Fake reply to: " + prompt[..Math.Min(prompt.Length, 40)];

		var server = new ProcessStartInfo("dotnet", $"\"{dll}\"")
		{
			WorkingDirectory = web,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		server.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
		server.Environment["ASPNETCORE_URLS"] = BaseUrl;
		server.Environment["TessynWeb__Auth__Email"] = Email;
		server.Environment["TessynWeb__Auth__PasswordHash"] = new PasswordHasher().Hash(Password, 1000);
		server.Environment["TessynWeb__Link__TokenSha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(LinkToken)));
		server.Environment["TessynWeb__DataProtectionKeysPath"] = Path.Combine(temp, "keys");
		server.Environment["TessynWeb__Auth__EpochPath"] = Path.Combine(temp, "auth-epoch");

		using var process = Process.Start(server)!;
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.WriteLine("[web] " + e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.WriteLine("[web!] " + e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		try
		{
			using var probe = new HttpClient { BaseAddress = new Uri(BaseUrl) };
			await WaitUntilAsync(async () => (await probe.GetAsync("/healthz")).IsSuccessStatusCode);

			using var link = new RelayLink(daemon.Dispatcher, daemon.Bus, BaseUrl, LinkToken, () => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
			link.Start();
			await link.WaitForStateAsync(RelayState.Connected);

			var exit = await RunAsync(
				"cmd.exe",
				["/c", "npx", "playwright", "test", "--config", "playwright.fullstack.config.ts"],
				clientApp,
				new Dictionary<string, string>
				{
					["TESSYN_URL"] = BaseUrl,
					["TESSYN_EMAIL"] = Email,
					["TESSYN_PASSWORD"] = Password,
				});
			Assert.Equal(0, exit);
		}
		finally
		{
			if (!process.HasExited)
				process.Kill(true);

			try
			{
				Directory.Delete(temp, true);
			}
			catch (IOException)
			{
				// Temp state only.
			}
		}
	}

	private async Task<int> RunAsync(string file, string[] args, string workingDirectory, IDictionary<string, string> env)
	{
		var info = new ProcessStartInfo(file)
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		foreach (var arg in args)
			info.ArgumentList.Add(arg);
		foreach (var (key, value) in env)
			info.Environment[key] = value;

		using var process = Process.Start(info)!;
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.WriteLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.WriteLine(e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		await process.WaitForExitAsync();
		return process.ExitCode;
	}

	private static async Task WaitUntilAsync(Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
		while (true)
		{
			try
			{
				if (await condition())
					return;
			}
			catch (HttpRequestException)
			{
				// Server not up yet.
			}

			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("Server did not become healthy");

			await Task.Delay(250);
		}
	}
}
