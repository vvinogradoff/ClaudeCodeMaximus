using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
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

	private static DockingTestHook NewHook(Func<Func<string>, Task<string>> runOnUi) =>
		new(StartupOptions.Parse(["--test-window"]), new DockingTestRequestHandler(new FakeDockingTestOperations()), runOnUi, NullLogger.Instance);

	[Fact]
	public async Task AFailureReply_IsValidJson_WhateverTheMessageHolds()
	{
		const string message = "bad\ttab \"quoted\" back\\slash \u0001 line\nbreak";
		using var hook = NewHook(_ => throw new InvalidOperationException(message));
		hook.Start();
		try
		{
			var reply = (await Exchange(hook.PipeName, "{\"op\":\"state\"}"))[0];

			using var json = JsonDocument.Parse(reply);
			Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
			Assert.Equal(message, json.RootElement.GetProperty("error").GetString());
		}
		finally
		{
			hook.Stop();
		}
	}

	[Fact]
	public async Task ARequestLineOverTheLimit_IsRefused_AndTheConnectionEnds()
	{
		using var hook = NewHook(work => Task.FromResult(work()));
		hook.Start();
		try
		{
			await using var client = new NamedPipeClientStream(".", hook.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			await client.ConnectAsync(timeout.Token);
			using var reader = new StreamReader(client, new UTF8Encoding(false));
			var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

			try
			{
				await writer.WriteLineAsync(new string('x', Constants.DockingTestHook.MaxRequestLineChars + 10));
			}
			catch (IOException)
			{
				// the hook refused the line and closed while the rest was still being written
			}

			var reply = await reader.ReadLineAsync(timeout.Token);
			using var json = JsonDocument.Parse(reply!);
			Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
			Assert.Contains(Constants.DockingTestHook.MaxRequestLineChars.ToString(), json.RootElement.GetProperty("error").GetString());
			Assert.Null(await reader.ReadLineAsync(timeout.Token));   // closed
		}
		finally
		{
			hook.Stop();
		}
	}

	[Fact]
	public async Task ALineAtTheLimit_IsServed()
	{
		using var hook = NewHook(work => Task.FromResult(work()));
		hook.Start();
		try
		{
			const string prefix = "{\"op\":\"state\",\"pad\":\"";
			var request = prefix + new string('x', Constants.DockingTestHook.MaxRequestLineChars - prefix.Length - 2) + "\"}";
			Assert.Equal(Constants.DockingTestHook.MaxRequestLineChars, request.Length);

			var reply = (await Exchange(hook.PipeName, request))[0];

			Assert.Contains("\"ok\":true", reply);
		}
		finally
		{
			hook.Stop();
		}
	}

	[Fact]
	public async Task Stop_WhileARequestWaitsForTheUiThread_DoesNotWaitForIt()
	{
		var neverRuns = new TaskCompletionSource<string>();   // the UI thread is busy running Stop itself
		using var hook = NewHook(_ => neverRuns.Task);
		hook.Start();
		await using var client = new NamedPipeClientStream(".", hook.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		await client.ConnectAsync(timeout.Token);
		await using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
		await writer.WriteLineAsync("{\"op\":\"state\"}");
		await Task.Delay(300, timeout.Token);   // the request is now waiting for the UI

		var watch = System.Diagnostics.Stopwatch.StartNew();
		hook.Stop();
		watch.Stop();

		Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Stop took {watch.Elapsed}");
		Assert.False(hook.IsRunning);
		Assert.False(PipeExists(hook.PipeName));
	}

	[Fact]
	public async Task ThePipe_DeniesNetworkLogons_AndAllowsTheCurrentUser()
	{
		using var hook = NewHook(work => Task.FromResult(work()));
		hook.Start();
		try
		{
			await using var client = new NamedPipeClientStream(".", hook.PipeName, PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.None, HandleInheritability.None);
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			await client.ConnectAsync(timeout.Token);

			var rules = client.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();

			var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
			Assert.Contains(rules, r => r.AccessControlType == AccessControlType.Deny && r.IdentityReference.Equals(network));
			Assert.Contains(rules, r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference.Equals(WindowsIdentity.GetCurrent().User));
			Assert.DoesNotContain(rules, r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.WorldSid, null)));
		}
		finally
		{
			hook.Stop();
		}
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
