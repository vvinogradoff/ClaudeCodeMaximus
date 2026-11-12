using System.Diagnostics;
using System.Text;

namespace Tessyn.Web.Tests;

/// <summary>Runs the real server executable (dll) as a child process for the CLI verbs and start-up validation.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

	private static async Task<ProcessResult> RunAsync(string stdin, params string[] args)
	{
		var dll = typeof(Program).Assembly.Location;
		var info = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = Path.GetDirectoryName(dll)!,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
		};
		info.ArgumentList.Add(dll);
		foreach (var arg in args)
			info.ArgumentList.Add(arg);
		info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
		info.Environment.Remove(Constants.Config.ExtraConfigVariable);

		using var process = Process.Start(info)!;
		await process.StandardInput.WriteAsync(stdin);
		process.StandardInput.Close();
		var stdout = process.StandardOutput.ReadToEndAsync();
		var stderr = process.StandardError.ReadToEndAsync();
		using var cts = new CancellationTokenSource(Timeout);
		await process.WaitForExitAsync(cts.Token);
		return new ProcessResult(process.ExitCode, await stdout, await stderr);
	}

	[Fact]
	public async Task Hash_password_verb_runs_before_the_host_starts()
	{
		var result = await RunAsync("hunter2\n", Constants.Cli.HashPasswordVerb);

		Assert.Equal(0, result.ExitCode);
		Assert.StartsWith("pbkdf2-sha256$600000$", result.Stdout.Trim(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task New_link_token_verb_prints_token_and_hash()
	{
		var result = await RunAsync(string.Empty, Constants.Cli.NewLinkTokenVerb);

		Assert.Equal(0, result.ExitCode);
		Assert.Contains(Constants.Cli.TokenLabel, result.Stdout, StringComparison.Ordinal);
		Assert.Contains(Constants.Cli.TokenSha256Label, result.Stdout, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Starting_without_credentials_exits_non_zero_with_a_clear_message()
	{
		var result = await RunAsync(string.Empty, "--urls", "http://127.0.0.1:0");

		Assert.Equal(1, result.ExitCode);
		Assert.Contains("TessynWeb:Auth:PasswordHash", result.Stderr, StringComparison.Ordinal);
		Assert.Contains("TessynWeb:Link:TokenSha256", result.Stderr, StringComparison.Ordinal);
	}
}
