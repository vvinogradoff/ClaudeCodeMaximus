using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.6.22, FR.29.26: the docking test hook's pipe: it exists only under <c>--test-window</c>, answers one JSON line per request, and is gone after Stop.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingTestHookTests
{
	private static bool PipeExists(string name) => Directory.EnumerateFiles(@"\\.\pipe\").Any(f => f.EndsWith(name, StringComparison.Ordinal));

	private static DockingTestHook NewHook(bool testWindow, FakeDockingTestOperations ops) =>
		new(StartupOptions.Parse(testWindow ? ["--test-window"] : []), new DockingTestRequestHandler(ops), work => Task.FromResult(work()), NullLogger.Instance);

	[Fact]
	public void WithoutTestWindow_NoPipeIsCreated()
	{
		using var hook = NewHook(false, new FakeDockingTestOperations());

		Assert.False(hook.Start());

		Assert.False(hook.IsRunning);
		Assert.False(PipeExists(hook.PipeName));
	}

	[Fact]
	public void ThePipeName_IsThePrefixAndTheProcessId()
	{
		using var hook = NewHook(true, new FakeDockingTestOperations());

		Assert.Equal("tessyn-wpf-test-" + Environment.ProcessId, hook.PipeName);
		Assert.Equal(Constants.DockingTestHook.PipeNamePrefix + Environment.ProcessId, hook.PipeName);
	}

	[Fact]
	public async Task WithTestWindow_ARequestLineGetsOneReplyLine_RepeatedlyAndOnAnotherConnection_AndTheCallRunsThroughTheUiRunner()
	{
		var ops = new FakeDockingTestOperations();
		var ran = 0;
		using var hook = new DockingTestHook(StartupOptions.Parse(["--test-window"]), new DockingTestRequestHandler(ops), work =>
		{
			ran++;
			return Task.FromResult(work());
		}, NullLogger.Instance);

		Assert.True(hook.Start());
		Assert.False(hook.Start());   // already running
		try
		{
			await Exchange(hook.PipeName, "{\"op\":\"dock\",\"panel\":\"recent\",\"arg\":\"left\"}", "{\"op\":\"state\"}", "garbage");
			var reply = await Exchange(hook.PipeName, "{\"op\":\"close\",\"panel\":\"tags\"}");

			Assert.Equal(["dock recent left", "close tags"], ops.Calls);
			Assert.Contains("\"ok\":true", reply[0]);
			Assert.Equal(4, ran);
		}
		finally
		{
			hook.Stop();
		}

		Assert.False(hook.IsRunning);
		Assert.False(PipeExists(hook.PipeName));
	}

	private static async Task<List<string>> Exchange(string pipeName, params string[] requests)
	{
		await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		await client.ConnectAsync(timeout.Token);
		using var reader = new StreamReader(client, new UTF8Encoding(false));
		await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
		var replies = new List<string>();
		foreach (var request in requests)
		{
			await writer.WriteLineAsync(request);
			replies.Add((await reader.ReadLineAsync(timeout.Token))!);
		}

		return replies;
	}
}
